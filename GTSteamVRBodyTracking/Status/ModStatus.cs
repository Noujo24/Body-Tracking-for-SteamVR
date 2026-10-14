using GTSteamVRBodyTracking.Behaviours;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GTSteamVRBodyTracking.Core;
using GTSteamVRBodyTracking.Tracking;

namespace GTSteamVRBodyTracking.Status
{
    internal enum Severity
    {
        Info,
        Warning,
        Error
    }

    internal struct StatusWarning
    {
        public Severity Severity;
        public string Text;
    }

    /// <summary>
    /// Aggregated, user-facing status. Evaluated a few times per second (not per frame) and read by the UI.
    /// </summary>
    internal sealed class ModStatus
    {
        private readonly List<RoleClaim> _claims = new List<RoleClaim>();
        private readonly List<TrackerSettings> _allSettings = new List<TrackerSettings>();
        private readonly StringBuilder _sb = new StringBuilder(256);

        public readonly List<StatusWarning> Warnings = new List<StatusWarning>();
        public readonly RoleConflictDetector Conflicts = new RoleConflictDetector();

        public int ConnectedCount;
        public int KnownCount;
        public int ValidCount;
        public int AssignedCount;
        public bool MixedTrackingSystems;
        public string CalibrationText = "Not calibrated";
        public Severity CalibrationSeverity = Severity.Warning;
        public string AssignmentText = "No roles assigned";
        public int Revision;

        /// <summary>Settings of every known tracker (connected or not), for assignment operations.</summary>
        public IReadOnlyList<TrackerSettings> AllSettings => _allSettings;

        public bool IsConflicted(TrackerRuntime t) =>
            t.Enabled && BodyRoles.IsUnique(t.Role) && Conflicts.IsConflicted(t.Role);

        public void Evaluate(ModRuntime rt)
        {
            TrackerService tracker = rt.Tracker;
            ConfigStore store = rt.Store;
            Warnings.Clear();
            _claims.Clear();
            _allSettings.Clear();

            ConnectedCount = 0;
            KnownCount = tracker.Trackers.Count;
            ValidCount = tracker.ValidCount;
            AssignedCount = 0;
            int unassignedConnected = 0;

            foreach (TrackerRuntime t in tracker.Trackers)
            {
                _allSettings.Add(t.Settings);
                _claims.Add(new RoleClaim(t.Key, t.Role, t.Enabled));
                if (t.Connected) ConnectedCount++;
                if (t.Role != BodyRole.Unassigned && t.Enabled) AssignedCount++;
                if (t.Connected && t.Enabled && t.Role == BodyRole.Unassigned) unassignedConnected++;
            }
            Conflicts.Evaluate(_claims);

            if (!store.Data.ModEnabled) Add(Severity.Warning, "Mod is disabled (markers and avatar off; discovery continues).");

            if (!tracker.Session.IsConnected)
                Add(Severity.Error, "SteamVR: " + tracker.Session.StatusText);
            else if (ConnectedCount == 0)
                Add(Severity.Warning, "No Vive Trackers detected. Turn them on and pair them in SteamVR, then press Refresh.");

            foreach (BodyRole role in Conflicts.ConflictedRoles)
                Add(Severity.Error, $"Role conflict: {BodyRoles.DisplayName(role)} is assigned to {Conflicts.KeysFor(role).Count} trackers (not applied). Press Resolve.");

            MixedTrackingSystems = false;
            string hmdSystem = tracker.Registry.HmdTrackingSystem;
            foreach (TrackerRuntime t in tracker.Trackers)
            {
                string id = TrackerIdentity.ShortId(t.Record.Serial);
                string name = t.Role != BodyRole.Unassigned ? BodyRoles.DisplayName(t.Role) + " tracker " + id : "Tracker " + id;

                if (!t.Connected)
                {
                    if (t.Role != BodyRole.Unassigned && t.Enabled)
                        Add(Severity.Warning, t.Record.ConnectCount == 0
                            ? $"Saved {name} not detected this session (assignment kept)."
                            : $"{name} disconnected (assignment kept).");
                    continue;
                }

                if (t.Record.DuplicateSerial)
                    Add(Severity.Error, $"Two devices report serial {t.Record.Serial}; they cannot be told apart. Settings are session-only.");
                else if (!t.Record.HasStableId)
                    Add(Severity.Warning, $"Tracker at index {t.Record.DeviceIndex} has no readable serial; its settings are session-only.");

                if (t.Enabled && t.State != TrackingState.Valid)
                    Add(Severity.Warning, $"{name}: {PoseValidity.Describe(t.State)}.");

                if (!string.IsNullOrEmpty(hmdSystem) && !string.IsNullOrEmpty(t.Record.TrackingSystem) && t.Record.TrackingSystem != hmdSystem)
                    MixedTrackingSystems = true;

                if (t.Enabled && BodyRoles.Support(t.Role) == AvatarSupport.BodyRotation)
                {
                    TrackerCalibration c = t.Settings.Calibration;
                    if (c == null) Add(Severity.Warning, $"{name} is not calibrated (torso not driven). Run Calibrate.");
                    else if (c.RoleAtCalibration != t.Role) Add(Severity.Warning, $"{name} changed role since calibration. Recalibrate.");
                }
            }

            if (MixedTrackingSystems)
                Add(Severity.Warning, $"Headset tracking '{hmdSystem}' differs from tracker tracking. Use a space-calibration tool (e.g. OpenVR Space Calibrator) or markers will be offset.");

            if (tracker.Session.IsConnected && ConnectedCount > 0 && AssignedCount == 0)
                Add(Severity.Warning, "No body roles assigned. Open Configure trackers.");
            else if (unassignedConnected > 0)
                Add(Severity.Info, $"{unassignedConnected} connected tracker(s) unassigned.");

            if (ValidCount > 0 && !tracker.Mapper.Valid)
                Add(Severity.Warning, "Game camera not found yet; markers cannot be placed.");

            string avatarWarning = rt.Avatar?.StatusWarning;
            if (!string.IsNullOrEmpty(avatarWarning)) Add(Severity.Info, avatarWarning);

            if (store.Dirty) Add(Severity.Info, "Unsaved changes. Press Save configuration.");

            // Most severe first (stable, so related lines keep their order).
            if (Warnings.Count > 1)
            {
                List<StatusWarning> sorted = Warnings.OrderByDescending(w => w.Severity).ToList();
                Warnings.Clear();
                Warnings.AddRange(sorted);
            }

            BuildAssignmentText(tracker);
            BuildCalibrationText(tracker, store);
            Revision++;
        }

        private void Add(Severity severity, string text) => Warnings.Add(new StatusWarning { Severity = severity, Text = text });

        private void BuildAssignmentText(TrackerService tracker)
        {
            _sb.Clear();
            foreach (BodyRole role in BodyRoles.All)
            {
                if (role == BodyRole.Unassigned) continue;
                int count = 0;
                bool live = false;
                foreach (TrackerRuntime t in tracker.Trackers)
                {
                    if (t.Role != role || !t.Enabled) continue;
                    count++;
                    if (t.State == TrackingState.Valid) live = true;
                }
                if (count == 0) continue;
                if (_sb.Length > 0) _sb.Append(", ");
                _sb.Append(BodyRoles.DisplayName(role));
                if (count > 1 && BodyRoles.IsUnique(role)) _sb.Append(" (CONFLICT)");
                else if (!live) _sb.Append(" (no pose)");
            }
            AssignmentText = _sb.Length == 0 ? "No roles assigned" : _sb.ToString();
        }

        private void BuildCalibrationText(TrackerService tracker, ConfigStore store)
        {
            CalibrationInfo info = store.Data.Calibration;
            int calibrated = 0, stale = 0, needed = 0;
            foreach (TrackerRuntime t in tracker.Trackers)
            {
                if (!t.Enabled || t.Role == BodyRole.Unassigned) continue;
                needed++;
                TrackerCalibration c = t.Settings.Calibration;
                if (c == null) continue;
                if (c.RoleAtCalibration == t.Role) calibrated++;
                else stale++;
            }

            if (!info.Valid || calibrated == 0)
            {
                CalibrationText = "Not calibrated";
                CalibrationSeverity = needed > 0 ? Severity.Warning : Severity.Info;
            }
            else if (stale > 0 || calibrated < needed)
            {
                CalibrationText = $"Partial: {calibrated}/{needed} assigned trackers calibrated{(stale > 0 ? ", roles changed" : "")}";
                CalibrationSeverity = Severity.Warning;
            }
            else
            {
                CalibrationText = $"Calibrated ({calibrated} tracker{(calibrated == 1 ? "" : "s")}) {FormatTime(info.CapturedUtc)}";
                CalibrationSeverity = Severity.Info;
            }
        }

        private static string FormatTime(string utc)
        {
            if (System.DateTime.TryParse(utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out System.DateTime t))
                return t.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            return string.Empty;
        }
    }
}
