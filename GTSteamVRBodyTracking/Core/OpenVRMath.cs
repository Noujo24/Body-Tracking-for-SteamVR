using System;

namespace GTSteamVRBodyTracking.Core
{
    /// <summary>
    /// OpenVR → Unity coordinate conversion.
    /// OpenVR tracking space is right-handed (+X right, +Y up, −Z forward, metres).
    /// Unity is left-handed (+X right, +Y up, +Z forward, metres).
    /// Mirroring Z (S = diag(1, 1, −1)) maps one to the other: p' = S·p, R' = S·R·S.
    /// Scale is identical (metres in both). The tracking-space ORIGIN is not assumed to match
    /// Unity's world origin; that mapping is resolved at runtime by the CoordinateMapper.
    /// </summary>
    public static class OpenVRMath
    {
        /// <summary>
        /// Converts an OpenVR HmdMatrix34_t (row-major 3x4: m0..m3 / m4..m7 / m8..m11)
        /// into a Unity-handed pose. Returns false if the matrix is not a usable rigid transform.
        /// </summary>
        public static bool TryConvertMatrix(
            float m0, float m1, float m2, float m3,
            float m4, float m5, float m6, float m7,
            float m8, float m9, float m10, float m11,
            out RigidPose pose)
        {
            pose = RigidPose.Identity;

            if (!AllFinite(m0, m1, m2, m3, m4, m5, m6, m7, m8, m9, m10, m11)) return false;

            // Reject matrices that are not (near) orthonormal; the runtime reports such poses
            // for devices that have never tracked, and they must never be applied.
            if (!IsUnit(m0, m4, m8) || !IsUnit(m1, m5, m9) || !IsUnit(m2, m6, m10)) return false;
            float det = m0 * (m5 * m10 - m6 * m9) - m1 * (m4 * m10 - m6 * m8) + m2 * (m4 * m9 - m5 * m8);
            if (det < 0.9f || det > 1.1f) return false;

            // R' = S R S: negate entries where exactly one of (row, col) is the Z axis.
            Quat rotation = Quat.FromMatrix(
                m0, m1, -m2,
                m4, m5, -m6,
                -m8, -m9, m10);

            pose = new RigidPose(new Vec3(m3, m7, -m11), rotation);
            return true;
        }

        private static bool IsUnit(float a, float b, float c)
        {
            float len = a * a + b * b + c * c;
            return len > 0.9f && len < 1.1f;
        }

        private static bool AllFinite(params float[] values)
        {
            foreach (float v in values)
                if (!MathUtil.IsFinite(v)) return false;
            return true;
        }

        /// <summary>
        /// Inverse conversion (Unity pose → OpenVR row-major 3x4). Used only by tests to
        /// prove the conversion round-trips.
        /// </summary>
        public static float[] ToOpenVRMatrix(RigidPose pose)
        {
            Quat q = pose.Rotation.GetNormalized();
            // Column vectors of the Unity rotation matrix.
            Vec3 cx = q * new Vec3(1f, 0f, 0f);
            Vec3 cy = q * Vec3.Up;
            Vec3 cz = q * Vec3.Forward;
            // Unity rows: [cx.X cy.X cz.X] [cx.Y cy.Y cz.Y] [cx.Z cy.Z cz.Z]; then S R S.
            return new[]
            {
                cx.X, cy.X, -cz.X, pose.Position.X,
                cx.Y, cy.Y, -cz.Y, pose.Position.Y,
                -cx.Z, -cy.Z, cz.Z, -pose.Position.Z
            };
        }
    }

    /// <summary>Mod-level interpretation of an OpenVR pose sample.</summary>
    public enum TrackingState
    {
        Disconnected,
        NoPose,
        Initializing,
        Calibrating,
        OutOfRange,
        RotationOnly,
        BadData,
        Valid
    }

    public static class PoseValidity
    {
        // Numeric values of Valve.VR.ETrackingResult verified in Unity.XR.OpenVR.dll.
        public const int ResultUninitialized = 1;
        public const int ResultCalibratingInProgress = 100;
        public const int ResultCalibratingOutOfRange = 101;
        public const int ResultRunningOk = 200;
        public const int ResultRunningOutOfRange = 201;
        public const int ResultFallbackRotationOnly = 300;

        /// <summary>
        /// Only "connected + bPoseIsValid + Running_OK" is considered current and safe to apply.
        /// Every other combination is reported but never used to drive markers' positions or the avatar.
        /// </summary>
        public static TrackingState Evaluate(bool deviceConnected, bool poseIsValid, int trackingResult)
        {
            if (!deviceConnected) return TrackingState.Disconnected;
            switch (trackingResult)
            {
                case ResultUninitialized: return TrackingState.Initializing;
                case ResultCalibratingInProgress: return TrackingState.Calibrating;
                case ResultCalibratingOutOfRange:
                case ResultRunningOutOfRange: return TrackingState.OutOfRange;
                case ResultFallbackRotationOnly: return TrackingState.RotationOnly;
                case ResultRunningOk: return poseIsValid ? TrackingState.Valid : TrackingState.NoPose;
                default: return TrackingState.NoPose;
            }
        }

        public static string Describe(TrackingState state)
        {
            switch (state)
            {
                case TrackingState.Disconnected: return "Disconnected";
                case TrackingState.NoPose: return "No pose";
                case TrackingState.Initializing: return "Initializing";
                case TrackingState.Calibrating: return "Calibrating";
                case TrackingState.OutOfRange: return "Out of range";
                case TrackingState.RotationOnly: return "Rotation only";
                case TrackingState.BadData: return "Bad pose data";
                case TrackingState.Valid: return "Tracking";
                default: return state.ToString();
            }
        }
    }

    /// <summary>Rules for deciding whether a device identifier is stable enough to persist.</summary>
    public static class TrackerIdentity
    {
        public const string StablePrefix = "sn:";
        public const string SessionPrefix = "session:";

        public static string Normalize(string serial) => string.IsNullOrWhiteSpace(serial) ? string.Empty : serial.Trim();

        /// <summary>
        /// A serial is reliable when it is non-empty, printable and not a known placeholder.
        /// Unreliable devices are usable for this session only and are never auto-mapped from saved config.
        /// </summary>
        public static bool IsReliable(string serial)
        {
            serial = Normalize(serial);
            if (serial.Length < 3 || serial.Length > 128) return false;
            string lower = serial.ToLowerInvariant();
            if (lower == "unknown" || lower == "none" || lower == "null" || lower == "0" || lower == "n/a") return false;
            foreach (char c in serial)
                if (c < 0x20 || c > 0x7E) return false;
            return true;
        }

        public static string StableKey(string serial) => StablePrefix + Normalize(serial);

        public static string SessionKey(uint deviceIndex) => SessionPrefix + deviceIndex.ToString();

        public static bool IsStableKey(string key) => key != null && key.StartsWith(StablePrefix, StringComparison.Ordinal);

        public static string SerialFromKey(string key) =>
            IsStableKey(key) ? key.Substring(StablePrefix.Length) : string.Empty;

        /// <summary>Short distinguishing label for UI (last 6 characters of the serial).</summary>
        public static string ShortId(string serialOrKey)
        {
            if (string.IsNullOrEmpty(serialOrKey)) return "?";
            return serialOrKey.Length <= 8 ? serialOrKey : ".." + serialOrKey.Substring(serialOrKey.Length - 6);
        }
    }
}
