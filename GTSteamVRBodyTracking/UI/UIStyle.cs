using System;
using GTSteamVRBodyTracking.Tools;
using UnityEngine;
using UnityEngine.Rendering;

namespace GTSteamVRBodyTracking.UI
{
    /// <summary>Shared look of the menu. Font and shaders verified present in this build (unity default resources / unity_builtin_extra).</summary>
    internal static class UIStyle
    {
        public static readonly Color Background = new Color(0.07f, 0.08f, 0.10f, 0.97f);
        public static readonly Color Header = new Color(0.11f, 0.13f, 0.17f, 1f);
        public static readonly Color Card = new Color(0.13f, 0.14f, 0.18f, 1f);
        public static readonly Color CardHighlight = new Color(0.20f, 0.26f, 0.38f, 1f);
        public static readonly Color Button = new Color(0.22f, 0.25f, 0.32f, 1f);
        public static readonly Color ButtonHover = new Color(0.30f, 0.36f, 0.48f, 1f);
        public static readonly Color ButtonPressed = new Color(0.35f, 0.55f, 0.95f, 1f);
        public static readonly Color ButtonDisabled = new Color(0.15f, 0.16f, 0.19f, 1f);
        public static readonly Color Accent = new Color(0.26f, 0.52f, 0.96f, 1f);
        public static readonly Color Good = new Color(0.30f, 0.82f, 0.45f, 1f);
        public static readonly Color Warn = new Color(1.00f, 0.76f, 0.22f, 1f);
        public static readonly Color Bad = new Color(0.97f, 0.36f, 0.32f, 1f);
        public static readonly Color Text = new Color(0.94f, 0.95f, 0.97f, 1f);
        public static readonly Color TextDim = new Color(0.62f, 0.65f, 0.72f, 1f);
        public static readonly Color TextDisabled = new Color(0.40f, 0.42f, 0.47f, 1f);
        public static readonly Color ToggleOn = new Color(0.20f, 0.55f, 0.32f, 1f);
        public static readonly Color ToggleOff = new Color(0.36f, 0.22f, 0.22f, 1f);

        public const int FontSmall = 21;
        public const int FontNormal = 25;
        public const int FontLarge = 31;
        public const int FontTitle = 36;

        public static Font Font { get; private set; }
        public static Material OverlayMaterial { get; private set; }
        public static Material DepthMaterial { get; private set; }
        public static int Layer { get; set; }

        private static bool _ownsFont;

        public static void Init()
        {
            if (Font == null)
            {
                try { Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
                catch (Exception e) { ModLog.Warn("Built-in LegacyRuntime.ttf unavailable: " + e.Message); }
                if (Font == null)
                {
                    Font = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Segoe UI", "Tahoma" }, 32);
                    _ownsFont = true;
                }
            }

            if (OverlayMaterial == null)
            {
                Material baseMaterial = Canvas.GetDefaultCanvasMaterial();
                OverlayMaterial = new Material(baseMaterial) { name = "GTSVRBT_UI_Overlay" };
                OverlayMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
                DepthMaterial = new Material(baseMaterial) { name = "GTSVRBT_UI_Depth" };
                DepthMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
            }
        }

        public static Material MaterialFor(bool alwaysOnTop) => alwaysOnTop ? OverlayMaterial : DepthMaterial;

        /// <summary>Picks a layer the game camera actually renders (prefers Default, then UI).</summary>
        public static int PickVisibleLayer(Camera camera)
        {
            if (camera == null) return 0;
            int mask = camera.cullingMask;
            if ((mask & 1) != 0) return 0;
            if ((mask & (1 << 5)) != 0) return 5;
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
            return 0;
        }

        public static void Dispose()
        {
            if (OverlayMaterial != null) UnityEngine.Object.Destroy(OverlayMaterial);
            if (DepthMaterial != null) UnityEngine.Object.Destroy(DepthMaterial);
            OverlayMaterial = null;
            DepthMaterial = null;
            if (_ownsFont && Font != null) UnityEngine.Object.Destroy(Font);
            if (_ownsFont) Font = null;
            _ownsFont = false;
        }
    }
}
