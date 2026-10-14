using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GTSteamVRBodyTracking.Core;

namespace GTSteamVRBodyTracking.Tests
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class TestAttribute : Attribute { }

    internal static class Program
    {
        private static int _checks;

        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--write-examples")
            {
                WriteExamples(args[1]);
                return 0;
            }

            var tests = typeof(Program).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name)
                .ToList();

            int failed = 0;
            foreach (MethodInfo test in tests)
            {
                string name = test.DeclaringType.Name + "." + test.Name;
                try
                {
                    test.Invoke(null, null);
                    Console.WriteLine("PASS  " + name);
                }
                catch (TargetInvocationException e)
                {
                    failed++;
                    Console.WriteLine("FAIL  " + name + "\n      " + e.InnerException.Message);
                }
            }

            Console.WriteLine();
            Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed ({_checks} assertions).");
            return failed == 0 ? 0 : 1;
        }

        /// <summary>Writes example configuration files using the real serializer (so the format is exact).</summary>
        private static void WriteExamples(string dir)
        {
            Directory.CreateDirectory(dir);

            var defaults = new ModConfigData();
            File.WriteAllText(Path.Combine(dir, "defaults.trackers.json"), ConfigStore.Serialize(defaults));

            var example = new ModConfigData();
            example.Trackers.Add(new TrackerSettings
            {
                Serial = "LHR-EXAMPLE1", LastKnownModel = "VIVE Tracker 3.0 MV", Role = BodyRole.Waist, Smoothing = 0.35f,
                Calibration = new TrackerCalibration { MountRotation = Quat.Euler(0, 90, -90), HeadRelativePosition = new Vec3(0f, -0.72f, -0.05f), RoleAtCalibration = BodyRole.Waist, CapturedUtc = "2026-10-13T20:00:00.0000000Z" }
            });
            example.Trackers.Add(new TrackerSettings
            {
                Serial = "LHR-EXAMPLE2", LastKnownModel = "VIVE Tracker 3.0 MV", Role = BodyRole.LeftElbow, Smoothing = 0.2f,
                PositionOffset = new Vec3(0f, 0f, -0.04f)
            });
            example.Trackers.Add(new TrackerSettings
            {
                Serial = "LHR-EXAMPLE3", LastKnownModel = "VIVE Tracker 3.0 MV", Role = BodyRole.LeftFoot, ShowMarker = true, Smoothing = 0.5f
            });
            example.Calibration = new CalibrationInfo { Valid = true, CapturedUtc = "2026-10-13T20:00:00.0000000Z", ForwardYawDegrees = 12.5f, TrackerCount = 1, RigYawOffsetDegrees = 0f };
            File.WriteAllText(Path.Combine(dir, "example.trackers.json"), ConfigStore.Serialize(example));

            // Prove the examples load cleanly with the same validation the plugin uses.
            foreach (string f in new[] { "defaults.trackers.json", "example.trackers.json" })
            {
                var store = new ConfigStore(Path.Combine(dir, f));
                LoadStatus s = store.Load();
                Console.WriteLine($"{f}: {s} ({store.Data.Trackers.Count} trackers, {store.LastLoadMessages.Count} messages)");
            }
        }

        internal static void Check(bool condition, string message)
        {
            _checks++;
            if (!condition) throw new Exception(message);
        }

        internal static void Near(float actual, float expected, float tol, string what) =>
            Check(Math.Abs(actual - expected) <= tol, $"{what}: expected {expected}, got {actual}");

        internal static void Near(Vec3 a, Vec3 e, float tol, string what) =>
            Check(Vec3.Distance(a, e) <= tol, $"{what}: expected {e}, got {a}");

        internal static void Near(Quat a, Quat e, float tolDeg, string what) =>
            Check(Quat.AngleBetween(a, e) <= tolDeg, $"{what}: expected {e}, got {a} ({Quat.AngleBetween(a, e):F3} deg apart)");
    }

    internal static class MathTests
    {
        [Test]
        private static void EulerMatchesUnityConvention()
        {
            // Values verified against UnityEngine.Quaternion.Euler semantics (ZXY order, LH).
            Program.Near(Quat.Euler(0, 90, 0) * Vec3.Forward, new Vec3(1, 0, 0), 1e-5f, "yaw +90 forward");
            Program.Near(Quat.Euler(90, 0, 0) * Vec3.Forward, new Vec3(0, -1, 0), 1e-5f, "pitch +90 forward");
            Program.Near(Quat.Euler(0, 0, 90) * new Vec3(1, 0, 0), new Vec3(0, 1, 0), 1e-5f, "roll +90 right");
            Program.Near(Quat.Euler(0, 90, 90) * Vec3.Up, new Vec3(0, 0, 1), 1e-5f, "roll then yaw (Z before Y)");
            Program.Near(Quat.Euler(90, 90, 0) * Vec3.Forward, new Vec3(0, -1, 0), 1e-5f, "pitch then yaw (X before Y)");
        }

        [Test]
        private static void QuaternionAlgebra()
        {
            var rng = new Random(1234);
            for (int i = 0; i < 200; i++)
            {
                Quat q = RandomQuat(rng);
                Program.Near(q * q.GetInverse(), Quat.Identity, 1e-3f, "q * q^-1");
                Vec3 v = new Vec3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble(), (float)rng.NextDouble() - 0.5f);
                Program.Near((q * v).GetMagnitude(), v.GetMagnitude(), 1e-4f, "rotation preserves length");
                Program.Near(q.GetInverse() * (q * v), v, 1e-4f, "inverse undoes rotation");
            }
            Program.Near(Quat.AngleBetween(Quat.Identity, Quat.AngleAxis(37f, Vec3.Up)), 37f, 1e-3f, "AngleBetween");
            Program.Near(Quat.Slerp(Quat.Identity, Quat.AngleAxis(80f, Vec3.Up), 0.25f), Quat.AngleAxis(20f, Vec3.Up), 1e-3f, "Slerp");
        }

        [Test]
        private static void MatrixToQuaternionRoundTrip()
        {
            var rng = new Random(99);
            for (int i = 0; i < 200; i++)
            {
                Quat q = RandomQuat(rng);
                Vec3 cx = q * new Vec3(1, 0, 0), cy = q * Vec3.Up, cz = q * Vec3.Forward;
                Quat back = Quat.FromMatrix(cx.X, cy.X, cz.X, cx.Y, cy.Y, cz.Y, cx.Z, cy.Z, cz.Z);
                Program.Near(back, q, 0.01f, "FromMatrix round trip");
            }
        }

        [Test]
        private static void YawExtraction()
        {
            Program.Near(Quat.Euler(20, 70, 10).GetYawDegrees(), 70f, 1e-3f, "yaw ignores pitch/roll");
            Program.Near(Quat.Euler(0, -135, 0).GetYawDegrees(), -135f, 1e-3f, "negative yaw");
            Program.Near(Quat.Euler(90, 40, 0).GetYawDegrees(), 40f, 0.01f, "yaw when looking straight down");
        }

        [Test]
        private static void RigidPoseInverse()
        {
            var p = new RigidPose(new Vec3(1, 2, 3), Quat.Euler(10, 20, 30));
            RigidPose id = p * p.GetInverse();
            Program.Near(id.Position, Vec3.Zero, 1e-5f, "pose * inverse position");
            Program.Near(id.Rotation, Quat.Identity, 1e-3f, "pose * inverse rotation");
        }

        [Test]
        private static void AnchorMapsHeadsetExactlyAndPreservesRelativeLayout()
        {
            var hmdTracking = new RigidPose(new Vec3(0.3f, 1.6f, -0.2f), Quat.Euler(5, 30, 0));
            var cameraWorld = new RigidPose(new Vec3(-12f, 4f, 30f), Quat.Euler(5, 120, 0));
            foreach (float scale in new[] { 1f, 0.5f })
            {
                SimilarityTransform m = SimilarityTransform.FromAnchor(cameraWorld, hmdTracking, scale);
                RigidPose mapped = m.Apply(hmdTracking);
                Program.Near(mapped.Position, cameraWorld.Position, 1e-4f, "anchor maps HMD position");
                Program.Near(mapped.Rotation, cameraWorld.Rotation, 0.01f, "anchor maps HMD rotation");

                // A tracker 1 m below the headset in tracking space is scale m below the camera in world.
                var tracker = new RigidPose(hmdTracking.Position - new Vec3(0, 1, 0), Quat.Identity);
                Vec3 world = m.ApplyPoint(tracker.Position);
                Program.Near(Vec3.Distance(world, cameraWorld.Position), scale, 1e-4f, "relative distance scales");
                Program.Near(world.Y, cameraWorld.Position.Y - scale, 1e-4f, "down stays down (yaw-only anchor)");
            }
        }

        internal static Quat RandomQuat(Random rng) =>
            Quat.Euler((float)rng.NextDouble() * 360f - 180f, (float)rng.NextDouble() * 360f - 180f, (float)rng.NextDouble() * 360f - 180f);
    }

    internal static class OpenVRConversionTests
    {
        private static bool Convert(float[] m, out RigidPose p) =>
            OpenVRMath.TryConvertMatrix(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], out p);

        [Test]
        private static void IdentityAndTranslationFlipZ()
        {
            Program.Check(Convert(new float[] { 1, 0, 0, 1, 0, 1, 0, 2, 0, 0, 1, 3 }, out RigidPose p), "identity matrix accepted");
            Program.Near(p.Position, new Vec3(1, 2, -3), 1e-6f, "position Z mirrored");
            Program.Near(p.Rotation, Quat.Identity, 1e-4f, "identity rotation");
        }

        [Test]
        private static void PhysicalLeftTurnStaysALeftTurn()
        {
            // OpenVR (right-handed) +90 deg about +Y: forward (-Z) becomes -X, i.e. the device turned LEFT.
            float[] m = { 0, 0, 1, 0, 0, 1, 0, 0, -1, 0, 0, 0 };
            Program.Check(Convert(m, out RigidPose p), "rotation matrix accepted");
            // In Unity (left-handed) a left turn is a NEGATIVE yaw and forward (+Z) points to -X.
            Program.Near(p.Rotation * Vec3.Forward, new Vec3(-1, 0, 0), 1e-5f, "converted forward points left");
            Program.Near(p.Rotation, Quat.AngleAxis(-90f, Vec3.Up), 0.01f, "equals Unity AngleAxis(-90, up)");
        }

        [Test]
        private static void PitchUpStaysPitchUp()
        {
            // OpenVR +30 deg about +X (right-handed) tilts forward (-Z) upward (+Y).
            float c = (float)Math.Cos(Math.PI / 6), s = (float)Math.Sin(Math.PI / 6);
            float[] m = { 1, 0, 0, 0, 0, c, -s, 0, 0, s, c, 0 };
            Program.Check(Convert(m, out RigidPose p), "pitch matrix accepted");
            Vec3 f = p.Rotation * Vec3.Forward;
            Program.Check(f.Y > 0.49f && f.Z > 0.85f, $"converted forward tilts up: {f}");
            Program.Near(p.Rotation, Quat.Euler(-30, 0, 0), 0.01f, "equals Unity Euler(-30,0,0) (look up)");
        }

        [Test]
        private static void RoundTripRandomPoses()
        {
            var rng = new Random(7);
            for (int i = 0; i < 300; i++)
            {
                var pose = new RigidPose(new Vec3((float)rng.NextDouble() * 4 - 2, (float)rng.NextDouble() * 2, (float)rng.NextDouble() * 4 - 2), MathTests.RandomQuat(rng));
                float[] m = OpenVRMath.ToOpenVRMatrix(pose);
                Program.Check(Convert(m, out RigidPose back), "round trip accepted");
                Program.Near(back.Position, pose.Position, 1e-5f, "round trip position");
                Program.Near(back.Rotation, pose.Rotation, 0.02f, "round trip rotation");
            }
        }

        [Test]
        private static void RejectsUnusableMatrices()
        {
            Program.Check(!Convert(new float[12], out _), "all-zero matrix rejected");
            Program.Check(!Convert(new float[] { 1, 0, 0, float.NaN, 0, 1, 0, 0, 0, 0, 1, 0 }, out _), "NaN rejected");
            Program.Check(!Convert(new float[] { 2, 0, 0, 0, 0, 2, 0, 0, 0, 0, 2, 0 }, out _), "scaled matrix rejected");
            Program.Check(!Convert(new float[] { -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 }, out _), "mirroring matrix rejected");
        }

        [Test]
        private static void TrackerInFrontOfHeadsetEndsUpInFrontOfCamera()
        {
            // Headset at origin facing OpenVR forward (-Z); tracker 1 m in front of it (z = -1).
            Convert(new float[] { 1, 0, 0, 0, 0, 1, 0, 1.6f, 0, 0, 1, 0 }, out RigidPose hmd);
            Convert(new float[] { 1, 0, 0, 0, 0, 1, 0, 1.6f, 0, 0, 1, -1 }, out RigidPose tracker);
            var camera = new RigidPose(new Vec3(5, 2, 5), Quat.Euler(0, 90, 0)); // camera facing world +X
            SimilarityTransform m = SimilarityTransform.FromAnchor(camera, hmd, 1f);
            Program.Near(m.ApplyPoint(tracker.Position), new Vec3(6, 2, 5), 1e-4f, "tracker lands 1 m along camera forward");
        }

        [Test]
        private static void PoseValidityRules()
        {
            Program.Check(PoseValidity.Evaluate(true, true, PoseValidity.ResultRunningOk) == TrackingState.Valid, "connected+valid+OK");
            Program.Check(PoseValidity.Evaluate(false, true, PoseValidity.ResultRunningOk) == TrackingState.Disconnected, "disconnected never valid");
            Program.Check(PoseValidity.Evaluate(true, false, PoseValidity.ResultRunningOk) == TrackingState.NoPose, "pose flag false");
            Program.Check(PoseValidity.Evaluate(true, true, PoseValidity.ResultRunningOutOfRange) == TrackingState.OutOfRange, "out of range");
            Program.Check(PoseValidity.Evaluate(true, true, PoseValidity.ResultFallbackRotationOnly) == TrackingState.RotationOnly, "rotation only is not valid");
            Program.Check(PoseValidity.Evaluate(true, true, PoseValidity.ResultUninitialized) == TrackingState.Initializing, "uninitialized");
            Program.Check(PoseValidity.Evaluate(true, true, 12345) == TrackingState.NoPose, "unknown result code");
        }
    }

    internal static class RegistryTests
    {
        private static ScanEntry Tracker(uint idx, string serial, bool connected = true) =>
            new ScanEntry { Index = idx, Class = DeviceClassKind.GenericTracker, Connected = connected, Serial = serial, Model = "VIVE Tracker 3.0 MV" };

        private static ScanEntry Device(uint idx, DeviceClassKind cls, string serial) =>
            new ScanEntry { Index = idx, Class = cls, Connected = true, Serial = serial, Model = cls.ToString() };

        private static RegistryChanges Scan(DeviceRegistry r, params ScanEntry[] e) => r.Reconcile(e, e.Length);

        [Test]
        private static void NoTrackersAndNonTrackersIgnored()
        {
            var r = new DeviceRegistry();
            Scan(r, Device(0, DeviceClassKind.Hmd, "HMD1"), Device(1, DeviceClassKind.Controller, "C1"),
                 Device(2, DeviceClassKind.Controller, "C2"), Device(3, DeviceClassKind.TrackingReference, "LHB-1"));
            Program.Check(r.Trackers.Count == 0, "headset/controllers/base stations are not body trackers");
            Program.Check(r.HmdCount == 1 && r.ControllerCount == 2 && r.BaseStationCount == 1, "class counters");
        }

        [Test]
        private static void OneAndMultipleTrackers()
        {
            var r = new DeviceRegistry();
            RegistryChanges c = Scan(r, Tracker(4, "LHR-AAAA0001"));
            Program.Check(c.Added == 1 && r.Trackers.Count == 1 && r.Trackers[0].HasStableId, "one tracker added");
            c = Scan(r, Tracker(4, "LHR-AAAA0001"), Tracker(5, "LHR-BBBB0002"), Tracker(6, "LHR-CCCC0003"),
                     Tracker(7, "LHR-DDDD0004"), Tracker(8, "LHR-EEEE0005"), Tracker(9, "LHR-FFFF0006"));
            Program.Check(c.Added == 5 && r.Trackers.Count == 6, "no hard-coded maximum; six trackers");
            c = Scan(r, Tracker(4, "LHR-AAAA0001"), Tracker(5, "LHR-BBBB0002"), Tracker(6, "LHR-CCCC0003"),
                     Tracker(7, "LHR-DDDD0004"), Tracker(8, "LHR-EEEE0005"), Tracker(9, "LHR-FFFF0006"));
            Program.Check(!c.AnyChange && r.Trackers.Count == 6, "rescan creates no duplicates");
        }

        [Test]
        private static void DisconnectAndReconnectOnDifferentIndex()
        {
            var r = new DeviceRegistry();
            Scan(r, Tracker(4, "LHR-AAAA0001"), Tracker(5, "LHR-BBBB0002"));
            TrackerRecord a = r.Find(TrackerIdentity.StableKey("LHR-AAAA0001"));

            RegistryChanges c = Scan(r, Tracker(5, "LHR-BBBB0002"));
            Program.Check(c.Disconnected == 1 && !a.Connected && a.DeviceIndex == TrackerRecord.NoIndex, "disconnect detected, index cleared");
            Program.Check(r.Trackers.Count == 2, "disconnected stable tracker keeps its card/settings");

            c = Scan(r, Tracker(5, "LHR-BBBB0002"), Tracker(11, "LHR-AAAA0001"));
            Program.Check(c.Reconnected == 1 && c.Added == 0, "reconnect matched by serial, not index");
            Program.Check(ReferenceEquals(a, r.Find(TrackerIdentity.StableKey("LHR-AAAA0001"))) && a.DeviceIndex == 11 && a.ConnectCount == 2, "same record, new index");
        }

        [Test]
        private static void IndexReuseByAnotherDeviceDoesNotInheritIdentity()
        {
            var r = new DeviceRegistry();
            Scan(r, Tracker(4, "LHR-AAAA0001"));
            Scan(r, Tracker(4, "LHR-ZZZZ9999"));
            Program.Check(r.Trackers.Count == 2, "a different serial on the same index is a different tracker");
            Program.Check(!r.Find(TrackerIdentity.StableKey("LHR-AAAA0001")).Connected, "original tracker shown disconnected");
        }

        [Test]
        private static void UnreliableSerialIsSessionOnly()
        {
            var r = new DeviceRegistry();
            Scan(r, Tracker(4, ""), Tracker(5, "unknown"));
            Program.Check(r.Trackers.Count == 2 && r.Trackers.All(t => !t.HasStableId), "unidentifiable trackers are session-only");
            RegistryChanges c = Scan(r);
            Program.Check(c.Removed == 2 && r.Trackers.Count == 0, "session-only trackers are dropped on disconnect (cannot be re-identified)");
        }

        [Test]
        private static void DuplicateSerialsAreFlaggedNotTrusted()
        {
            var r = new DeviceRegistry();
            Scan(r, Tracker(4, "LHR-SAME0000"), Tracker(5, "LHR-SAME0000"));
            Program.Check(r.Trackers.Count == 2 && r.Trackers.All(t => t.DuplicateSerial && !t.HasStableId), "duplicate serials flagged and not stable");
        }

        [Test]
        private static void SavedPlaceholderBindsWhenDeviceAppears()
        {
            var r = new DeviceRegistry();
            TrackerRecord placeholder = r.AddSavedPlaceholder("LHR-SAVED001", "VIVE Tracker 3.0 MV");
            Program.Check(placeholder != null && !placeholder.Connected, "saved-but-missing tracker is listed as disconnected");
            Scan(r, Tracker(9, "LHR-SAVED001"));
            Program.Check(placeholder.Connected && placeholder.DeviceIndex == 9 && r.Trackers.Count == 1, "placeholder becomes the live record");
            Program.Check(r.AddSavedPlaceholder("  ", null) == null && r.Trackers.Count == 1, "unusable saved serial creates no placeholder");
            Program.Check(ReferenceEquals(r.AddSavedPlaceholder("LHR-SAVED001", null), placeholder), "no duplicate placeholder");
        }

        [Test]
        private static void ForgetOnlyDisconnected()
        {
            var r = new DeviceRegistry();
            Scan(r, Tracker(4, "LHR-AAAA0001"));
            Program.Check(!r.Forget(TrackerIdentity.StableKey("LHR-AAAA0001")), "cannot forget a connected tracker");
            Scan(r);
            Program.Check(r.Forget(TrackerIdentity.StableKey("LHR-AAAA0001")) && r.Trackers.Count == 0, "forget removes disconnected tracker");
        }

        [Test]
        private static void SessionLossMarksEverythingDisconnected()
        {
            var r = new DeviceRegistry();
            Scan(r, Tracker(4, "LHR-AAAA0001"), Tracker(5, "LHR-BBBB0002"));
            RegistryChanges c = r.MarkAllDisconnected();
            Program.Check(c.Disconnected == 2 && r.ConnectedTrackerCount == 0, "SteamVR loss disconnects all");
        }
    }

    internal static class RoleTests
    {
        [Test]
        private static void ConflictsOnlyForUniqueEnabledRoles()
        {
            var d = new RoleConflictDetector();
            d.Evaluate(new List<RoleClaim>
            {
                new RoleClaim("a", BodyRole.Chest, true),
                new RoleClaim("b", BodyRole.Chest, true),
                new RoleClaim("c", BodyRole.Waist, true),
                new RoleClaim("d", BodyRole.Waist, false),
                new RoleClaim("e", BodyRole.Custom, true),
                new RoleClaim("f", BodyRole.Custom, true),
                new RoleClaim("g", BodyRole.Unassigned, true),
                new RoleClaim("h", BodyRole.Unassigned, true)
            });
            Program.Check(d.ConflictedRoles.Count == 1 && d.IsConflicted(BodyRole.Chest), "only Chest conflicts");
            Program.Check(d.KeysFor(BodyRole.Chest).Count == 2, "both chest claimants listed");

            d.Evaluate(new List<RoleClaim> { new RoleClaim("a", BodyRole.Chest, true), new RoleClaim("b", BodyRole.LeftFoot, true) });
            Program.Check(d.ConflictedRoles.Count == 0, "reassignment clears the conflict");
        }

        [Test]
        private static void RoleParsingAndSupport()
        {
            Program.Check(BodyRoles.All.Length == 12, "all 12 roles available");
            Program.Check(BodyRoles.TryParse("LeftElbow", out BodyRole r) && r == BodyRole.LeftElbow, "parse name");
            Program.Check(!BodyRoles.TryParse("Tail", out _), "reject unknown");
            Program.Check(!BodyRoles.TryParse("42", out _), "reject undefined number");
            Program.Check(BodyRoles.Support(BodyRole.Chest) == AvatarSupport.BodyRotation, "chest drives body");
            Program.Check(BodyRoles.Support(BodyRole.RightElbow) == AvatarSupport.ElbowDirection, "elbow drives elbow");
            Program.Check(BodyRoles.Support(BodyRole.LeftFoot) == AvatarSupport.MarkerOnly, "feet unsupported by rig");
            Program.Check(!BodyRoles.IsUnique(BodyRole.Custom) && BodyRoles.IsUnique(BodyRole.LeftKnee), "uniqueness");
        }

        private static List<TrackerSettings> Three(out TrackerSettings a, out TrackerSettings b, out TrackerSettings c)
        {
            a = new TrackerSettings { Serial = "A", Role = BodyRole.Chest };
            b = new TrackerSettings { Serial = "B", Role = BodyRole.Waist };
            c = new TrackerSettings { Serial = "C", Role = BodyRole.Unassigned };
            return new List<TrackerSettings> { a, b, c };
        }

        [Test]
        private static void AssignReassignUnassign()
        {
            var all = Three(out TrackerSettings a, out TrackerSettings b, out TrackerSettings c);
            AssignmentOps.Assign(c, BodyRole.LeftFoot, all, AssignMode.MoveHere);
            Program.Check(c.Role == BodyRole.LeftFoot && a.Role == BodyRole.Chest && b.Role == BodyRole.Waist, "assign free role touches nobody");
            AssignmentOps.Assign(c, BodyRole.Unassigned, all, AssignMode.MoveHere);
            Program.Check(c.Role == BodyRole.Unassigned, "unassign");

            Program.Check(AssignmentOps.FindOtherHolder(BodyRole.Chest, all, c) == a, "holder lookup");
            int moved = AssignmentOps.Assign(c, BodyRole.Chest, all, AssignMode.MoveHere);
            Program.Check(moved == 1 && c.Role == BodyRole.Chest && a.Role == BodyRole.Unassigned, "move here unassigns previous holder");

            int swapped = AssignmentOps.Assign(b, BodyRole.Chest, all, AssignMode.Swap);
            Program.Check(swapped == 1 && b.Role == BodyRole.Chest && c.Role == BodyRole.Waist, "swap exchanges roles");

            AssignmentOps.Assign(a, BodyRole.Chest, all, AssignMode.AllowConflict);
            Program.Check(a.Role == BodyRole.Chest && b.Role == BodyRole.Chest, "explicit conflict allowed (warned in UI)");
            AssignmentOps.KeepOnly(a, BodyRole.Chest, all);
            Program.Check(a.Role == BodyRole.Chest && b.Role == BodyRole.Unassigned, "conflict resolved by keeping one");

            AssignmentOps.Assign(b, BodyRole.Custom, all, AssignMode.MoveHere);
            AssignmentOps.Assign(c, BodyRole.Custom, all, AssignMode.MoveHere);
            Program.Check(b.Role == BodyRole.Custom && c.Role == BodyRole.Custom, "Custom may be shared");
            Program.Check(AssignmentOps.UnassignAll(BodyRole.Custom, all) == 2, "unassign all");
        }
    }

    internal static class ConfigTests
    {
        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gtsvrbt-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Test]
        private static void SaveLoadRoundTrip()
        {
            string dir = TempDir();
            try
            {
                var store = new ConfigStore(Path.Combine(dir, "cfg.json"));
                Program.Check(store.Load() == LoadStatus.NotFound, "missing file → defaults");
                TrackerSettings t = store.GetOrCreate("LHR-AAAA0001", "VIVE Tracker 3.0 MV");
                t.Role = BodyRole.LeftElbow;
                t.Enabled = false;
                t.PositionOffset = new Vec3(0.01f, -0.02f, 0.03f);
                t.RotationOffset = new Vec3(10, -20, 30);
                t.Smoothing = 0.6f;
                t.ShowMarker = false;
                t.Calibration = new TrackerCalibration { MountRotation = Quat.Euler(0, 45, 0), HeadRelativePosition = new Vec3(0, -0.7f, 0.05f), RoleAtCalibration = BodyRole.LeftElbow, CapturedUtc = "2026-10-13T00:00:00Z" };
                store.Data.Calibration = new CalibrationInfo { Valid = true, ForwardYawDegrees = 33f, TrackerCount = 1, CapturedUtc = "x" };
                store.Data.Ui.MenuDistance = 0.8f;
                SaveResult res = store.Save();
                Program.Check(res.Success, "save succeeded: " + res.Message);

                var again = new ConfigStore(store.FilePath);
                Program.Check(again.Load() == LoadStatus.Loaded, "reload status");
                TrackerSettings u = again.Find("LHR-AAAA0001");
                Program.Check(u != null && u.Role == BodyRole.LeftElbow && !u.Enabled && !u.ShowMarker, "role/enabled/marker persisted");
                Program.Near(u.PositionOffset, t.PositionOffset, 1e-6f, "position offset persisted");
                Program.Near(u.RotationOffset, t.RotationOffset, 1e-6f, "rotation offset persisted");
                Program.Near(u.Smoothing, 0.6f, 1e-6f, "smoothing persisted");
                Program.Check(u.Calibration != null && u.Calibration.RoleAtCalibration == BodyRole.LeftElbow, "calibration persisted");
                Program.Near(u.Calibration.MountRotation, t.Calibration.MountRotation, 0.01f, "mount rotation persisted");
                Program.Check(again.Data.Calibration.Valid && Math.Abs(again.Data.Ui.MenuDistance - 0.8f) < 1e-6f, "global calibration and UI prefs persisted");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        private static void InvalidValuesAreRepairedOnLoad()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "cfg.json");
                File.WriteAllText(path, @"{
  ""Trackers"": [
    { ""Serial"": ""LHR-AAAA0001"", ""Role"": ""Tail"", ""PositionOffset"": { ""X"": 9, ""Y"": ""NaN"", ""Z"": -9 }, ""RotationOffset"": { ""X"": 999, ""Y"": 0, ""Z"": 0 }, ""Smoothing"": 7 },
    { ""Serial"": ""LHR-AAAA0001"", ""Role"": ""Chest"" },
    { ""Serial"": """", ""Role"": ""Chest"" },
    { ""Serial"": ""LHR-BBBB0002"", ""Role"": ""Waist"", ""Calibration"": { ""MountRotation"": { ""X"": 0, ""Y"": 0, ""Z"": 0, ""W"": 0 } } }
  ],
  ""Ui"": { ""MenuDistance"": 50 },
  ""SomethingFromTheFuture"": 1
}");
                var store = new ConfigStore(path);
                Program.Check(store.Load() == LoadStatus.LoadedWithRepairs, "repairs reported");
                Program.Check(store.Data.Trackers.Count == 2, "empty-serial and duplicate entries removed");
                TrackerSettings a = store.Find("LHR-AAAA0001");
                Program.Check(a.Role == BodyRole.Unassigned, "unknown role → Unassigned");
                Program.Near(a.PositionOffset, new Vec3(0.5f, 0f, -0.5f), 1e-6f, "position offset clamped / NaN zeroed");
                Program.Near(a.RotationOffset.X, 180f, 1e-6f, "rotation offset clamped");
                Program.Near(a.Smoothing, ConfigLimits.MaxSmoothing, 1e-6f, "smoothing clamped");
                Program.Check(store.Find("LHR-BBBB0002").Calibration == null, "degenerate calibration discarded");
                Program.Near(store.Data.Ui.MenuDistance, ConfigLimits.MaxMenuDistance, 1e-6f, "menu distance clamped");
                Program.Check(store.LastLoadMessages.Count >= 5, "every repair produces a message");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        private static void CorruptFileRecoversFromBackupAndInterruptedWriteIsHarmless()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "cfg.json");
                var store = new ConfigStore(path);
                store.Load();
                store.GetOrCreate("LHR-AAAA0001", "m").Role = BodyRole.Chest;
                Program.Check(store.Save().Success, "first save");
                store.Find("LHR-AAAA0001").Role = BodyRole.Waist;
                Program.Check(store.Save().Success, "second save");
                Program.Check(File.Exists(store.BackupPath), "backup kept");
                Program.Check(!File.Exists(store.TempPath), "temp file removed after replace");

                // Interrupted write: a garbage temp file must not affect loading.
                File.WriteAllText(store.TempPath, "{ truncated");
                var s2 = new ConfigStore(path);
                Program.Check(s2.Load() == LoadStatus.Loaded && s2.Find("LHR-AAAA0001").Role == BodyRole.Waist, "stray .tmp ignored");

                // Corrupt primary: fall back to the previous good version.
                File.WriteAllText(path, "{ not json");
                var s3 = new ConfigStore(path);
                Program.Check(s3.Load() == LoadStatus.RecoveredFromBackup, "recovered from .bak");
                Program.Check(s3.Find("LHR-AAAA0001").Role == BodyRole.Chest, "backup holds the previous save");

                // Both unreadable: defaults, and the files are left untouched.
                File.WriteAllText(store.BackupPath, "also bad");
                var s4 = new ConfigStore(path);
                Program.Check(s4.Load() == LoadStatus.Failed && s4.Data.Trackers.Count == 0, "failed load → defaults");
                Program.Check(File.ReadAllText(path) == "{ not json", "unreadable file not overwritten by loading");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        private static void RestoreDefaultsIsInMemoryUntilSaved()
        {
            string dir = TempDir();
            try
            {
                var store = new ConfigStore(Path.Combine(dir, "cfg.json"));
                store.Load();
                store.GetOrCreate("LHR-AAAA0001", "m").Role = BodyRole.Chest;
                store.Save();
                store.RestoreDefaults();
                Program.Check(store.Dirty && store.Data.Trackers.Count == 0, "defaults applied in memory");
                var other = new ConfigStore(store.FilePath);
                other.Load();
                Program.Check(other.Find("LHR-AAAA0001") != null, "file unchanged until Save");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        private static void PerTrackerSettingsAreIndependent()
        {
            var store = new ConfigStore(Path.Combine(Path.GetTempPath(), "unused.json"));
            TrackerSettings a = store.GetOrCreate("LHR-AAAA0001", "m");
            TrackerSettings b = store.GetOrCreate("LHR-BBBB0002", "m");
            a.PositionOffset = new Vec3(0.1f, 0, 0);
            a.Smoothing = 0.9f;
            Program.Check(b.PositionOffset.X == 0f && Math.Abs(b.Smoothing - ConfigLimits.DefaultSmoothing) < 1e-6f, "changing A leaves B untouched");
            a.ResetSettings();
            Program.Check(a.PositionOffset.X == 0f && Math.Abs(a.Smoothing - ConfigLimits.DefaultSmoothing) < 1e-6f, "reset settings");
            Program.Check(store.GetOrCreate("", "m") == null, "no settings for unidentifiable devices");
        }
    }

    internal static class FilterAndCalibrationTests
    {
        [Test]
        private static void ZeroSmoothingIsPassThrough()
        {
            var f = new PoseFilter();
            var p = new RigidPose(new Vec3(1, 1, 1), Quat.Euler(0, 30, 0));
            f.Process(RigidPose.Identity, Vec3.Zero, Vec3.Zero, 0f, 1 / 90f, 0f);
            RigidPose o = f.Process(p, Vec3.Zero, Vec3.Zero, 0f, 1 / 90f, 0f);
            Program.Near(o.Position, p.Position, 1e-6f, "no smoothing → raw position");
        }

        [Test]
        private static void SmoothingConvergesAndIsFrameRateIndependent()
        {
            float oneStep = PoseFilter.SmoothingToLerp(0.5f, 1 / 45f);
            float half = PoseFilter.SmoothingToLerp(0.5f, 1 / 90f);
            float twoHalfSteps = 1f - (1f - half) * (1f - half);
            Program.Near(twoHalfSteps, oneStep, 1e-5f, "frame-rate independent");

            var f = new PoseFilter();
            f.Process(RigidPose.Identity, Vec3.Zero, Vec3.Zero, 0.8f, 1 / 90f, 0f);
            var target = new RigidPose(new Vec3(0.2f, 0, 0), Quat.Identity);
            RigidPose o = f.Process(target, Vec3.Zero, Vec3.Zero, 0.8f, 1 / 90f, 0f);
            Program.Check(o.Position.X > 0f && o.Position.X < 0.2f, "smoothed position lags");
            for (int i = 0; i < 400; i++) o = f.Process(target, Vec3.Zero, Vec3.Zero, 0.8f, 1 / 90f, 0f);
            Program.Near(o.Position, target.Position, 1e-3f, "converges");
        }

        [Test]
        private static void SnapsAfterGapOrReset()
        {
            var f = new PoseFilter();
            f.Process(RigidPose.Identity, Vec3.Zero, Vec3.Zero, 0.9f, 1 / 90f, 0f);
            var target = new RigidPose(new Vec3(0.2f, 0, 0), Quat.Identity);
            RigidPose o = f.Process(target, Vec3.Zero, Vec3.Zero, 0.9f, 1 / 90f, 1.0f);
            Program.Near(o.Position, target.Position, 1e-6f, "after a tracking gap the old pose is not blended in");
            f.Reset();
            Program.Check(!f.HasState, "reset clears history");
        }

        [Test]
        private static void OffsetsAreInTrackerLocalFrame()
        {
            var raw = new RigidPose(new Vec3(1, 0, 0), Quat.Euler(0, 90, 0)); // tracker facing world +X
            RigidPose o = PoseFilter.ApplyOffsets(raw, new Vec3(0, 0, 0.1f), new Vec3(0, -90, 0));
            Program.Near(o.Position, new Vec3(1.1f, 0, 0), 1e-5f, "local +Z offset follows the tracker's facing");
            Program.Near(o.Rotation, Quat.Identity, 0.01f, "rotation offset composes locally");
        }

        [Test]
        private static void CalibrationMountYieldsHeadsetYawAndFollowsTracker()
        {
            Quat hmd = Quat.Euler(15, 40, -5);
            Quat tracker = Quat.Euler(-80, 130, 25); // arbitrary strap orientation
            Quat mount = CalibrationMath.ComputeMount(tracker, hmd);
            Program.Near(CalibrationMath.BodyFrame(tracker, mount), Quat.AngleAxis(40f, Vec3.Up), 0.01f, "body frame = upright, facing headset yaw");

            Quat turn = Quat.AngleAxis(-30f, Vec3.Up); // user rotates hips 30 deg left (world)
            Program.Near(CalibrationMath.BodyFrame(turn * tracker, mount), turn * Quat.AngleAxis(40f, Vec3.Up), 0.01f, "body follows tracker rotation");

            Quat again = CalibrationMath.ComputeMount(tracker, hmd);
            Program.Near(again, mount, 1e-3f, "recalibrating from raw data does not accumulate");
        }

        [Test]
        private static void RecenterFixesYawOnly()
        {
            Quat tracker = Quat.Euler(10, 0, 0);
            Quat mount = CalibrationMath.ComputeMount(tracker, Quat.Identity);
            Quat newHmd = Quat.Euler(0, 75, 0);
            Quat recentered = CalibrationMath.RecenterMount(mount, tracker, newHmd);
            Quat body = CalibrationMath.BodyFrame(tracker, recentered);
            Program.Near(body.GetYawDegrees(), 75f, 0.05f, "recentered yaw");
            Program.Near(body, Quat.AngleAxis(75f, Vec3.Up), 0.05f, "pitch/roll preserved (upright)");
        }

        [Test]
        private static void CaptureStabilityCheck()
        {
            var acc = new PoseAccumulator();
            for (int i = 0; i < 30; i++) acc.Add(new RigidPose(new Vec3(0.001f * i, 0, 0), Quat.AngleAxis(0.1f * i, Vec3.Up)));
            Program.Check(acc.IsStable(out _, out _), "still user accepted");
            acc.Add(new RigidPose(new Vec3(0.3f, 0, 0), Quat.Identity));
            Program.Check(!acc.IsStable(out float spread, out _) && spread > 0.05f, "moving user rejected");
        }

        [Test]
        private static void HeadRelativePositionUsesYawFrame()
        {
            var hmd = new RigidPose(new Vec3(1, 1.6f, 1), Quat.Euler(30, 90, 0));
            Vec3 waist = new Vec3(1.1f, 1.0f, 1); // 0.1 m along world +X = headset forward (yaw 90)
            Program.Near(CalibrationMath.HeadRelativePosition(waist, hmd), new Vec3(0, -0.6f, 0.1f), 1e-4f, "relative position in heading frame");
        }

        [Test]
        private static void ClampLimitsAngle()
        {
            Program.Near(CalibrationMath.ClampAngle(Quat.AngleAxis(100f, Vec3.Up), 60f).GetAngle(), 60f, 0.05f, "clamped");
            Program.Near(CalibrationMath.ClampAngle(Quat.AngleAxis(30f, Vec3.Up), 60f).GetAngle(), 30f, 0.05f, "untouched below limit");
        }

        [Test]
        private static void BodyBoneRotationNeutralAndTurned()
        {
            Quat root = Quat.AngleAxis(50f, Vec3.Up);
            Quat parent = root * Quat.Euler(-90, 0, 0); // a typical FBX parent offset
            Quat initial = Quat.Euler(0, 0, 12);
            Quat neutral = AvatarMath.BodyBoneLocalRotation(root, root, parent, initial, 75f);
            Program.Near(neutral, initial, 0.01f, "neutral body → game's own bodyInitialRot");

            Quat turn = Quat.AngleAxis(25f, Vec3.Up);
            Quat local = AvatarMath.BodyBoneLocalRotation(turn * root, root, parent, initial, 75f);
            Program.Near(parent * local, turn * parent * initial, 0.01f, "bone world rotation turns with the body");

            Quat big = Quat.AngleAxis(170f, Vec3.Up);
            Quat clamped = AvatarMath.BodyBoneLocalRotation(big * root, root, parent, initial, 75f);
            Program.Near(Quat.AngleBetween(parent * clamped, parent * initial), 75f, 0.1f, "body rotation limited by MaxBodyAngle");
        }
    }
}
