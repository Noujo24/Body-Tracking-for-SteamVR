using System;
using System.Collections.Generic;
using GTSteamVRBodyTracking.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GTSteamVRBodyTracking.UI
{
    /// <summary>All interactive controls, in creation order (later = on top for hit testing).</summary>
    internal static class UIControlRegistry
    {
        private static readonly List<UIControl> Controls = new List<UIControl>();

        public static IReadOnlyList<UIControl> All => Controls;
        public static void Register(UIControl c) => Controls.Add(c);
        public static void Clear() => Controls.Clear();

        /// <summary>Topmost active control under a world-space point (projected onto each control's plane).</summary>
        public static UIControl HitTest(Vector3 worldPoint)
        {
            for (int i = Controls.Count - 1; i >= 0; i--)
            {
                UIControl c = Controls[i];
                if (!c.IsVisible) continue;
                Vector2 local = c.Rect.InverseTransformPoint(worldPoint);
                if (c.Rect.rect.Contains(local)) return c.IsInteractable || c.BlocksInput ? c : null;
            }
            return null;
        }
    }

    internal abstract class UIControl
    {
        public RectTransform Rect;
        public Image Background;
        public Text Label;
        public bool Interactable = true;
        public Color NormalColor = UIStyle.Button;
        public Color LabelColor = UIStyle.Text;

        private float _pressedUntil;
        private bool _hover;
        private Color _appliedBg = new Color(-1f, 0f, 0f, 0f);
        private Color _appliedLabel = new Color(-1f, 0f, 0f, 0f);

        public virtual bool Draggable => false;
        public virtual bool BlocksInput => false;
        public bool IsVisible => Rect != null && Rect.gameObject.activeInHierarchy;
        public bool IsInteractable => Interactable && IsVisible;

        public void Press(Vector3 worldPoint, float now)
        {
            if (!IsInteractable) return;
            _pressedUntil = now + 0.15f;
            OnPress(LocalNormalized(worldPoint));
        }

        public void Drag(Vector3 worldPoint)
        {
            if (IsInteractable) OnDrag(LocalNormalized(worldPoint));
        }

        protected abstract void OnPress(Vector2 normalized);
        protected virtual void OnDrag(Vector2 normalized) { }

        /// <summary>0..1 position inside the control (x left→right, y bottom→top).</summary>
        private Vector2 LocalNormalized(Vector3 worldPoint)
        {
            Vector2 local = Rect.InverseTransformPoint(worldPoint);
            Rect r = Rect.rect;
            return new Vector2(Mathf.InverseLerp(r.xMin, r.xMax, local.x), Mathf.InverseLerp(r.yMin, r.yMax, local.y));
        }

        public void SetHover(bool hover) => _hover = hover;

        public void SetLabel(string text)
        {
            if (Label != null && Label.text != text) Label.text = text;
        }

        public virtual void UpdateVisual(float now)
        {
            if (Background == null) return;
            Color bg;
            if (!Interactable) bg = UIStyle.ButtonDisabled;
            else if (now < _pressedUntil) bg = UIStyle.ButtonPressed;
            else if (_hover) bg = Color.Lerp(NormalColor, UIStyle.ButtonHover, 0.6f);
            else bg = NormalColor;
            if (bg != _appliedBg)
            {
                Background.color = bg;
                _appliedBg = bg;
            }
            if (Label != null)
            {
                Color lc = Interactable ? LabelColor : UIStyle.TextDisabled;
                if (lc != _appliedLabel)
                {
                    Label.color = lc;
                    _appliedLabel = lc;
                }
            }
        }
    }

    internal sealed class UIButton : UIControl
    {
        public Action OnClick;
        protected override void OnPress(Vector2 normalized) => OnClick?.Invoke();
    }

    internal sealed class UIToggle : UIControl
    {
        public string Caption;
        public bool Value { get; private set; }
        public Action<bool> OnChanged;

        public void SetValue(bool value)
        {
            Value = value;
            NormalColor = value ? UIStyle.ToggleOn : UIStyle.ToggleOff;
            SetLabel(Caption + (value ? ": ON" : ": OFF"));
        }

        protected override void OnPress(Vector2 normalized)
        {
            SetValue(!Value);
            OnChanged?.Invoke(Value);
        }
    }

    internal sealed class UISlider : UIControl
    {
        public float Min;
        public float Max = 1f;
        public RectTransform Fill;
        public RectTransform Handle;
        public Action<float> OnChanged;
        public float Value { get; private set; }

        public override bool Draggable => true;

        public void SetValue(float value)
        {
            Value = MathUtil.Clamp(value, Min, Max);
            float t = Max > Min ? (Value - Min) / (Max - Min) : 0f;
            if (Fill != null) Fill.anchorMax = new Vector2(t, 1f);
            if (Handle != null)
            {
                Handle.anchorMin = new Vector2(t, 0f);
                Handle.anchorMax = new Vector2(t, 1f);
            }
        }

        protected override void OnPress(Vector2 normalized) => Apply(normalized.x);
        protected override void OnDrag(Vector2 normalized) => Apply(normalized.x);

        private void Apply(float t)
        {
            float v = Mathf.Lerp(Min, Max, Mathf.Clamp01(t));
            v = Mathf.Round(v * 100f) / 100f;
            if (Mathf.Abs(v - Value) < 1e-4f) return;
            SetValue(v);
            OnChanged?.Invoke(Value);
        }
    }

    /// <summary>Invisible full-panel control that swallows presses behind a modal popup.</summary>
    internal sealed class UIBlocker : UIControl
    {
        public override bool BlocksInput => true;
        protected override void OnPress(Vector2 normalized) { }
        public override void UpdateVisual(float now) { }
    }
}
