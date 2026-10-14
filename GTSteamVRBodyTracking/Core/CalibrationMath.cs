using System;
using System.Collections.Generic;

namespace GTSteamVRBodyTracking.Core
{
    /// <summary>
    /// Calibration in tracking space. The neutral body frame is "upright, facing the headset's yaw".
    /// Each tracker stores a mount rotation so that trackerRotation * mount = body frame.
    /// Values are always recomputed from RAW poses, so pressing Calibrate repeatedly never accumulates.
    /// </summary>
    public static class CalibrationMath
    {
        public static Quat ComputeMount(Quat trackerRotation, Quat hmdRotation) =>
            (trackerRotation.GetInverse() * hmdRotation.GetYawOnly()).GetNormalized();

        public static Quat BodyFrame(Quat trackerRotation, Quat mount) => (trackerRotation * mount).GetNormalized();

        /// <summary>Tracker position in the headset's yaw frame (x right, y up, z forward).</summary>
        public static Vec3 HeadRelativePosition(Vec3 trackerPosition, RigidPose hmd) =>
            hmd.Rotation.GetYawOnly().GetInverse() * (trackerPosition - hmd.Position);

        /// <summary>Yaw-only recenter: rotates the body frame about +Y so it faces the headset's current yaw.</summary>
        public static Quat RecenterMount(Quat mount, Quat trackerRotation, Quat hmdRotation)
        {
            Quat body = BodyFrame(trackerRotation, mount);
            Quat delta = (hmdRotation.GetYawOnly() * body.GetYawOnly().GetInverse()).GetNormalized();
            return (trackerRotation.GetInverse() * delta * trackerRotation * mount).GetNormalized();
        }

        /// <summary>Limits a rotation to at most <paramref name="maxDegrees"/> away from identity.</summary>
        public static Quat ClampAngle(Quat rotation, float maxDegrees)
        {
            float angle = rotation.GetAngle();
            if (angle <= maxDegrees || angle < 1e-4f) return rotation;
            return Quat.Slerp(Quat.Identity, rotation, maxDegrees / angle);
        }
    }

    /// <summary>Averages pose samples during calibration capture and rejects captures where the user moved.</summary>
    public sealed class PoseAccumulator
    {
        public const float MaxPositionSpread = 0.05f; // metres
        public const float MaxAngleSpread = 8f;       // degrees

        private readonly List<RigidPose> _samples = new List<RigidPose>();

        public int Count => _samples.Count;

        public void Clear() => _samples.Clear();

        public void Add(RigidPose pose) => _samples.Add(pose);

        public RigidPose Mean()
        {
            if (_samples.Count == 0) return RigidPose.Identity;
            Vec3 pos = Vec3.Zero;
            Quat first = _samples[0].Rotation;
            float x = 0f, y = 0f, z = 0f, w = 0f;
            foreach (RigidPose s in _samples)
            {
                pos += s.Position;
                Quat q = s.Rotation;
                if (Quat.Dot(first, q) < 0f) q = new Quat(-q.X, -q.Y, -q.Z, -q.W);
                x += q.X; y += q.Y; z += q.Z; w += q.W;
            }
            float inv = 1f / _samples.Count;
            return new RigidPose(pos * inv, new Quat(x, y, z, w).GetNormalized());
        }

        /// <summary>True when all samples stay within the allowed spread around the mean.</summary>
        public bool IsStable(out float positionSpread, out float angleSpread)
        {
            positionSpread = 0f;
            angleSpread = 0f;
            if (_samples.Count == 0) return false;
            RigidPose mean = Mean();
            foreach (RigidPose s in _samples)
            {
                positionSpread = Math.Max(positionSpread, Vec3.Distance(s.Position, mean.Position));
                angleSpread = Math.Max(angleSpread, Quat.AngleBetween(s.Rotation, mean.Rotation));
            }
            return positionSpread <= MaxPositionSpread && angleSpread <= MaxAngleSpread;
        }
    }

    /// <summary>
    /// Avatar-side rotation math for GorillaIK.bodyBone (verified: CopyOutput writes lerpBodyRot as localRotation).
    /// </summary>
    public static class AvatarMath
    {
        /// <summary>
        /// targetLocal = inverse(P) · D · P · initial, where D = bodyFrameWorld · inverse(rigRootWorld)
        /// (clamped) is the body's rotation relative to the head-driven rig root, P = bodyBone.parent world
        /// rotation, and "initial" = GorillaIK.bodyInitialRot. At the neutral pose D = identity and
        /// targetLocal = initial, i.e. the game's own default body pose.
        /// </summary>
        public static Quat BodyBoneLocalRotation(Quat bodyFrameWorld, Quat rigRootWorld, Quat boneParentWorld, Quat bodyInitialLocal, float maxDegrees)
        {
            Quat delta = (bodyFrameWorld * rigRootWorld.GetInverse()).GetNormalized();
            delta = CalibrationMath.ClampAngle(delta, maxDegrees);
            return (boneParentWorld.GetInverse() * delta * boneParentWorld * bodyInitialLocal).GetNormalized();
        }
    }
}
