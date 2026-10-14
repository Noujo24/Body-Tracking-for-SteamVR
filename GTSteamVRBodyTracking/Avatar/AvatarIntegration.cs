using GTSteamVRBodyTracking.Behaviours;
using System;
using System.Text;
using GTSteamVRBodyTracking.Core;
using GTSteamVRBodyTracking.Tools;
using GTSteamVRBodyTracking.Status;
using GTSteamVRBodyTracking.Tracking;
using GTSteamVRBodyTracking.Extensions;
using GTSteamVRBodyTracking.Patches;
using HarmonyLib;
using UnityEngine;

namespace GTSteamVRBodyTracking.Avatar
{
    /// <summary>
    /// Drives the LOCAL gorilla's torso rotation and elbow directions through the game's own body-tracking
    /// inputs on GorillaIK (verified: usingUpdatedIK, targetBodyRot, left/rightElbowDirection,
    /// projectedBodyRotation). The values are written ONLY for the duration of GorillaIKMgr.CopyInput and
    /// restored in a Harmony finalizer, so:
    ///  * VRRig.SerializeWriteShared (network) never sees them → nothing is sent to other players;
    ///  * GTPlayer.bodyInitialHeight (body collider) never sees usingUpdatedIK = true → physics unchanged;
    ///  * when tracking is invalid or the mod is disabled nothing is written and the game's next CopyInput
    ///    returns the bones to bodyInitialRot by itself.
    /// </summary>
    internal sealed class AvatarIntegration
    {
        internal static AvatarIntegration Active;

        private const float MinElbowDistance = 0.05f;

        private readonly ModRuntime _rt;
        private Harmony _harmony;
        private GorillaIK _ik;
        private VRRig _rig;
        private float _nextLookup;
        private bool _loggedRig;
        private bool _patchFailed;
        private int _statusSignature = -1;

        // Saved originals (restored in the finalizer).
        private bool _applied;
        private bool _savedUsingUpdatedIK;
        private Quaternion _savedTargetBodyRot;
        private Vector3 _savedLeftElbow;
        private Vector3 _savedRightElbow;
        private Quaternion _savedProjectedRot;
        private bool _savedProjected;

        public bool PatchInstalled { get; private set; }
        public bool RigFound => _ik != null;
        public bool BodyActive { get; private set; }
        public bool LeftElbowActive { get; private set; }
        public bool RightElbowActive { get; private set; }
        public string StatusText { get; private set; } = "Not started";
        public string StatusWarning { get; private set; }
        public int AppliedFrames { get; private set; }

        public AvatarIntegration(ModRuntime rt)
        {
            _rt = rt;
        }

        public void InstallPatch()
        {
            try
            {
                _harmony = new Harmony(Constants.Guid);
                _harmony.CreateClassProcessor(typeof(CopyInputPatch)).Patch();
                PatchInstalled = true;
                Active = this;
                ModLog.Info("Avatar patch installed on GorillaIKMgr.CopyInput.");
            }
            catch (Exception e)
            {
                PatchInstalled = false;
                _patchFailed = true;
                ModLog.Error("Could not patch GorillaIKMgr.CopyInput; avatar integration disabled, trackers/markers still work. " + e);
            }
        }

        public void Uninstall()
        {
            Restore();
            Active = null;
            try { _harmony?.UnpatchSelf(); }
            catch (Exception e) { ModLog.Warn("Unpatch failed: " + e.Message); }
            _harmony = null;
            PatchInstalled = false;
        }

        /// <summary>Rig yaw relative to the headset's world yaw (for calibration), if the local rig is available.</summary>
        public bool TryGetRigYawOffset(out float degrees)
        {
            degrees = 0f;
            Camera cam = _rt.Tracker.Mapper.Camera;
            if (_rig == null || cam == null) return false;
            degrees = MathUtil.WrapDegrees(_rig.transform.rotation.ToCore().GetYawDegrees() - cam.transform.rotation.ToCore().GetYawDegrees());
            return true;
        }

        /// <summary>Periodic upkeep from ModRuntime (finds the rig, updates status text).</summary>
        public void Tick(float now)
        {
            if (now >= _nextLookup && (_ik == null || _rig == null))
            {
                _nextLookup = now + 2f;
                FindLocalRig();
            }
            UpdateStatusText();
        }

        private void FindLocalRig()
        {
            try
            {
                VRRig rig = VRRig.LocalRig;
                if (rig == null && GorillaTagger.Instance != null) rig = GorillaTagger.Instance.offlineVRRig;
                GorillaIK ik = rig != null ? rig.GetComponent<GorillaIK>() : null;
                _rig = rig;
                _ik = ik;
                if (ik != null && !_loggedRig)
                {
                    _loggedRig = true;
                    ModLog.Info("Local rig diagnostics: " + Describe(ik));
                }
            }
            catch (Exception e)
            {
                ModLog.Exception("find-rig", e, "Looking up the local rig failed");
            }
        }

        private static string Describe(GorillaIK ik)
        {
            string N(UnityEngine.Object o) => o != null ? o.name : "NULL";
            return $"bodyBone={N(ik.bodyBone)} parent={N(ik.bodyBone != null ? ik.bodyBone.parent : null)} " +
                   $"L upper={N(ik.leftUpperArm)} R upper={N(ik.rightUpperArm)} targetL={N(ik.targetLeft)} targetR={N(ik.targetRight)} " +
                   $"projectedBody={N(ik.projectedBodyRotation)} projL={N(ik.projectedLeftShoulderPosition)} projR={N(ik.projectedRightShoulderPosition)} " +
                   $"usingUpdatedIK={ik.usingUpdatedIK} canUseUpdatedIK={ik.canUseUpdatedIK} " +
                   $"GorillaIK.playerIK={(GorillaIK.playerIK == ik ? "this" : GorillaIK.playerIK == null ? "null" : "other")} " +
                   $"GorillaIKMgr.playerIK={(GorillaIKMgr.playerIK == null ? "null" : "set")} bodyInitialRot={ik.bodyInitialRot.eulerAngles}";
        }

        private void UpdateStatusText()
        {
            AvatarPreferences prefs = _rt.Store.Data.Avatar;
            // Only rebuild the text when one of its inputs changed (Tick runs every frame).
            int signature = (PatchInstalled ? 1 : 0) | (_patchFailed ? 2 : 0) | (_rt.Store.Data.ModEnabled ? 4 : 0) | (prefs.Enabled ? 8 : 0)
                            | (_ik != null ? 16 : 0) | (BodyActive ? 32 : 0) | (LeftElbowActive ? 64 : 0) | (RightElbowActive ? 128 : 0);
            if (signature == _statusSignature) return;
            _statusSignature = signature;
            StatusWarning = null;
            if (!PatchInstalled)
            {
                StatusText = _patchFailed ? "Patch failed (game update?) - tracker-only mode" : "Avatar patch disabled in config (tracker-only)";
                return;
            }
            if (!_rt.Store.Data.ModEnabled) { StatusText = "Off (mod disabled)"; return; }
            if (!prefs.Enabled) { StatusText = "Off (avatar integration disabled)"; return; }
            if (_ik == null) { StatusText = "Waiting for the local gorilla rig"; return; }

            var sb = new StringBuilder();
            if (BodyActive) sb.Append("torso");
            if (LeftElbowActive) sb.Append(sb.Length > 0 ? ", " : "").Append("left elbow");
            if (RightElbowActive) sb.Append(sb.Length > 0 ? ", " : "").Append("right elbow");
            StatusText = sb.Length > 0 ? "Active: " + sb : "Idle (no valid supported tracker)";
            if (sb.Length == 0) StatusWarning = "Avatar idle: assign and calibrate a Chest/Waist tracker or assign an elbow tracker. Feet, knees and hips have no bone on the gorilla rig (markers only).";
        }

        /// <summary>Harmony prefix body: writes the overrides for the local IK only.</summary>
        internal void Apply()
        {
            _applied = false;
            BodyActive = LeftElbowActive = RightElbowActive = false;

            GorillaIK ik = _ik;
            AvatarPreferences prefs = _rt.Store.Data.Avatar;
            if (ik == null || !ik.isActiveAndEnabled || !_rt.Store.Data.ModEnabled || !prefs.Enabled) return;
            if (ik.bodyBone == null || ik.bodyBone.parent == null || ik.myRig == null) return;
            // With usingUpdatedIK the game's GetShoulderLocalTargetPos_* dereferences these; never create a state
            // in which the game's own code would throw.
            bool projectedOk = ik.projectedBodyRotation == null
                ? ik.leftUpperArm != null && ik.leftUpperArm.parent != null && ik.rightUpperArm != null && ik.rightUpperArm.parent != null
                : ik.projectedLeftShoulderPosition != null && ik.projectedRightShoulderPosition != null;
            if (!projectedOk)
            {
                ModLog.ErrorOnce("ik-refs", "Local GorillaIK is missing shoulder references required by the game's updated IK; avatar integration stays off. " + Describe(ik));
                return;
            }

            TrackerService tracker = _rt.Tracker;
            tracker.SampleIfNeeded();
            if (!tracker.Mapper.Valid) return;

            ModStatus status = _rt.Status;
            TrackerRuntime body = prefs.DriveBodyRotation ? PickBodyTracker(tracker, status) : null;
            TrackerRuntime left = prefs.DriveElbows ? PickUsable(tracker, status, BodyRole.LeftElbow) : null;
            TrackerRuntime right = prefs.DriveElbows ? PickUsable(tracker, status, BodyRole.RightElbow) : null;
            if (body == null && left == null && right == null) return;

            // Body target (local to bodyBone.parent, same space the game uses for targetBodyRot).
            Quaternion targetBody = ik.bodyInitialRot;
            if (body != null)
            {
                Quat mount = body.Settings.Calibration.MountRotation;
                Quat bodyTracking = CalibrationMath.BodyFrame(body.FilteredTracking.Rotation, mount);
                Quat bodyWorld = (tracker.Mapper.TrackingToWorld.Rotation * bodyTracking).GetNormalized()
                                 * Quat.AngleAxis(_rt.Store.Data.Calibration.RigYawOffsetDegrees, Vec3.Up);
                targetBody = AvatarMath.BodyBoneLocalRotation(
                    bodyWorld,
                    ik.myRig.transform.rotation.ToCore(),
                    ik.bodyBone.parent.rotation.ToCore(),
                    ik.bodyInitialRot.ToCore(),
                    prefs.MaxBodyAngle).ToUnity();
            }

            // Save originals.
            _savedUsingUpdatedIK = ik.usingUpdatedIK;
            _savedTargetBodyRot = ik.targetBodyRot;
            _savedLeftElbow = ik.leftElbowDirection;
            _savedRightElbow = ik.rightElbowDirection;
            _savedProjected = ik.projectedBodyRotation != null;
            if (_savedProjected) _savedProjectedRot = ik.projectedBodyRotation.localRotation;
            _applied = true;

            // Same writes the game's own SkeletonUpdate performs (see Docs/GAME_INTEGRATION.md §5.2).
            ik.usingUpdatedIK = true;
            ik.targetBodyRot = targetBody;
            if (_savedProjected) ik.projectedBodyRotation.localRotation = targetBody;
            BodyActive = body != null;

            ik.leftElbowDirection = left != null && ElbowDirection(ik, true, left, out Vector3 l) ? l : Vector3.zero;
            LeftElbowActive = left != null && ik.leftElbowDirection != Vector3.zero;
            ik.rightElbowDirection = right != null && ElbowDirection(ik, false, right, out Vector3 r) ? r : Vector3.zero;
            RightElbowActive = right != null && ik.rightElbowDirection != Vector3.zero;

            AppliedFrames++;
        }

        /// <summary>Harmony finalizer body: always restores what Apply changed.</summary>
        internal void Restore()
        {
            if (!_applied) return;
            _applied = false;
            GorillaIK ik = _ik;
            if (ik == null) return;
            ik.usingUpdatedIK = _savedUsingUpdatedIK;
            ik.targetBodyRot = _savedTargetBodyRot;
            ik.leftElbowDirection = _savedLeftElbow;
            ik.rightElbowDirection = _savedRightElbow;
            if (_savedProjected && ik.projectedBodyRotation != null) ik.projectedBodyRotation.localRotation = _savedProjectedRot;
        }

        /// <summary>Hand→elbow direction in the shoulder space GorillaIK uses (projected shoulder when present).</summary>
        private static bool ElbowDirection(GorillaIK ik, bool isLeft, TrackerRuntime elbow, out Vector3 dir)
        {
            dir = Vector3.zero;
            Transform hand = isLeft ? ik.targetLeft : ik.targetRight;
            Transform shoulderSpace = ik.projectedBodyRotation != null
                ? (isLeft ? ik.projectedLeftShoulderPosition : ik.projectedRightShoulderPosition)
                : (isLeft ? ik.leftUpperArm : ik.rightUpperArm)?.parent;
            if (hand == null || shoulderSpace == null) return false;

            Vector3 delta = elbow.World.Position.ToUnity() - hand.position;
            if (delta.magnitude < MinElbowDistance) return false;
            dir = shoulderSpace.InverseTransformDirection(delta.normalized).normalized;
            return dir.sqrMagnitude > 0.5f;
        }

        private static TrackerRuntime PickBodyTracker(TrackerService tracker, ModStatus status) =>
            PickCalibrated(tracker, status, BodyRole.Chest) ?? PickCalibrated(tracker, status, BodyRole.Waist);

        private static TrackerRuntime PickCalibrated(TrackerService tracker, ModStatus status, BodyRole role)
        {
            TrackerRuntime t = PickUsable(tracker, status, role);
            if (t == null) return null;
            TrackerCalibration c = t.Settings.Calibration;
            return c != null && c.RoleAtCalibration == role ? t : null;
        }

        /// <summary>A tracker is usable only if enabled, uniquely assigned (no conflict) and has a CURRENT world pose.</summary>
        private static TrackerRuntime PickUsable(TrackerService tracker, ModStatus status, BodyRole role)
        {
            if (status.Conflicts.IsConflicted(role)) return null;
            int frame = Time.frameCount;
            foreach (TrackerRuntime t in tracker.Trackers)
                if (t.Role == role && t.Enabled && t.WorldValid && t.LastValidFrame == frame) return t;
            return null;
        }
    }
}
