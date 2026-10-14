using GTSteamVRBodyTracking.Calibration;
using GTSteamVRBodyTracking.Status;
using UnityEngine;
using UnityEngine.UI;

namespace GTSteamVRBodyTracking.UI.Pages
{
    /// <summary>Guided calibration: wear → neutral pose → face forward → confirm roles → capture → save → check → recalibrate.</summary>
    internal sealed class CalibrationPage : MenuPage
    {
        private readonly Text _step1, _step2, _phase, _result, _current;
        private readonly UIButton _start, _cancel, _save, _check, _recenter, _clear;

        public override string Title => "Calibration";

        public CalibrationPage(MenuController menu) : base(menu, "Calibration")
        {
            const float W = MenuController.ContentWidth;
            float y = 0f;
            _step1 = UIFactory.Label(Root, "", 0f, y, W, 40f, UIStyle.FontNormal);
            y += 42f;
            _step2 = UIFactory.Label(Root, "", 0f, y, W - 300f, 70f, UIStyle.FontNormal);
            UIFactory.Button(Root, "Configure trackers", W - 290f, y + 4f, 290f, 60f, () => Menu.Show(Menu.TrackerList), UIStyle.FontSmall);
            y += 78f;
            UIFactory.Label(Root,
                "3. Stand upright in a comfortable neutral pose: arms relaxed at your sides, feet shoulder-width apart.\n" +
                "4. Face the direction you want to be 'forward' and look straight ahead (your headset defines forward).\n" +
                "5. Press Start. You have 3 seconds to get into the pose, then hold still for 1 second.\n" +
                "6. Save calibration, then check that the yellow body arrows on the markers point where you face.",
                0f, y, W, 190f, UIStyle.FontSmall, TextAnchor.UpperLeft, UIStyle.TextDim);
            y += 196f;

            _phase = UIFactory.Label(Root, "", 0f, y, W, 70f, UIStyle.FontTitle, TextAnchor.MiddleCenter);
            y += 74f;
            _result = UIFactory.Label(Root, "", 0f, y, W, 130f, UIStyle.FontNormal, TextAnchor.UpperLeft);
            y += 136f;

            const float bw = 284f, bh = 66f, gap = 14f;
            _start = UIFactory.Button(Root, "Start calibration", 0f, y, bw, bh, OnStart);
            _cancel = UIFactory.Button(Root, "Cancel", bw + gap, y, bw, bh, () => Rt.Calibration.Cancel());
            _save = UIFactory.Button(Root, "Save calibration", 2 * (bw + gap), y, bw, bh, OnSave);
            y += bh + gap;
            _check = UIFactory.Button(Root, "Check alignment", 0f, y, bw, bh, OnCheck);
            _recenter = UIFactory.Button(Root, "Recenter (yaw)", bw + gap, y, bw, bh, OnRecenter);
            _clear = UIFactory.Button(Root, "Clear calibration", 2 * (bw + gap), y, bw, bh, OnClear);
            y += bh + gap + 6f;
            _current = UIFactory.Label(Root, "", 0f, y, W, 40f, UIStyle.FontSmall, TextAnchor.MiddleLeft, UIStyle.TextDim);
        }

        private void OnStart()
        {
            Rt.Calibration.Acknowledge();
            if (Rt.Calibration.Begin(Time.unscaledTime, out string reason)) Menu.Notify("Get into your neutral pose and hold still...");
            else Menu.Notify("Cannot calibrate: " + reason, Severity.Error);
        }

        private void OnSave()
        {
            Rt.Calibration.Acknowledge();
            Rt.SaveConfig();
        }

        private void OnCheck()
        {
            Rt.Store.Data.Ui.ShowDebugMarkers = true;
            Rt.Store.MarkDirty();
            Menu.Notify("Markers on. Stand facing forward: each yellow body arrow should point the way you face. If not, recalibrate or adjust rotation offsets.");
        }

        private void OnRecenter()
        {
            bool rig = Rt.Avatar.TryGetRigYawOffset(out float yaw);
            int n = Rt.Calibration.Recenter(yaw, rig, out string message);
            Menu.Notify(message, n > 0 ? Severity.Info : Severity.Warning);
        }

        private void OnClear()
        {
            Menu.Confirm.Show("Clear calibration for all trackers? The torso will stop following trackers until you calibrate again.", "Clear", () =>
            {
                Rt.Calibration.ClearAll();
                Rt.RefreshStatusNow();
                Menu.Notify("Calibration cleared (press Save to write it).", Severity.Warning);
            });
        }

        public override void Refresh(float now)
        {
            CalibrationService cal = Rt.Calibration;
            int targets = cal.CountTargets(out int valid);
            UIFactory.SetText(_step1, $"1. Connect and wear trackers: {targets} assigned and connected, {valid} with a valid pose.");
            UIFactory.SetColor(_step1, targets > 0 && valid == targets ? UIStyle.Good : UIStyle.Warn);
            UIFactory.SetText(_step2, "2. Confirm assignments: " + Rt.Status.AssignmentText);
            UIFactory.SetColor(_step2, Rt.Status.Conflicts.ConflictedRoles.Count > 0 ? UIStyle.Bad : UIStyle.Text);

            CalibrationPhase phase = cal.Phase;
            bool busy = phase == CalibrationPhase.Countdown || phase == CalibrationPhase.Capturing;
            switch (phase)
            {
                case CalibrationPhase.Countdown:
                    UIFactory.SetText(_phase, $"Get ready... {Mathf.CeilToInt(cal.CountdownRemaining(now))}");
                    UIFactory.SetColor(_phase, UIStyle.Warn);
                    break;
                case CalibrationPhase.Capturing:
                    UIFactory.SetText(_phase, $"HOLD STILL  {cal.CaptureProgress(now) * 100f:F0}%");
                    UIFactory.SetColor(_phase, UIStyle.Accent);
                    break;
                case CalibrationPhase.Succeeded:
                    UIFactory.SetText(_phase, "Calibration captured");
                    UIFactory.SetColor(_phase, UIStyle.Good);
                    break;
                case CalibrationPhase.Failed:
                    UIFactory.SetText(_phase, "Calibration failed");
                    UIFactory.SetColor(_phase, UIStyle.Bad);
                    break;
                default:
                    UIFactory.SetText(_phase, "Ready");
                    UIFactory.SetColor(_phase, UIStyle.TextDim);
                    break;
            }
            UIFactory.SetText(_result, cal.ResultText);
            UIFactory.SetColor(_result, phase == CalibrationPhase.Failed ? UIStyle.Bad : UIStyle.Text);

            _start.SetLabel(phase == CalibrationPhase.Succeeded || phase == CalibrationPhase.Failed || Rt.Store.Data.Calibration.Valid ? "Recalibrate" : "Start calibration");
            _start.Interactable = !busy;
            _cancel.Interactable = busy;
            _save.Interactable = !busy && Rt.Store.Dirty;
            _check.Interactable = !busy;
            _recenter.Interactable = !busy && Rt.Store.Data.Calibration.Valid;
            _clear.Interactable = !busy && Rt.Store.Data.Calibration.Valid;
            UIFactory.SetText(_current, "Current: " + Rt.Status.CalibrationText);
        }
    }
}
