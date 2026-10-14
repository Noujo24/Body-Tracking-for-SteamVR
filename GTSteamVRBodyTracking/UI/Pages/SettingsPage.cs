using System;
using GTSteamVRBodyTracking.Core;
using GTSteamVRBodyTracking.Status;
using UnityEngine;
using UnityEngine.UI;

namespace GTSteamVRBodyTracking.UI.Pages
{
    /// <summary>Global avatar, marker and menu preferences (saved in the tracker JSON as UI preferences).</summary>
    internal sealed class SettingsPage : MenuPage
    {
        private readonly UIToggle _avatar, _torso, _elbows, _markers, _walls, _labels, _onTop, _follow;
        private readonly Text _bodyAngle, _markerSize, _menuDistance, _menuScale;

        public override string Title => "Settings";

        public SettingsPage(MenuController menu) : base(menu, "Settings")
        {
            const float colW = 430f, h = 62f, gap = 10f;
            float y = 0f;
            UIFactory.Label(Root, "Avatar (local gorilla only, never sent to other players)", 0f, y, 880f, 36f, UIStyle.FontSmall, TextAnchor.MiddleLeft, UIStyle.TextDim);
            y += 40f;
            _avatar = UIFactory.Toggle(Root, "Avatar integration", 0f, y, colW, h, v => Change(d => d.Avatar.Enabled = v));
            _torso = UIFactory.Toggle(Root, "Drive torso", colW + 20f, y, colW, h, v => Change(d => d.Avatar.DriveBodyRotation = v));
            y += h + gap;
            _elbows = UIFactory.Toggle(Root, "Drive elbows", 0f, y, colW, h, v => Change(d => d.Avatar.DriveElbows = v));
            _bodyAngle = Stepper(colW + 20f, y, "Max torso angle", () => Step(d => d.Avatar.MaxBodyAngle = Clamp(d.Avatar.MaxBodyAngle - 5f, ConfigLimits.MinBodyAngle, ConfigLimits.MaxBodyAngle)),
                () => Step(d => d.Avatar.MaxBodyAngle = Clamp(d.Avatar.MaxBodyAngle + 5f, ConfigLimits.MinBodyAngle, ConfigLimits.MaxBodyAngle)));
            y += h + 24f;

            UIFactory.Label(Root, "Debug markers", 0f, y, 880f, 36f, UIStyle.FontSmall, TextAnchor.MiddleLeft, UIStyle.TextDim);
            y += 40f;
            _markers = UIFactory.Toggle(Root, "Show markers", 0f, y, colW, h, v => Change(d => d.Ui.ShowDebugMarkers = v));
            _walls = UIFactory.Toggle(Root, "Through walls", colW + 20f, y, colW, h, v => Change(d => d.Ui.MarkersThroughWalls = v));
            y += h + gap;
            _labels = UIFactory.Toggle(Root, "Marker labels", 0f, y, colW, h, v => Change(d => d.Ui.ShowMarkerLabels = v));
            _markerSize = Stepper(colW + 20f, y, "Marker size", () => Step(d => d.Ui.MarkerSize = Clamp(d.Ui.MarkerSize - 0.01f, ConfigLimits.MinMarkerSize, ConfigLimits.MaxMarkerSize)),
                () => Step(d => d.Ui.MarkerSize = Clamp(d.Ui.MarkerSize + 0.01f, ConfigLimits.MinMarkerSize, ConfigLimits.MaxMarkerSize)));
            y += h + 24f;

            UIFactory.Label(Root, "Menu", 0f, y, 880f, 36f, UIStyle.FontSmall, TextAnchor.MiddleLeft, UIStyle.TextDim);
            y += 40f;
            _onTop = UIFactory.Toggle(Root, "Always on top", 0f, y, colW, h, v => Change(d => d.Ui.MenuAlwaysOnTop = v));
            _follow = UIFactory.Toggle(Root, "Follows player", colW + 20f, y, colW, h, v => { Change(d => d.Ui.MenuFollowsPlayer = v); Menu.Recenter(); });
            y += h + gap;
            _menuDistance = Stepper(0f, y, "Distance", () => Step(d => d.Ui.MenuDistance = Clamp(d.Ui.MenuDistance - 0.05f, ConfigLimits.MinMenuDistance, ConfigLimits.MaxMenuDistance), true),
                () => Step(d => d.Ui.MenuDistance = Clamp(d.Ui.MenuDistance + 0.05f, ConfigLimits.MinMenuDistance, ConfigLimits.MaxMenuDistance), true));
            _menuScale = Stepper(colW + 20f, y, "Scale", () => Step(d => d.Ui.MenuScale = Clamp(d.Ui.MenuScale - 0.1f, ConfigLimits.MinMenuScale, ConfigLimits.MaxMenuScale)),
                () => Step(d => d.Ui.MenuScale = Clamp(d.Ui.MenuScale + 0.1f, ConfigLimits.MinMenuScale, ConfigLimits.MaxMenuScale)));
            y += h + 34f;

            UIFactory.Button(Root, "Reload config from disk", 0f, y, colW, h, OnReload);
            UIFactory.Button(Root, "Save configuration", colW + 20f, y, colW, h, () => Rt.SaveConfig());

            UIFactory.Button(Root, "< Back", 0f, MenuController.ContentHeight - 70f, 280f, 66f, () => Menu.Show(Menu.Dashboard));
        }

        private Text Stepper(float x, float y, string caption, Action minus, Action plus)
        {
            UIFactory.Label(Root, caption, x, y, 190f, 62f, UIStyle.FontSmall, TextAnchor.MiddleLeft, UIStyle.TextDim);
            UIFactory.Button(Root, "-", x + 190f, y, 70f, 62f, minus, UIStyle.FontTitle);
            Text value = UIFactory.Label(Root, "", x + 262f, y, 96f, 62f, UIStyle.FontNormal, TextAnchor.MiddleCenter);
            UIFactory.Button(Root, "+", x + 360f, y, 70f, 62f, plus, UIStyle.FontTitle);
            return value;
        }

        private static float Clamp(float v, float min, float max) => (float)Math.Round(MathUtil.Clamp(v, min, max), 3);

        private void Change(Action<ModConfigData> change)
        {
            change(Rt.Store.Data);
            Rt.Store.MarkDirty();
            Rt.RefreshStatusNow();
        }

        private void Step(Action<ModConfigData> change, bool replaceMenu = false)
        {
            Change(change);
            if (replaceMenu) Menu.Recenter();
            Refresh(Time.unscaledTime);
        }

        private void OnReload()
        {
            Menu.Confirm.Show("Reload the configuration from disk? Unsaved changes are discarded.", "Reload", () =>
            {
                string message = Rt.ReloadConfig();
                Menu.Notify(message, Rt.Store.LastLoadStatus == LoadStatus.Failed ? Severity.Error : Severity.Info);
            });
        }

        public override void Refresh(float now)
        {
            ModConfigData d = Rt.Store.Data;
            Sync(_avatar, d.Avatar.Enabled);
            Sync(_torso, d.Avatar.DriveBodyRotation);
            Sync(_elbows, d.Avatar.DriveElbows);
            Sync(_markers, d.Ui.ShowDebugMarkers);
            Sync(_walls, d.Ui.MarkersThroughWalls);
            Sync(_labels, d.Ui.ShowMarkerLabels);
            Sync(_onTop, d.Ui.MenuAlwaysOnTop);
            Sync(_follow, d.Ui.MenuFollowsPlayer);
            _torso.Interactable = d.Avatar.Enabled;
            _elbows.Interactable = d.Avatar.Enabled;
            UIFactory.SetText(_bodyAngle, d.Avatar.MaxBodyAngle.ToString("0") + "°");
            UIFactory.SetText(_markerSize, (d.Ui.MarkerSize * 100f).ToString("0") + " cm");
            UIFactory.SetText(_menuDistance, (d.Ui.MenuDistance * 100f).ToString("0") + " cm");
            UIFactory.SetText(_menuScale, d.Ui.MenuScale.ToString("0.0") + "x");
        }

        private static void Sync(UIToggle t, bool v)
        {
            if (t.Value != v) t.SetValue(v);
        }
    }
}
