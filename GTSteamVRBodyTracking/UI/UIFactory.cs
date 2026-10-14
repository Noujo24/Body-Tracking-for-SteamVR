using System;
using UnityEngine;
using UnityEngine.UI;

namespace GTSteamVRBodyTracking.UI
{
    /// <summary>
    /// Builds uGUI elements with absolute layout: (x, y) is the top-left corner in parent pixels, y grows downward.
    /// No colliders and no EventSystem are involved; interaction is handled by <see cref="PokeInteractor"/>.
    /// </summary>
    internal static class UIFactory
    {
        public static Material CurrentMaterial;

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UIStyle.Layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static RectTransform Container(Transform parent, string name, float x, float y, float w, float h)
        {
            RectTransform rt = Rect(parent, name);
            Place(rt, x, y, w, h);
            return rt;
        }

        public static Image Panel(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            RectTransform rt = Rect(parent, name);
            Place(rt, x, y, w, h);
            return AddImage(rt, color);
        }

        public static Image AddImage(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.material = CurrentMaterial;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string text, float x, float y, float w, float h,
            int size = UIStyle.FontNormal, TextAnchor anchor = TextAnchor.MiddleLeft, Color? color = null)
        {
            RectTransform rt = Rect(parent, "Text");
            Place(rt, x, y, w, h);
            return AddText(rt, text, size, anchor, color ?? UIStyle.Text);
        }

        public static Text AddText(RectTransform rt, string text, int size, TextAnchor anchor, Color color)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = UIStyle.Font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.supportRichText = false;
            t.raycastTarget = false;
            t.material = CurrentMaterial;
            return t;
        }

        private static T Setup<T>(T control, Transform parent, string label, float x, float y, float w, float h, int size) where T : UIControl
        {
            RectTransform rt = Rect(parent, typeof(T).Name);
            Place(rt, x, y, w, h);
            control.Rect = rt;
            control.Background = AddImage(rt, control.NormalColor);
            if (label != null)
            {
                RectTransform lr = Rect(rt, "Label");
                Stretch(lr);
                lr.offsetMin = new Vector2(8f, 2f);
                lr.offsetMax = new Vector2(-8f, -2f);
                control.Label = AddText(lr, label, size, TextAnchor.MiddleCenter, UIStyle.Text);
            }
            UIControlRegistry.Register(control);
            return control;
        }

        public static UIButton Button(Transform parent, string label, float x, float y, float w, float h, Action onClick, int size = UIStyle.FontNormal)
        {
            UIButton b = Setup(new UIButton { OnClick = onClick }, parent, label, x, y, w, h, size);
            return b;
        }

        public static UIToggle Toggle(Transform parent, string caption, float x, float y, float w, float h, Action<bool> onChanged, int size = UIStyle.FontNormal)
        {
            UIToggle t = Setup(new UIToggle { Caption = caption, OnChanged = onChanged }, parent, caption, x, y, w, h, size);
            t.SetValue(false);
            return t;
        }

        public static UISlider Slider(Transform parent, float x, float y, float w, float h, float min, float max, Action<float> onChanged)
        {
            UISlider s = Setup(new UISlider { Min = min, Max = max, OnChanged = onChanged, NormalColor = UIStyle.ButtonDisabled }, parent, null, x, y, w, h, UIStyle.FontNormal);

            RectTransform fill = Rect(s.Rect, "Fill");
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = new Vector2(0f, 6f);
            fill.offsetMax = new Vector2(0f, -6f);
            AddImage(fill, UIStyle.Accent);

            RectTransform handle = Rect(s.Rect, "Handle");
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            handle.sizeDelta = new Vector2(22f, 8f);
            handle.anchoredPosition = Vector2.zero;
            AddImage(handle, UIStyle.Text);

            s.Fill = fill;
            s.Handle = handle;
            s.SetValue(min);
            return s;
        }

        public static UIBlocker Blocker(RectTransform parent)
        {
            RectTransform rt = Rect(parent, "Blocker");
            Stretch(rt);
            var b = new UIBlocker { Rect = rt };
            UIControlRegistry.Register(b);
            return b;
        }

        public static void SetText(Text t, string value)
        {
            if (t != null && t.text != value) t.text = value;
        }

        public static void SetColor(Graphic g, Color c)
        {
            if (g != null && g.color != c) g.color = c;
        }

        public static void SetActive(Component c, bool active)
        {
            if (c != null && c.gameObject.activeSelf != active) c.gameObject.SetActive(active);
        }
    }
}
