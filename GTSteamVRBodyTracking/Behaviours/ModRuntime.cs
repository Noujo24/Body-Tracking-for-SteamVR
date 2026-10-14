using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using GTSteamVRBodyTracking.Avatar;
using GTSteamVRBodyTracking.Calibration;
using GTSteamVRBodyTracking.Config;
using GTSteamVRBodyTracking.Core;
using GTSteamVRBodyTracking.Debugging;
using GTSteamVRBodyTracking.Tools;
using GTSteamVRBodyTracking.Status;
using GTSteamVRBodyTracking.Tracking;
using GTSteamVRBodyTracking.UI;
using UnityEngine;

namespace GTSteamVRBodyTracking.Behaviours
{
    /// <summary>
    /// Owns every service of the mod. Lives on a single DontDestroyOnLoad root object; destroying that
    /// object releases the Harmony patch, OpenVR resources, markers and UI.
    /// Frame order: Update → discovery; LateUpdate → pose sampling → calibration → markers → status → menu.
    /// (The avatar patch samples on demand, so its order relative to GorillaIKMgr does not matter.)
    /// </summary>
    internal sealed class ModRuntime : MonoBehaviour
    {
        internal static ModRuntime Instance { get; private set; }

        private const float StatusInterval = 0.25f;

        public PluginSettings Settings { get; private set; }
        public ConfigStore Store { get; private set; }
        public TrackerService Tracker { get; private set; }
        public ModStatus Status { get; } = new ModStatus();
        public CalibrationService Calibration { get; private set; }
        public MarkerManager Markers { get; private set; }
        public AvatarIntegration Avatar { get; private set; }
        public MenuController Menu { get; private set; }

        private float _nextStatus;
        private float _nextDiagnostics;
        private bool _initialized;

        public void Initialize(ConfigFile config)
        {
            if (Instance != null && Instance != this)
            {
                ModLog.Warn("Duplicate ModRuntime detected; destroying the new one.");
                Destroy(this);
                return;
            }
            Instance = this;

            Settings = new PluginSettings(config);

            Store = new ConfigStore(Path.Combine(Paths.ConfigPath, Constants.TrackerConfigFileName));
            LoadStatus status = Store.Load();
            ModLog.Info($"Tracker config: {status} ({Store.FilePath}).");
            foreach (string m in Store.LastLoadMessages) ModLog.Warn("Config: " + m);

            Tracker = new TrackerService(Store, Settings.AllowStandaloneOpenVR.Value, Time.realtimeSinceStartup)
            {
                DiscoveryInterval = Settings.DiscoveryIntervalSeconds.Value
            };
            Tracker.Mapper.Setting = Settings.CoordinateMode.Value;

            Calibration = new CalibrationService(Tracker, Store);
            Markers = new MarkerManager(transform);
            Avatar = new AvatarIntegration(this);
            if (Settings.AvatarPatchEnabled.Value) Avatar.InstallPatch();
            else ModLog.Info("Avatar integration patch disabled in config; running tracker-only.");
            Menu = new MenuController(this, transform);

            _initialized = true;
            RefreshStatusNow();
            ModLog.Info($"Ready. Open the menu with {Settings.MenuToggleKey.Value} or the VR gesture ({Settings.MenuGesture.Value}).");
        }

        private void Update()
        {
            if (!_initialized) return;
            try
            {
                Tracker.Tick(Time.realtimeSinceStartup);
                Avatar.Tick(Time.unscaledTime);
            }
            catch (Exception e)
            {
                ModLog.Exception("runtime-update", e, "Tracker update failed");
            }
        }

        private void LateUpdate()
        {
            if (!_initialized) return;
            float now = Time.unscaledTime;

            // Explicit try/catch per step (no lambdas → no per-frame closure allocations).
            try { Tracker.SampleIfNeeded(); }
            catch (Exception e) { ModLog.Exception("late-sample", e, "Tracker sampling failed"); }

            try
            {
                bool rig = Avatar.TryGetRigYawOffset(out float yaw);
                Calibration.Update(now, yaw, rig);
            }
            catch (Exception e) { ModLog.Exception("late-calibration", e, "Calibration update failed"); }

            try { Markers.Update(this, now); }
            catch (Exception e) { ModLog.Exception("late-markers", e, "Marker update failed"); }

            if (now >= _nextStatus) RefreshStatusNow();

            try { Menu.Update(now); }
            catch (Exception e) { ModLog.Exception("late-menu", e, "Menu update failed"); }

            LogDiagnostics();
        }

        private static void Step(string key, Action action)
        {
            try { action(); }
            catch (Exception e) { ModLog.Exception("late-" + key, e, $"Per-frame step '{key}' failed"); }
        }

        // ------------------------------------------------------------------ actions used by the UI

        public void RefreshStatusNow()
        {
            if (!_initialized) return;
            _nextStatus = Time.unscaledTime + StatusInterval;
            try { Status.Evaluate(this); }
            catch (Exception e) { ModLog.Exception("status", e, "Status evaluation failed"); }
        }

        /// <summary>Called after any per-tracker edit (role, enable, offsets, smoothing, marker).</summary>
        public void OnTrackerSettingsChanged(TrackerRuntime t)
        {
            Store.MarkDirty();
            RefreshStatusNow();
        }

        public void Identify(TrackerRuntime t)
        {
            Markers.Identify(t.Key, Time.unscaledTime);
            string id = TrackerIdentity.ShortId(t.Record.Serial);
            if (!Store.Data.ModEnabled)
                Menu.Notify("The mod is disabled; enable it on the dashboard to see markers.", Severity.Warning);
            else if (!t.Connected)
                Menu.Notify($"Tracker {id} is disconnected; showing its last known position if it had one.", Severity.Warning);
            else if (!t.PoseValid)
                Menu.Notify($"Tracker {id} has no valid pose ({PoseValidity.Describe(t.State)}). Move it into view of the base stations.", Severity.Warning);
            else
                Menu.Notify($"Tracker {id}: its marker is flashing yellow for {MarkerManager.IdentifySeconds:0} s. Move the physical tracker to confirm.");
        }

        public void SaveConfig()
        {
            SaveResult result = Store.Save();
            RefreshStatusNow();
            if (result.Success) ModLog.Info(result.Message);
            else ModLog.Error(result.Message);
            Menu.Notify(result.Message, result.Success ? Severity.Info : Severity.Error);
        }

        public void RestoreDefaults()
        {
            Store.RestoreDefaults();
            Tracker.RebindSettings();
            RefreshStatusNow();
        }

        public string ReloadConfig()
        {
            LoadStatus status = Store.Load();
            Tracker.RebindSettings();
            RefreshStatusNow();
            string detail = Store.LastLoadMessages.Count > 0 ? " " + string.Join(" ", Store.LastLoadMessages) : "";
            foreach (string m in Store.LastLoadMessages) ModLog.Warn("Config: " + m);
            return $"Config reload: {status}.{detail}";
        }

        // ------------------------------------------------------------------ diagnostics / shutdown

        private void LogDiagnostics()
        {
            if (!Settings.VerboseLogging.Value || Time.realtimeSinceStartup < _nextDiagnostics) return;
            _nextDiagnostics = Time.realtimeSinceStartup + 5f;
            ModLog.Info($"[diag] session={Tracker.Session.State} '{Tracker.Session.StatusText}' trackers={Tracker.Registry.ConnectedTrackerCount}/{Tracker.Trackers.Count} valid={Tracker.ValidCount} " +
                        $"hmd={(Tracker.HmdValid ? "ok" : "invalid")} hmdSystem='{Tracker.Registry.HmdTrackingSystem}' source={Tracker.PoseSource} mapping={Tracker.Mapper.Mode} " +
                        $"err={Tracker.Mapper.PositionError * 100f:F1}cm/{Tracker.Mapper.AngleError:F1}deg avatar='{Avatar.StatusText}' appliedFrames={Avatar.AppliedFrames}");
            foreach (TrackerRuntime t in Tracker.Trackers)
                ModLog.Info($"[diag]   {t.Record.DisplayId} '{t.Record.Model}' sys='{t.Record.TrackingSystem}' idx={t.Record.DeviceIndex} role={t.Role} enabled={t.Enabled} state={t.State} " +
                            $"raw={(t.PoseValid ? t.RawTracking.Position.ToString() : "-")} world={(t.WorldValid ? t.World.Position.ToString() : "-")}");
        }

        private void OnDestroy()
        {
            Step("avatar-uninstall", () => Avatar?.Uninstall());
            Step("menu-dispose", () => Menu?.Dispose());
            Step("markers-dispose", () => Markers?.Dispose());
            Step("tracker-shutdown", () => Tracker?.Shutdown());
            _initialized = false;
            if (Instance == this) Instance = null;
            ModLog.Info("ModRuntime destroyed; patch removed, tracking resources and objects released.");
        }
    }
}
