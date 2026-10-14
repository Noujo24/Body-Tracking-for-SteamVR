using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GTSteamVRBodyTracking.Core
{
    /// <summary>Allowed ranges for every user-editable value. Enforced on UI input and on load.</summary>
    public static class ConfigLimits
    {
        public const float MaxPositionOffset = 0.5f;      // metres per axis
        public const float MaxRotationOffset = 180f;      // degrees per axis
        public const float MinSmoothing = 0f;
        public const float MaxSmoothing = 0.95f;
        public const float DefaultSmoothing = 0.35f;

        public const float MinMenuDistance = 0.3f;
        public const float MaxMenuDistance = 1.2f;
        public const float DefaultMenuDistance = 0.6f;
        public const float MinMenuScale = 0.6f;
        public const float MaxMenuScale = 1.6f;

        public const float MinMarkerSize = 0.02f;
        public const float MaxMarkerSize = 0.2f;
        public const float DefaultMarkerSize = 0.06f;

        public const float MinBodyAngle = 10f;
        public const float MaxBodyAngle = 120f;
        public const float DefaultBodyAngle = 75f;

        public const int CurrentSchemaVersion = 1;
    }

    public sealed class ModConfigData
    {
        public int SchemaVersion = ConfigLimits.CurrentSchemaVersion;
        public bool ModEnabled = true;
        public List<TrackerSettings> Trackers = new List<TrackerSettings>();
        public CalibrationInfo Calibration = new CalibrationInfo();
        public UiPreferences Ui = new UiPreferences();
        public AvatarPreferences Avatar = new AvatarPreferences();
    }

    public sealed class TrackerSettings
    {
        /// <summary>Stable identifier (OpenVR Prop_SerialNumber_String). Never a device index.</summary>
        public string Serial = string.Empty;
        public string LastKnownModel = string.Empty;

        [JsonIgnore] public BodyRole Role = BodyRole.Unassigned;

        /// <summary>Serialized as a name so unknown values can be rejected without failing the whole file.</summary>
        [JsonProperty("Role")]
        public string RoleName
        {
            get => Role.ToString();
            set
            {
                if (BodyRoles.TryParse(value, out BodyRole r)) { Role = r; RoleWasInvalid = false; }
                else { Role = BodyRole.Unassigned; RoleWasInvalid = !string.IsNullOrEmpty(value); }
            }
        }

        [JsonIgnore] public bool RoleWasInvalid;

        public bool Enabled = true;
        /// <summary>Metres, in the tracker's local frame (Unity axes).</summary>
        public Vec3 PositionOffset = Vec3.Zero;
        /// <summary>Degrees, Unity Euler convention, applied in the tracker's local frame.</summary>
        public Vec3 RotationOffset = Vec3.Zero;
        public float Smoothing = ConfigLimits.DefaultSmoothing;
        public bool ShowMarker = true;
        public TrackerCalibration Calibration;

        public TrackerSettings Clone()
        {
            var c = (TrackerSettings)MemberwiseClone();
            c.Calibration = Calibration?.Clone();
            return c;
        }

        public void ResetOffsets()
        {
            PositionOffset = Vec3.Zero;
            RotationOffset = Vec3.Zero;
        }

        /// <summary>"Reset settings": everything except identity and role assignment returns to defaults.</summary>
        public void ResetSettings()
        {
            ResetOffsets();
            Smoothing = ConfigLimits.DefaultSmoothing;
            ShowMarker = true;
            Enabled = true;
        }
    }

    public sealed class TrackerCalibration
    {
        /// <summary>Rotation from tracker frame to the neutral body frame captured at calibration.</summary>
        public Quat MountRotation = Quat.Identity;
        /// <summary>Tracker position relative to the headset (headset-yaw frame, metres) at calibration.</summary>
        public Vec3 HeadRelativePosition = Vec3.Zero;

        [JsonIgnore] public BodyRole RoleAtCalibration = BodyRole.Unassigned;

        [JsonProperty("RoleAtCalibration")]
        public string RoleAtCalibrationName
        {
            get => RoleAtCalibration.ToString();
            set => RoleAtCalibration = BodyRoles.TryParse(value, out BodyRole r) ? r : BodyRole.Unassigned;
        }

        public string CapturedUtc = string.Empty;

        public TrackerCalibration Clone() => (TrackerCalibration)MemberwiseClone();
    }

    public sealed class CalibrationInfo
    {
        public bool Valid;
        public string CapturedUtc = string.Empty;
        public float ForwardYawDegrees;
        public int TrackerCount;
        /// <summary>Yaw of the gorilla rig root relative to the headset at calibration (world, degrees).</summary>
        public float RigYawOffsetDegrees;
    }

    public sealed class UiPreferences
    {
        public float MenuDistance = ConfigLimits.DefaultMenuDistance;
        public float MenuScale = 1f;
        public bool MenuAlwaysOnTop = true;
        public bool MenuFollowsPlayer = true;
        public bool ShowDebugMarkers = true;
        public bool MarkersThroughWalls = true;
        public bool ShowMarkerLabels = true;
        public float MarkerSize = ConfigLimits.DefaultMarkerSize;
    }

    public sealed class AvatarPreferences
    {
        public bool Enabled = true;
        public bool DriveBodyRotation = true;
        public bool DriveElbows = true;
        public float MaxBodyAngle = ConfigLimits.DefaultBodyAngle;
    }

    /// <summary>Validates and repairs data loaded from disk. Never throws; reports every repair.</summary>
    public static class ConfigValidator
    {
        public static void Validate(ModConfigData data, List<string> warnings)
        {
            if (data.Trackers == null) { data.Trackers = new List<TrackerSettings>(); }
            if (data.Calibration == null) { data.Calibration = new CalibrationInfo(); }
            if (data.Ui == null) { data.Ui = new UiPreferences(); warnings.Add("UI preferences missing; defaults used."); }
            if (data.Avatar == null) { data.Avatar = new AvatarPreferences(); warnings.Add("Avatar preferences missing; defaults used."); }

            if (data.SchemaVersion > ConfigLimits.CurrentSchemaVersion)
                warnings.Add($"Config schema {data.SchemaVersion} is newer than this mod ({ConfigLimits.CurrentSchemaVersion}); unknown fields ignored.");
            data.SchemaVersion = ConfigLimits.CurrentSchemaVersion;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = data.Trackers.Count - 1; i >= 0; i--)
            {
                TrackerSettings t = data.Trackers[i];
                if (t == null) { data.Trackers.RemoveAt(i); continue; }
                t.Serial = TrackerIdentity.Normalize(t.Serial);
                if (!TrackerIdentity.IsReliable(t.Serial))
                {
                    warnings.Add($"Removed a saved tracker entry with an unusable identifier '{t.Serial}'.");
                    data.Trackers.RemoveAt(i);
                    continue;
                }
                t.LastKnownModel = t.LastKnownModel ?? string.Empty;
                if (t.RoleWasInvalid)
                    warnings.Add($"Tracker {t.Serial}: unknown saved role; set to Unassigned.");
                ValidateTracker(t, warnings);
            }

            // Duplicates: keep the first occurrence in file order.
            for (int i = 0; i < data.Trackers.Count; i++)
            {
                if (!seen.Add(data.Trackers[i].Serial))
                {
                    warnings.Add($"Duplicate saved entry for tracker {data.Trackers[i].Serial}; extra entry ignored.");
                    data.Trackers.RemoveAt(i);
                    i--;
                }
            }

            UiPreferences ui = data.Ui;
            ui.MenuDistance = Fix(ui.MenuDistance, ConfigLimits.MinMenuDistance, ConfigLimits.MaxMenuDistance, ConfigLimits.DefaultMenuDistance, "Menu distance", warnings);
            ui.MenuScale = Fix(ui.MenuScale, ConfigLimits.MinMenuScale, ConfigLimits.MaxMenuScale, 1f, "Menu scale", warnings);
            ui.MarkerSize = Fix(ui.MarkerSize, ConfigLimits.MinMarkerSize, ConfigLimits.MaxMarkerSize, ConfigLimits.DefaultMarkerSize, "Marker size", warnings);

            data.Avatar.MaxBodyAngle = Fix(data.Avatar.MaxBodyAngle, ConfigLimits.MinBodyAngle, ConfigLimits.MaxBodyAngle, ConfigLimits.DefaultBodyAngle, "Max body angle", warnings);

            CalibrationInfo cal = data.Calibration;
            cal.CapturedUtc = cal.CapturedUtc ?? string.Empty;
            if (!MathUtil.IsFinite(cal.ForwardYawDegrees)) { cal.ForwardYawDegrees = 0f; cal.Valid = false; }
            cal.RigYawOffsetDegrees = MathUtil.WrapDegrees(cal.RigYawOffsetDegrees);
            if (cal.TrackerCount < 0) cal.TrackerCount = 0;
        }

        public static void ValidateTracker(TrackerSettings t, List<string> warnings)
        {
            Vec3 p = MathUtil.Sanitize(t.PositionOffset, -ConfigLimits.MaxPositionOffset, ConfigLimits.MaxPositionOffset);
            if (!Same(p, t.PositionOffset)) warnings?.Add($"Tracker {t.Serial}: position offset clamped to ±{ConfigLimits.MaxPositionOffset} m.");
            t.PositionOffset = p;

            Vec3 r = MathUtil.Sanitize(t.RotationOffset, -ConfigLimits.MaxRotationOffset, ConfigLimits.MaxRotationOffset);
            if (!Same(r, t.RotationOffset)) warnings?.Add($"Tracker {t.Serial}: rotation offset clamped to ±{ConfigLimits.MaxRotationOffset}°.");
            t.RotationOffset = r;

            t.Smoothing = Fix(t.Smoothing, ConfigLimits.MinSmoothing, ConfigLimits.MaxSmoothing, ConfigLimits.DefaultSmoothing, $"Tracker {t.Serial} smoothing", warnings);

            if (t.Calibration != null)
            {
                TrackerCalibration c = t.Calibration;
                bool ok = c.MountRotation.IsFinite() && c.HeadRelativePosition.IsFinite();
                float norm = c.MountRotation.X * c.MountRotation.X + c.MountRotation.Y * c.MountRotation.Y +
                             c.MountRotation.Z * c.MountRotation.Z + c.MountRotation.W * c.MountRotation.W;
                if (!ok || norm < 0.5f || norm > 1.5f)
                {
                    warnings?.Add($"Tracker {t.Serial}: saved calibration was invalid and has been discarded.");
                    t.Calibration = null;
                }
                else
                {
                    c.MountRotation = c.MountRotation.GetNormalized();
                    c.CapturedUtc = c.CapturedUtc ?? string.Empty;
                }
            }
        }

        private static float Fix(float value, float min, float max, float fallback, string name, List<string> warnings)
        {
            float fixedValue = MathUtil.Sanitize(value, min, max, fallback);
            if (Math.Abs(fixedValue - value) > 1e-6f || !MathUtil.IsFinite(value))
                warnings?.Add($"{name} {value} out of range [{min}, {max}]; using {fixedValue}.");
            return fixedValue;
        }

        private static bool Same(Vec3 a, Vec3 b) =>
            a.IsFinite() && b.IsFinite() && Math.Abs(a.X - b.X) < 1e-6f && Math.Abs(a.Y - b.Y) < 1e-6f && Math.Abs(a.Z - b.Z) < 1e-6f;
    }
}
