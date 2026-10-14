using BepInEx.Configuration;

namespace GTSteamVRBodyTracking.Config
{
    public enum VRMenuGesture
    {
        BothSecondaryButtons,
        LeftSecondaryButton,
        RightSecondaryButton,
        LeftPrimaryAndSecondary,
        Disabled
    }

    public enum CoordinateModeSetting
    {
        Auto,
        HmdAnchor,
        TrackingOrigin
    }

    /// <summary>
    /// Startup options in the BepInEx convention (BepInEx/config/&lt;GUID&gt;.cfg).
    /// Everything edited from the in-game UI lives in the separate JSON file handled by ConfigStore.
    /// </summary>
    internal sealed class PluginSettings
    {
        public ConfigEntry<string> MenuToggleKey { get; }
        public ConfigEntry<VRMenuGesture> MenuGesture { get; }
        public ConfigEntry<float> MenuGestureHoldSeconds { get; }
        public ConfigEntry<float> DiscoveryIntervalSeconds { get; }
        public ConfigEntry<bool> AllowStandaloneOpenVR { get; }
        public ConfigEntry<CoordinateModeSetting> CoordinateMode { get; }
        public ConfigEntry<bool> AvatarPatchEnabled { get; }
        public ConfigEntry<bool> VerboseLogging { get; }

        public PluginSettings(ConfigFile config)
        {
            MenuToggleKey = config.Bind("Menu", "Toggle Key", "F8",
                "Keyboard key (Unity Input System Key name, e.g. F8, F9, Backquote) that opens/closes the tracker menu.");
            MenuGesture = config.Bind("Menu", "VR Gesture", VRMenuGesture.BothSecondaryButtons,
                "Controller gesture (held) that opens/closes the tracker menu in VR. Secondary = B/Y on Quest/Index, menu button region on Vive wands.");
            MenuGestureHoldSeconds = config.Bind("Menu", "VR Gesture Hold Seconds", 0.6f,
                new ConfigDescription("How long the VR gesture must be held.", new AcceptableValueRange<float>(0.1f, 3f)));

            DiscoveryIntervalSeconds = config.Bind("Tracking", "Discovery Interval Seconds", 1.0f,
                new ConfigDescription("How often SteamVR devices are re-scanned for connects/disconnects. Pose sampling is every frame regardless.", new AcceptableValueRange<float>(0.25f, 5f)));
            AllowStandaloneOpenVR = config.Bind("Tracking", "Allow Standalone OpenVR Session", true,
                "If the game is NOT running through its OpenVR XR loader (e.g. OpenXR), open a background OpenVR session to read trackers. Never used when the game's own OpenVR session exists.");
            CoordinateMode = config.Bind("Tracking", "Coordinate Mode", CoordinateModeSetting.Auto,
                "Auto: verify at runtime whether the camera parent is the tracking origin; otherwise anchor on the headset. Force one mode only for troubleshooting.");

            AvatarPatchEnabled = config.Bind("Avatar", "Enable Avatar Integration Patch", true,
                "Installs the Harmony patch on GorillaIKMgr.CopyInput that lets trackers pose the local gorilla visually. Requires restart. Disable to run tracker-only.");

            VerboseLogging = config.Bind("Debug", "Verbose Logging", false, "Extra diagnostic log lines.");
        }
    }
}
