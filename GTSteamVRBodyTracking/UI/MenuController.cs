using GTSteamVRBodyTracking.Behaviours;
using System;
using GTSteamVRBodyTracking.Config;
using GTSteamVRBodyTracking.Core;
using GTSteamVRBodyTracking.Tools;
using GTSteamVRBodyTracking.Status;
using GTSteamVRBodyTracking.UI.Pages;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GTSteamVRBodyTracking.UI
{
    /// <summary>
    /// World-space VR menu. Built lazily on first open (so the game camera/layers are known), positioned in
    /// front of the headset, optionally following the player, refreshed at 10 Hz (not rebuilt per frame).
    /// Tracking keeps running whether the menu is open or closed.
    /// </summary>
    internal sealed class MenuController
    {
        public const float Width = 900f;
        public const float Height = 1120f;
        public const float ContentX = 10f;
        public const float ContentY = 92f;
        public const float ContentWidth = 880f;
        public const float ContentHeight = 930f;
        private const float MetresPerUnit = 0.00045f;
        private const float RefreshInterval = 0.1f;

        private readonly ModRuntime _rt;
        private readonly Transform _modRoot;

        private GameObject _root;
        private RectTransform _canvasRect;
        private PokeInteractor _poke;
        private Text _title;
        private Text _headerStatus;
        private Text _footer;
        private Image _footerBg;
        private UIButton _back;
        private float _footerUntil;
        private float _nextRefresh;
        private bool _appliedOnTop;
        private float _appliedScale = -1f;

        private Transform _anchor;
        private Vector3 _anchorLocalPos;
        private Quaternion _anchorLocalRot;

        private Key _key = Key.F8;
        private KeyCode _legacyKey = KeyCode.F8;
        private bool _inputSystemFailed;
        private float _gestureHeldSince = -1f;
        private bool _gestureLatched;

        private MenuPage _current;

        public RectTransform Content { get; private set; }
        public bool IsOpen { get; private set; }
        public bool IsBuilt => _root != null;
        public ModRuntime Runtime => _rt;

        public DashboardPage Dashboard { get; private set; }
        public TrackerListPage TrackerList { get; private set; }
        public TrackerSettingsPage TrackerSettings { get; private set; }
        public CalibrationPage Calibration { get; private set; }
        public ConflictsPage Conflicts { get; private set; }
        public SettingsPage Settings { get; private set; }
        public RolePickerPopup RolePicker { get; private set; }
        public ConfirmPopup Confirm { get; private set; }

        public MenuController(ModRuntime rt, Transform modRoot)
        {
            _rt = rt;
            _modRoot = modRoot;
            string keyName = rt.Settings.MenuToggleKey.Value;
            if (!Enum.TryParse(keyName, true, out _key) || _key == Key.None)
            {
                ModLog.Warn($"Unknown menu key '{keyName}'; using F8.");
                _key = Key.F8;
            }
            if (!Enum.TryParse(keyName, true, out _legacyKey)) _legacyKey = KeyCode.F8;
        }

        // ---------------------------------------------------------------- lifecycle

        private void Build()
        {
            Camera cam = _rt.Tracker.Mapper.Camera;
            UIStyle.Init();
            UIStyle.Layer = UIStyle.PickVisibleLayer(cam);
            _appliedOnTop = _rt.Store.Data.Ui.MenuAlwaysOnTop;
            UIFactory.CurrentMaterial = UIStyle.MaterialFor(_appliedOnTop);

            _root = new GameObject("GTSVRBT_Menu", typeof(RectTransform));
            _root.layer = UIStyle.Layer;
            _root.transform.SetParent(_modRoot, false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 30000;
            canvas.worldCamera = cam;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 2f;

            _canvasRect = (RectTransform)_root.transform;
            _canvasRect.sizeDelta = new Vector2(Width, Height);
            _canvasRect.pivot = new Vector2(0.5f, 0.5f);

            UIFactory.AddImage(_canvasRect, UIStyle.Background);
            UIFactory.Panel(_canvasRect, "Header", 0f, 0f, Width, 84f, UIStyle.Header);
            _back = UIFactory.Button(_canvasRect, "< Back", 12f, 12f, 150f, 60f, GoBack);
            _title = UIFactory.Label(_canvasRect, "", 175f, 6f, 470f, 44f, UIStyle.FontTitle, TextAnchor.MiddleLeft);
            _headerStatus = UIFactory.Label(_canvasRect, "", 175f, 48f, 520f, 32f, UIStyle.FontSmall, TextAnchor.MiddleLeft, UIStyle.TextDim);
            UIFactory.Button(_canvasRect, "Close X", Width - 172f, 12f, 160f, 60f, Close);

            Content = UIFactory.Container(_canvasRect, "Content", ContentX, ContentY, ContentWidth, ContentHeight);

            _footerBg = UIFactory.Panel(_canvasRect, "Footer", 0f, Height - 92f, Width, 92f, UIStyle.Header);
            _footer = UIFactory.Label(_footerBg.transform, "", 16f, 6f, Width - 32f, 80f, UIStyle.FontSmall, TextAnchor.MiddleLeft, UIStyle.TextDim);

            Dashboard = new DashboardPage(this);
            TrackerList = new TrackerListPage(this);
            TrackerSettings = new TrackerSettingsPage(this);
            Calibration = new CalibrationPage(this);
            Conflicts = new ConflictsPage(this);
            Settings = new SettingsPage(this);
            // Popups after pages so they are drawn and hit-tested on top.
            RolePicker = new RolePickerPopup(this, _canvasRect);
            Confirm = new ConfirmPopup(this, _canvasRect);
            _poke = new PokeInteractor(_canvasRect);

            Show(Dashboard);
            ShowFooterHint();
            ModLog.Info($"Menu built on layer {UIStyle.Layer} (camera: {(cam != null ? cam.name : "none")}).");
        }

        public void Dispose()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            UIControlRegistry.Clear();
            UIStyle.Dispose();
            IsOpen = false;
        }

        // ---------------------------------------------------------------- per frame

        public void Update(float now)
        {
            HandleToggleInput(now);
            if (!IsOpen || _root == null) return;

            ApplyPreferences();
            FollowAnchor();

            try { _poke.Update(_rt.Tracker.Mapper.Camera, now); }
            catch (Exception e) { ModLog.Exception("poke", e, "Menu interaction failed"); }

            if (now >= _nextRefresh)
            {
                _nextRefresh = now + RefreshInterval;
                try
                {
                    _title.text = _current?.Title ?? "";
                    UIFactory.SetText(_headerStatus, HeaderStatus());
                    UIFactory.SetActive(_back.Rect, _current != Dashboard);
                    _current?.Refresh(now);
                    RolePicker.Refresh(now);
                }
                catch (Exception e) { ModLog.Exception("menu-refresh", e, "Menu refresh failed"); }

                if (_footerUntil > 0f && now > _footerUntil)
                {
                    _footerUntil = 0f;
                    ShowFooterHint();
                }
            }
        }

        private string HeaderStatus()
        {
            ModStatus s = _rt.Status;
            return $"SteamVR: {(_rt.Tracker.Session.IsConnected ? "OK" : "offline")}   Trackers: {s.ConnectedCount}   Valid: {s.ValidCount}   {(s.Conflicts.ConflictedRoles.Count > 0 ? "CONFLICT   " : "")}{(_rt.Store.Dirty ? "unsaved" : "saved")}";
        }

        private void HandleToggleInput(float now)
        {
            bool keyPressed = false;
            if (!_inputSystemFailed)
            {
                try
                {
                    Keyboard kb = Keyboard.current;
                    keyPressed = kb != null && kb[_key].wasPressedThisFrame;
                }
                catch (Exception e)
                {
                    _inputSystemFailed = true;
                    ModLog.Warn("Input System keyboard unavailable, using legacy Input: " + e.Message);
                }
            }
            else
            {
                try { keyPressed = Input.GetKeyDown(_legacyKey); }
                catch (Exception) { /* neither input path available */ }
            }

            if (keyPressed) Toggle();

            if (GestureHeld())
            {
                if (_gestureHeldSince < 0f) _gestureHeldSince = now;
                if (!_gestureLatched && now - _gestureHeldSince >= _rt.Settings.MenuGestureHoldSeconds.Value)
                {
                    _gestureLatched = true;
                    Toggle();
                }
            }
            else
            {
                _gestureHeldSince = -1f;
                _gestureLatched = false;
            }
        }

        private bool GestureHeld()
        {
            VRMenuGesture g = _rt.Settings.MenuGesture.Value;
            if (g == VRMenuGesture.Disabled) return false;
            try
            {
                ControllerInputPoller p = ControllerInputPoller.instance;
                if (p == null) return false;
                switch (g)
                {
                    case VRMenuGesture.BothSecondaryButtons: return p.leftControllerSecondaryButton && p.rightControllerSecondaryButton;
                    case VRMenuGesture.LeftSecondaryButton: return p.leftControllerSecondaryButton;
                    case VRMenuGesture.RightSecondaryButton: return p.rightControllerSecondaryButton;
                    case VRMenuGesture.LeftPrimaryAndSecondary: return p.leftControllerPrimaryButton && p.leftControllerSecondaryButton;
                    default: return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---------------------------------------------------------------- open / close / placement

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            try
            {
                if (_root == null) Build();
                _root.SetActive(true);
                IsOpen = true;
                _poke.ResetPointers();
                PlaceInFrontOfHead();
                _nextRefresh = 0f;
                _rt.RefreshStatusNow();
            }
            catch (Exception e)
            {
                ModLog.Exception("menu-open", e, "Opening the menu failed");
                IsOpen = false;
            }
        }

        public void Close()
        {
            if (_root != null) _root.SetActive(false);
            IsOpen = false;
            RolePicker?.Hide();
            Confirm?.Hide();
        }

        private void PlaceInFrontOfHead()
        {
            Camera cam = _rt.Tracker.Mapper.Camera;
            if (cam == null) cam = Camera.main;
            if (cam == null)
            {
                _root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                _anchor = null;
                return;
            }

            Transform head = cam.transform;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
            forward.Normalize();
            float scale = head.parent != null ? head.parent.lossyScale.x : 1f;
            UiPreferences ui = _rt.Store.Data.Ui;
            Vector3 pos = head.position + forward * (ui.MenuDistance * scale) + Vector3.down * (0.12f * scale);
            // Canvas forward must point AWAY from the viewer for the front face to be visible.
            Quaternion rot = Quaternion.LookRotation(pos - head.position, Vector3.up);
            _root.transform.SetPositionAndRotation(pos, rot);

            _anchor = ui.MenuFollowsPlayer ? head.parent : null;
            if (_anchor != null)
            {
                _anchorLocalPos = _anchor.InverseTransformPoint(pos);
                _anchorLocalRot = Quaternion.Inverse(_anchor.rotation) * rot;
            }
        }

        private void FollowAnchor()
        {
            if (_anchor == null) return;
            _root.transform.SetPositionAndRotation(_anchor.TransformPoint(_anchorLocalPos), _anchor.rotation * _anchorLocalRot);
        }

        public void Recenter() => PlaceInFrontOfHead();

        private void ApplyPreferences()
        {
            UiPreferences ui = _rt.Store.Data.Ui;
            float scale = MetresPerUnit * ui.MenuScale;
            if (Math.Abs(scale - _appliedScale) > 1e-7f)
            {
                _canvasRect.localScale = Vector3.one * scale;
                _appliedScale = scale;
            }
            if (ui.MenuAlwaysOnTop != _appliedOnTop)
            {
                _appliedOnTop = ui.MenuAlwaysOnTop;
                UIFactory.CurrentMaterial = UIStyle.MaterialFor(_appliedOnTop);
                foreach (Graphic g in _root.GetComponentsInChildren<Graphic>(true)) g.material = UIFactory.CurrentMaterial;
            }
        }

        // ---------------------------------------------------------------- navigation & feedback

        public void Show(MenuPage page)
        {
            if (_current != null) _current.Root.gameObject.SetActive(false);
            _current = page;
            _current.Root.gameObject.SetActive(true);
            _current.OnShow();
            _nextRefresh = 0f;
        }

        public void GoBack()
        {
            if (_current == TrackerSettings) Show(TrackerList);
            else Show(Dashboard);
        }

        public void Notify(string message, Severity severity = Severity.Info)
        {
            if (_footer == null) return;
            _footer.text = message;
            _footer.color = severity == Severity.Error ? UIStyle.Bad : severity == Severity.Warning ? UIStyle.Warn : UIStyle.Good;
            _footerUntil = Time.unscaledTime + 7f;
            if (severity == Severity.Error) ModLog.Warn("UI: " + message);
        }

        private void ShowFooterHint()
        {
            _footer.text = $"Touch buttons with a fingertip. Toggle: {_key} key or {Describe(_rt.Settings.MenuGesture.Value)}.";
            _footer.color = UIStyle.TextDim;
        }

        private static string Describe(VRMenuGesture g)
        {
            switch (g)
            {
                case VRMenuGesture.BothSecondaryButtons: return "hold both B/Y buttons";
                case VRMenuGesture.LeftSecondaryButton: return "hold left Y";
                case VRMenuGesture.RightSecondaryButton: return "hold right B";
                case VRMenuGesture.LeftPrimaryAndSecondary: return "hold left X+Y";
                default: return "no VR gesture";
            }
        }
    }

    internal abstract class MenuPage
    {
        protected readonly MenuController Menu;
        public readonly RectTransform Root;

        protected ModRuntime Rt => Menu.Runtime;

        protected MenuPage(MenuController menu, string name)
        {
            Menu = menu;
            Root = UIFactory.Container(menu.Content, name, 0f, 0f, MenuController.ContentWidth, MenuController.ContentHeight);
            Root.gameObject.SetActive(false);
        }

        public abstract string Title { get; }
        public virtual void OnShow() { }
        public abstract void Refresh(float now);

        protected static Color SeverityColor(Severity s) =>
            s == Severity.Error ? UIStyle.Bad : s == Severity.Warning ? UIStyle.Warn : UIStyle.TextDim;
    }
}
