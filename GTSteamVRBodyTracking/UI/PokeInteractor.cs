using System;
using GTSteamVRBodyTracking.Tools;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GTSteamVRBodyTracking.UI
{
    /// <summary>
    /// VR fingertip "poke" interaction, like the game's own buttons: the fingertips are the game's
    /// GorillaTagger.left/rightHandTriggerCollider objects (verified fields). Pure math; adds no
    /// colliders, so arm locomotion is unaffected. A desktop mouse ray is supported as a helper.
    /// </summary>
    internal sealed class PokeInteractor
    {
        // Depth is measured along the canvas normal in metres; negative = in front of the panel (user side).
        private const float HoverDepth = -0.10f;
        private const float TouchDepth = -0.012f;
        private const float ReArmDepth = -0.03f;
        private const float BehindLimit = 0.08f;
        private const float Cooldown = 0.22f;

        private struct Pointer
        {
            public bool Armed;
            public float CooldownUntil;
            public UIControl Pressed;
            public UIControl Hover;
        }

        private readonly RectTransform _canvas;
        private readonly Image _leftCursor;
        private readonly Image _rightCursor;
        private Pointer _left;
        private Pointer _right;
        private Pointer _mouse;

        public PokeInteractor(RectTransform canvas)
        {
            _canvas = canvas;
            _leftCursor = MakeCursor("LeftCursor");
            _rightCursor = MakeCursor("RightCursor");
        }

        private Image MakeCursor(string name)
        {
            RectTransform rt = UIFactory.Rect(_canvas, name);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(18f, 18f);
            Image img = UIFactory.AddImage(rt, UIStyle.Accent);
            rt.gameObject.SetActive(false);
            return img;
        }

        public void ResetPointers()
        {
            _left = default;
            _right = default;
            _mouse = default;
            _leftCursor.gameObject.SetActive(false);
            _rightCursor.gameObject.SetActive(false);
        }

        public void Update(Camera camera, float now)
        {
            Transform leftTip = null, rightTip = null;
            try
            {
                GorillaTagger tagger = GorillaTagger.Instance;
                if (tagger != null)
                {
                    if (tagger.leftHandTriggerCollider != null) leftTip = tagger.leftHandTriggerCollider.transform;
                    if (tagger.rightHandTriggerCollider != null) rightTip = tagger.rightHandTriggerCollider.transform;
                }
            }
            catch (Exception e) { ModLog.Exception("poke-tips", e, "Reading fingertip transforms failed"); }

            ClearHover(ref _left);
            ClearHover(ref _right);
            ClearHover(ref _mouse);

            ProcessFinger(ref _left, leftTip, _leftCursor, true, now);
            ProcessFinger(ref _right, rightTip, _rightCursor, false, now);
            ProcessMouse(camera, now);

            for (int i = 0; i < UIControlRegistry.All.Count; i++) UIControlRegistry.All[i].UpdateVisual(now);
        }

        private static void ClearHover(ref Pointer p)
        {
            p.Hover?.SetHover(false);
            p.Hover = null;
        }

        private void ProcessFinger(ref Pointer p, Transform tip, Image cursor, bool isLeft, float now)
        {
            if (tip == null)
            {
                cursor.gameObject.SetActive(false);
                p = default;
                return;
            }

            Vector3 world = tip.position;
            Vector3 local = _canvas.InverseTransformPoint(world);
            float depth = local.z * _canvas.lossyScale.z;
            Rect r = _canvas.rect;
            bool overPanel = r.Contains(new Vector2(local.x, local.y));

            bool showCursor = overPanel && depth > HoverDepth && depth < BehindLimit;
            cursor.gameObject.SetActive(showCursor);
            if (showCursor)
            {
                cursor.rectTransform.anchoredPosition = new Vector2(local.x, local.y);
                float closeness = Mathf.InverseLerp(HoverDepth, TouchDepth, depth);
                UIFactory.SetColor(cursor, Color.Lerp(UIStyle.TextDim, UIStyle.Accent, closeness));
            }

            if (depth < ReArmDepth || !overPanel) p.Armed = true;
            bool touching = overPanel && depth > TouchDepth && depth < BehindLimit;

            if (showCursor)
            {
                UIControl hover = UIControlRegistry.HitTest(world);
                if (hover != null && !hover.BlocksInput)
                {
                    hover.SetHover(true);
                    p.Hover = hover;
                }
            }

            if (touching && p.Armed && now >= p.CooldownUntil)
            {
                p.Armed = false;
                p.CooldownUntil = now + Cooldown;
                UIControl hit = UIControlRegistry.HitTest(world);
                if (hit != null && !hit.BlocksInput)
                {
                    hit.Press(world, now);
                    p.Pressed = hit;
                    Haptic(isLeft);
                }
            }

            if (p.Pressed != null)
            {
                if (touching && p.Pressed.Draggable) p.Pressed.Drag(world);
                else if (!touching) p.Pressed = null;
            }
        }

        private void ProcessMouse(Camera camera, float now)
        {
            if (camera == null) return;
            Mouse mouse;
            try { mouse = Mouse.current; }
            catch (Exception) { return; }
            if (mouse == null) return;

            Vector2 screen = mouse.position.ReadValue();
            Ray ray = camera.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            var plane = new Plane(-_canvas.forward, _canvas.position);
            if (!plane.Raycast(ray, out float distance)) { _mouse.Pressed = null; return; }
            Vector3 world = ray.GetPoint(distance);
            Vector3 local = _canvas.InverseTransformPoint(world);
            if (!_canvas.rect.Contains(new Vector2(local.x, local.y))) { _mouse.Pressed = null; return; }

            UIControl hover = UIControlRegistry.HitTest(world);
            if (hover != null && !hover.BlocksInput)
            {
                hover.SetHover(true);
                _mouse.Hover = hover;
            }

            if (mouse.leftButton.wasPressedThisFrame && hover != null && !hover.BlocksInput)
            {
                hover.Press(world, now);
                _mouse.Pressed = hover;
            }
            else if (mouse.leftButton.isPressed && _mouse.Pressed != null && _mouse.Pressed.Draggable)
            {
                _mouse.Pressed.Drag(world);
            }
            else if (!mouse.leftButton.isPressed)
            {
                _mouse.Pressed = null;
            }
        }

        private static void Haptic(bool left)
        {
            try
            {
                GorillaTagger tagger = GorillaTagger.Instance;
                if (tagger != null) tagger.StartVibration(left, tagger.tapHapticStrength * 0.5f, tagger.tapHapticDuration);
            }
            catch (Exception) { /* haptics are optional */ }
        }
    }
}
