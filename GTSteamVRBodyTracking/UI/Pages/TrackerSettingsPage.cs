using System;
using GTSteamVRBodyTracking.Core;
using GTSteamVRBodyTracking.Status;
using GTSteamVRBodyTracking.Tracking;
using UnityEngine;
using UnityEngine.UI;

namespace GTSteamVRBodyTracking.UI.Pages
{
    /// <summary>Settings for ONE tracker (selected by stable key). Every control edits only that tracker.</summary>
    internal sealed class TrackerSettingsPage : MenuPage
    {
        private static readonly float[] PositionSteps = { 0.005f, 0.01f, 0.05f };
        private static readonly float[] RotationSteps = { 1f, 5f, 15f };

        private readonly Text _name, _ids, _state, _support, _pose, _smoothingValue;
        private readonly UIButton _role, _identify, _forget, _posStep, _rotStep;
        private readonly UIToggle _enabled, _marker;
        private readonly UISlider _smoothing;
        private readonly Text[] _posValues = new Text[3];
        private readonly Text[] _rotValues = new Text[3];
        private int _posStepIndex = 1;
        private int _rotStepIndex = 1;
        private string _key;

        public override string Title => "Tracker settings";

        public TrackerSettingsPage(MenuController menu) : base(menu, "TrackerSettings")
        {
            const float W = MenuController.ContentWidth;
            _name = UIFactory.Label(Root, "", 0f, 0f, W, 42f, UIStyle.FontLarge);
            _ids = UIFactory.Label(Root, "", 0f, 42f, W, 30f, UIStyle.FontSmall, TextAnchor.MiddleLeft, UIStyle.TextDim);
            _state = UIFactory.Label(Root, "", 0f, 72f, W, 32f, UIStyle.FontNormal);
            _support = UIFactory.Label(Root, "", 0f, 104f, W, 30f, UIStyle.FontSmall, TextAnchor.MiddleLeft, UIStyle.TextDim);

            float y = 142f;
            _role = UIFactory.Button(Root, "Role", 0f, y, 330f, 62f, () => { TrackerRuntime t = Selected(); if (t != null) Menu.RolePicker.Show(t); });
            _enabled = UIFactory.Toggle(Root, "Enabled", 340f, y, 250f, 62f, on => Edit(t => t.Settings.Enabled = on, on ? "Tracker enabled." : "Tracker disabled: no marker, not used for the avatar."));
            _marker = UIFactory.Toggle(Root, "Debug marker", 600f, y, 280f, 62f, on => Edit(t => t.Settings.ShowMarker = on, on ? "Marker shown." : "Marker hidden (tracking continues)."));
            y += 72f;
            _identify = UIFactory.Button(Root, "Identify / test", 0f, y, 330f, 62f, () => { TrackerRuntime t = Selected(); if (t != null) Rt.Identify(t); });
            UIFactory.Button(Root, "Reset offsets", 340f, y, 250f, 62f, () => Edit(t => t.Settings.ResetOffsets(), "Offsets reset to zero."));
            UIFactory.Button(Root, "Reset settings", 600f, y, 280f, 62f, () => Edit(t => t.Settings.ResetSettings(), "Tracker settings reset (role and calibration kept)."));

            y += 82f;
            UIFactory.Label(Root, "Position offset (cm)", 0f, y, 300f, 40f, UIStyle.FontNormal, TextAnchor.MiddleLeft, UIStyle.TextDim);
            _posStep = UIFactory.Button(Root, "", 300f, y, 130f, 40f, () => { _posStepIndex = (_posStepIndex + 1) % PositionSteps.Length; }, UIStyle.FontSmall);
            UIFactory.Label(Root, "Rotation offset (deg)", 450f, y, 300f, 40f, UIStyle.FontNormal, TextAnchor.MiddleLeft, UIStyle.TextDim);
            _rotStep = UIFactory.Button(Root, "", 750f, y, 130f, 40f, () => { _rotStepIndex = (_rotStepIndex + 1) % RotationSteps.Length; }, UIStyle.FontSmall);
            y += 50f;
            string[] axes = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
            {
                int axis = i;
                float ry = y + i * 72f;
                _posValues[i] = Stepper(0f, ry, axes[i], () => NudgePosition(axis, -1f), () => NudgePosition(axis, 1f));
                _rotValues[i] = Stepper(450f, ry, axes[i], () => NudgeRotation(axis, -1f), () => NudgeRotation(axis, 1f));
            }
            y += 3 * 72f + 6f;
            UIFactory.Label(Root, "Offsets are in the tracker's own axes (red X, green Y, blue Z on its marker); limits ±50 cm / ±180°.", 0f, y, W, 30f, UIStyle.FontSmall, TextAnchor.MiddleLeft, UIStyle.TextDim);

            y += 40f;
            UIFactory.Label(Root, "Smoothing", 0f, y, 200f, 44f, UIStyle.FontNormal, TextAnchor.MiddleLeft, UIStyle.TextDim);
            _smoothingValue = UIFactory.Label(Root, "", 680f, y, 200f, 44f, UIStyle.FontNormal, TextAnchor.MiddleRight);
            y += 46f;
            _smoothing = UIFactory.Slider(Root, 0f, y, W, 60f, ConfigLimits.MinSmoothing, ConfigLimits.MaxSmoothing,
                v => Edit(t => t.Settings.Smoothing = v, null));

            y += 72f;
            _pose = UIFactory.Label(Root, "", 0f, y, W, 60f, UIStyle.FontSmall, TextAnchor.UpperLeft, UIStyle.TextDim);

            float by = MenuController.ContentHeight - 72f;
            UIFactory.Button(Root, "< Back", 0f, by, 280f, 66f, () => Menu.Show(Menu.TrackerList));
            _forget = UIFactory.Button(Root, "Forget tracker", 300f, by, 280f, 66f, OnForget);
            UIFactory.Button(Root, "Save configuration", 600f, by, 280f, 66f, () => Rt.SaveConfig());
        }

        private Text Stepper(float x, float y, string axis, Action minus, Action plus)
        {
            UIFactory.Label(Root, axis, x, y, 50f, 62f, UIStyle.FontLarge, TextAnchor.MiddleCenter, UIStyle.TextDim);
            UIFactory.Button(Root, "-", x + 54f, y, 100f, 62f, minus, UIStyle.FontTitle);
            Text value = UIFactory.Label(Root, "", x + 158f, y, 120f, 62f, UIStyle.FontLarge, TextAnchor.MiddleCenter);
            UIFactory.Button(Root, "+", x + 282f, y, 100f, 62f, plus, UIStyle.FontTitle);
            return value;
        }

        public void Select(string key) => _key = key;

        private TrackerRuntime Selected() => Rt.Tracker.Find(_key);

        private void Edit(Action<TrackerRuntime> change, string message)
        {
            TrackerRuntime t = Selected();
            if (t == null) return;
            change(t);
            ConfigValidator.ValidateTracker(t.Settings, null);
            Rt.OnTrackerSettingsChanged(t);
            if (message != null) Menu.Notify(message);
            Refresh(Time.unscaledTime);
        }

        private void NudgePosition(int axis, float sign)
        {
            float step = PositionSteps[_posStepIndex] * sign;
            Edit(t =>
            {
                Vec3 v = t.Settings.PositionOffset;
                float limit = ConfigLimits.MaxPositionOffset;
                float before = Get(v, axis);
                float after = MathUtil.Clamp((float)Math.Round((before + step) * 1000f) / 1000f, -limit, limit);
                t.Settings.PositionOffset = Set(v, axis, after);
                if (Mathf.Approximately(before, after)) Menu.Notify($"Position offset limit is ±{limit * 100f:F0} cm.", Severity.Warning);
            }, null);
        }

        private void NudgeRotation(int axis, float sign)
        {
            float step = RotationSteps[_rotStepIndex] * sign;
            Edit(t =>
            {
                Vec3 v = t.Settings.RotationOffset;
                float limit = ConfigLimits.MaxRotationOffset;
                float before = Get(v, axis);
                float after = MathUtil.Clamp(before + step, -limit, limit);
                t.Settings.RotationOffset = Set(v, axis, after);
                if (Mathf.Approximately(before, after)) Menu.Notify($"Rotation offset limit is ±{limit:F0}°.", Severity.Warning);
            }, null);
        }

        private static float Get(Vec3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

        private static Vec3 Set(Vec3 v, int axis, float value)
        {
            if (axis == 0) v.X = value;
            else if (axis == 1) v.Y = value;
            else v.Z = value;
            return v;
        }

        private void OnForget()
        {
            TrackerRuntime t = Selected();
            if (t == null || t.Connected) return;
            Menu.Confirm.Show($"Forget tracker {t.Record.DisplayId}? Its saved role, offsets and calibration are removed (after Save).", "Forget", () =>
            {
                if (Rt.Tracker.Forget(t.Key))
                {
                    Rt.RefreshStatusNow();
                    Menu.Notify("Tracker forgotten. Press Save configuration to write the change.", Severity.Warning);
                    Menu.Show(Menu.TrackerList);
                }
            });
        }

        public override void Refresh(float now)
        {
            TrackerRuntime t = Selected();
            if (t == null)
            {
                UIFactory.SetText(_name, "Tracker no longer available");
                UIFactory.SetText(_ids, "It was removed (session-only device disconnected, or forgotten).");
                UIFactory.SetText(_state, "");
                UIFactory.SetText(_support, "");
                UIFactory.SetText(_pose, "");
                return;
            }

            TrackerRecord rec = t.Record;
            TrackerSettings s = t.Settings;
            UIFactory.SetText(_name, rec.DisplayName + "  " + BodyRoles.DisplayName(s.Role));
            UIFactory.SetText(_ids, $"Serial: {rec.DisplayId}   Maker: {rec.Manufacturer}   SteamVR type: {rec.ControllerType}   Tracking: {rec.TrackingSystem}");
            UIFactory.SetText(_state, $"Connection: {(rec.Connected ? "connected (index " + rec.DeviceIndex + ")" : "disconnected")}    Pose: {PoseValidity.Describe(t.State)}");
            UIFactory.SetColor(_state, t.State == TrackingState.Valid ? UIStyle.Good : UIStyle.Warn);
            string support = BodyRoles.SupportDescription(s.Role);
            if (Rt.Status.IsConflicted(t)) support = "ROLE CONFLICT: not applied until resolved";
            if (!t.SettingsPersistent) support += "   |   No stable serial: settings are session-only";
            if (s.Calibration != null) support += s.Calibration.RoleAtCalibration == s.Role ? "   |   Calibrated" : "   |   Calibrated for another role";
            UIFactory.SetText(_support, support);

            _role.SetLabel("Role: " + BodyRoles.DisplayName(s.Role) + "  v");
            if (_enabled.Value != s.Enabled) _enabled.SetValue(s.Enabled);
            if (_marker.Value != s.ShowMarker) _marker.SetValue(s.ShowMarker);
            _identify.Interactable = true;
            _forget.Interactable = !rec.Connected;

            _posStep.SetLabel($"step {PositionSteps[_posStepIndex] * 100f:0.#}");
            _rotStep.SetLabel($"step {RotationSteps[_rotStepIndex]:0}");
            UIFactory.SetText(_posValues[0], (s.PositionOffset.X * 100f).ToString("0.0"));
            UIFactory.SetText(_posValues[1], (s.PositionOffset.Y * 100f).ToString("0.0"));
            UIFactory.SetText(_posValues[2], (s.PositionOffset.Z * 100f).ToString("0.0"));
            UIFactory.SetText(_rotValues[0], s.RotationOffset.X.ToString("0"));
            UIFactory.SetText(_rotValues[1], s.RotationOffset.Y.ToString("0"));
            UIFactory.SetText(_rotValues[2], s.RotationOffset.Z.ToString("0"));

            if (Mathf.Abs(_smoothing.Value - s.Smoothing) > 1e-4f) _smoothing.SetValue(s.Smoothing);
            UIFactory.SetText(_smoothingValue, s.Smoothing <= 0f ? "off" : s.Smoothing.ToString("0.00"));

            UIFactory.SetText(_pose, t.PoseValid
                ? $"Tracking (Unity axes): {t.RawTracking.Position}   World: {(t.WorldValid ? t.World.Position.ToString() : "unmapped")}"
                : "No current pose.");
        }
    }
}
