using GTSteamVRBodyTracking.Core;
using GTSteamVRBodyTracking.Status;
using GTSteamVRBodyTracking.Tracking;
using UnityEngine;
using UnityEngine.UI;

namespace GTSteamVRBodyTracking.UI.Pages
{
    internal sealed class DashboardPage : MenuPage
    {
        private const int WarningLines = 7;

        private readonly UIToggle _modEnabled;
        private readonly UIToggle _markers;
        private readonly Text _steamVR, _trackers, _calibration, _assignments, _avatar, _coordinates;
        private readonly Text[] _warnings = new Text[WarningLines];
        private readonly Text _warningsHeader;
        private readonly UIButton _resolve;
        private int _statusRevision = -1;

        public override string Title => "Body Tracking";

        public DashboardPage(MenuController menu) : base(menu, "Dashboard")
        {
            float y = 0f;
            UIFactory.Label(Root, "Mod", 0f, y, 220f, 50f, UIStyle.FontNormal, TextAnchor.MiddleLeft, UIStyle.TextDim);
            _modEnabled = UIFactory.Toggle(Root, "Enabled", 220f, y + 2f, 280f, 46f, OnModEnabled);
            _markers = UIFactory.Toggle(Root, "Tracker markers", 520f, y + 2f, 360f, 46f, OnMarkers);
            y += 54f;
            _steamVR = Row("SteamVR", ref y);
            _trackers = Row("Trackers", ref y);
            _calibration = Row("Calibration", ref y);
            _assignments = Row("Assignments", ref y, 66f);
            _avatar = Row("Avatar", ref y);
            _coordinates = Row("Coordinates", ref y);

            y += 6f;
            UIFactory.Panel(Root, "WarningsBg", 0f, y, MenuController.ContentWidth, 330f, UIStyle.Card);
            _warningsHeader = UIFactory.Label(Root, "Warnings", 14f, y + 4f, 600f, 34f, UIStyle.FontNormal, TextAnchor.MiddleLeft, UIStyle.Text);
            for (int i = 0; i < WarningLines; i++)
                _warnings[i] = UIFactory.Label(Root, "", 14f, y + 40f + i * 41f, MenuController.ContentWidth - 28f, 40f, UIStyle.FontSmall, TextAnchor.MiddleLeft, UIStyle.TextDim);
            y += 342f;

            const float bw = 284f, bh = 66f, gap = 14f;
            UIFactory.Button(Root, "Refresh trackers", 0f, y, bw, bh, OnRefresh);
            UIFactory.Button(Root, "Configure trackers", bw + gap, y, bw, bh, () => Menu.Show(Menu.TrackerList));
            UIFactory.Button(Root, "Calibrate", 2 * (bw + gap), y, bw, bh, () => Menu.Show(Menu.Calibration));
            y += bh + gap;
            _resolve = UIFactory.Button(Root, "Resolve conflicts", 0f, y, bw, bh, () => Menu.Show(Menu.Conflicts));
            UIFactory.Button(Root, "Move menu here", bw + gap, y, bw, bh, () => Menu.Recenter());
            UIFactory.Button(Root, "Settings", 2 * (bw + gap), y, bw, bh, () => Menu.Show(Menu.Settings));
            y += bh + gap;
            UIFactory.Button(Root, "Save configuration", 0f, y, bw, bh, () => Rt.SaveConfig());
            UIFactory.Button(Root, "Restore defaults", bw + gap, y, bw, bh, OnRestoreDefaults);
            UIFactory.Button(Root, "Close menu", 2 * (bw + gap), y, bw, bh, () => Menu.Close());
        }

        private Text Row(string label, ref float y, float height = 40f)
        {
            UIFactory.Label(Root, label, 0f, y, 220f, height, UIStyle.FontNormal, TextAnchor.UpperLeft, UIStyle.TextDim);
            Text t = UIFactory.Label(Root, "", 220f, y, MenuController.ContentWidth - 220f, height, UIStyle.FontNormal, TextAnchor.UpperLeft);
            y += height;
            return t;
        }

        private void OnModEnabled(bool on)
        {
            Rt.Store.Data.ModEnabled = on;
            Rt.Store.MarkDirty();
            Rt.RefreshStatusNow();
            Menu.Notify(on ? "Mod enabled." : "Mod disabled: markers and avatar off. Tracker discovery keeps running.", on ? Severity.Info : Severity.Warning);
        }

        private void OnMarkers(bool on)
        {
            Rt.Store.Data.Ui.ShowDebugMarkers = on;
            Rt.Store.MarkDirty();
            Rt.RefreshStatusNow();
            Menu.Notify(on
                ? "Tracker markers shown."
                : "Tracker markers hidden. Tracking and the avatar keep working. Press Save configuration to keep this setting.");
        }

        private void OnRefresh()
        {
            Rt.Tracker.RequestRefresh();
            Menu.Notify("Rescanning SteamVR devices and re-verifying coordinates...");
        }

        private void OnRestoreDefaults()
        {
            Menu.Confirm.Show("Restore defaults? All assignments, offsets, calibration and preferences reset in memory. Nothing is written until you press Save.",
                "Restore defaults", () =>
                {
                    Rt.RestoreDefaults();
                    Menu.Notify("Defaults restored (not saved yet). Press Save configuration to write them.", Severity.Warning);
                });
        }

        public override void Refresh(float now)
        {
            ModStatus s = Rt.Status;
            TrackerService tracker = Rt.Tracker;

            if (_modEnabled.Value != Rt.Store.Data.ModEnabled) _modEnabled.SetValue(Rt.Store.Data.ModEnabled);
            if (_markers.Value != Rt.Store.Data.Ui.ShowDebugMarkers) _markers.SetValue(Rt.Store.Data.Ui.ShowDebugMarkers);
            _resolve.Interactable = s.Conflicts.ConflictedRoles.Count > 0;

            UIFactory.SetText(_steamVR, tracker.Session.StatusText);
            UIFactory.SetColor(_steamVR, tracker.Session.IsConnected ? UIStyle.Good : UIStyle.Bad);
            UIFactory.SetText(_trackers, $"{s.ConnectedCount} connected / {s.KnownCount} known, {s.ValidCount} with valid pose");
            UIFactory.SetColor(_trackers, s.ConnectedCount == 0 ? UIStyle.Warn : s.ValidCount < s.ConnectedCount ? UIStyle.Warn : UIStyle.Good);
            UIFactory.SetText(_calibration, s.CalibrationText);
            UIFactory.SetColor(_calibration, s.CalibrationSeverity == Severity.Info ? UIStyle.Good : UIStyle.Warn);
            UIFactory.SetText(_assignments, s.AssignmentText);
            UIFactory.SetColor(_assignments, s.Conflicts.ConflictedRoles.Count > 0 ? UIStyle.Bad : s.AssignedCount == 0 ? UIStyle.Warn : UIStyle.Text);
            UIFactory.SetText(_avatar, Rt.Avatar.StatusText);
            UIFactory.SetText(_coordinates, $"{CoordinateMapper.Describe(tracker.Mapper.Mode)}, headset agreement {tracker.Mapper.PositionError * 100f:F1} cm / {tracker.Mapper.AngleError:F1}°");

            if (_statusRevision == s.Revision) return;
            _statusRevision = s.Revision;
            int count = s.Warnings.Count;
            UIFactory.SetText(_warningsHeader, count == 0 ? "Warnings: none" : count > WarningLines ? $"Warnings ({count}, showing {WarningLines})" : $"Warnings ({count})");
            for (int i = 0; i < WarningLines; i++)
            {
                if (i < count)
                {
                    UIFactory.SetText(_warnings[i], "- " + s.Warnings[i].Text);
                    UIFactory.SetColor(_warnings[i], SeverityColor(s.Warnings[i].Severity));
                }
                else
                {
                    UIFactory.SetText(_warnings[i], "");
                }
            }
        }
    }
}
