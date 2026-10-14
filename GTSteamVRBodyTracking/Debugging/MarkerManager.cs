using GTSteamVRBodyTracking.Behaviours;
using System;
using System.Collections.Generic;
using GTSteamVRBodyTracking.Core;
using GTSteamVRBodyTracking.Tools;
using GTSteamVRBodyTracking.Tracking;
using GTSteamVRBodyTracking.UI;
using GTSteamVRBodyTracking.Extensions;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace GTSteamVRBodyTracking.Debugging
{
    /// <summary>
    /// Independent tracker-debug visualisation. Markers follow the mapped WORLD pose of each tracker.
    /// They have no colliders (removed on creation), are local-only, and are destroyed on unload.
    /// Hiding markers never stops discovery or sampling.
    /// </summary>
    internal sealed class MarkerManager
    {
        public const float IdentifySeconds = 4f;
        private const float HideLostAfterSeconds = 30f;

        private sealed class Marker
        {
            public string Key;
            public GameObject Root;
            public Transform Sphere;
            public Transform AxisX, AxisY, AxisZ, BodyArrow;
            public Material SphereMat, BodyMat;
            public Canvas LabelCanvas;
            public Text Label;
            public Color AppliedColor = new Color(-1, 0, 0, 0);
            public bool AppliedOnTop;
            public bool AppliedVisible = true;
            public int LabelSignature = -1;
        }

        private readonly Transform _parent;
        private readonly Dictionary<string, Marker> _markers = new Dictionary<string, Marker>(StringComparer.Ordinal);
        private readonly List<string> _removeBuffer = new List<string>();
        private readonly Shader _shader;
        private Material _axisX, _axisY, _axisZ;
        private int _listRevision = -1;
        private bool _styleReady;
        private string _identifyKey;
        private float _identifyUntil;

        public MarkerManager(Transform parent)
        {
            _parent = parent;
            _shader = Shader.Find("Hidden/Internal-Colored");
            if (_shader == null) ModLog.Warn("Shader 'Hidden/Internal-Colored' not found; markers use the UI material.");
            _axisX = MakeMaterial(new Color(0.95f, 0.25f, 0.25f), true);
            _axisY = MakeMaterial(new Color(0.25f, 0.9f, 0.3f), true);
            _axisZ = MakeMaterial(new Color(0.3f, 0.5f, 1f), true);
        }

        public void Identify(string key, float now)
        {
            _identifyKey = key;
            _identifyUntil = now + IdentifySeconds;
        }

        public bool IsIdentifying(string key, float now) => key != null && key == _identifyKey && now < _identifyUntil;

        public void Update(ModRuntime rt, float now)
        {
            TrackerService tracker = rt.Tracker;
            Camera cam = tracker.Mapper.Camera;
            // Nothing can be placed before the game camera exists; this also lets us pick a layer it renders.
            if (cam == null) return;
            if (!_styleReady)
            {
                UIStyle.Init();
                UIStyle.Layer = UIStyle.PickVisibleLayer(cam);
                _styleReady = true;
            }

            if (_listRevision != tracker.ListRevision)
            {
                _listRevision = tracker.ListRevision;
                SyncMarkers(tracker);
            }

            UiPreferences ui = rt.Store.Data.Ui;
            bool modEnabled = rt.Store.Data.ModEnabled;
            Vector3 camPos = cam.transform.position;
            float scale = tracker.Mapper.Valid ? tracker.Mapper.TrackingToWorld.Scale : 1f;
            float size = ui.MarkerSize * scale;

            foreach (TrackerRuntime t in tracker.Trackers)
            {
                if (!_markers.TryGetValue(t.Key, out Marker m)) continue;
                bool identifying = IsIdentifying(t.Key, now);
                bool wanted = modEnabled && (identifying || (ui.ShowDebugMarkers && t.Settings.ShowMarker && t.Settings.Enabled));

                bool hasPose = false;
                bool current = false;
                RigidPose pose = default;
                if (t.WorldValid)
                {
                    pose = t.World;
                    hasPose = true;
                    current = true;
                }
                else if (t.LastValidTime >= 0f && now - t.LastValidTime < HideLostAfterSeconds && tracker.Mapper.Valid)
                {
                    // Last known pose, re-mapped so it moves with the player; drawn grey and labelled as NOT current.
                    pose = tracker.Mapper.TrackingToWorld.Apply(t.FilteredTracking);
                    hasPose = true;
                }

                bool visible = wanted && hasPose;
                if (m.AppliedVisible != visible)
                {
                    m.Root.SetActive(visible);
                    m.AppliedVisible = visible;
                }
                if (!visible) continue;

                float pulse = identifying ? 1.6f + 0.4f * Mathf.Sin(now * 12f) : 1f;
                m.Root.transform.SetPositionAndRotation(pose.Position.ToUnity(), pose.Rotation.ToUnity());
                m.Sphere.localScale = Vector3.one * (size * pulse);
                SetAxis(m.AxisX, new Vector3(size * 0.9f, 0f, 0f), new Vector3(size * 1.8f, size * 0.12f, size * 0.12f));
                SetAxis(m.AxisY, new Vector3(0f, size * 0.9f, 0f), new Vector3(size * 0.12f, size * 1.8f, size * 0.12f));
                SetAxis(m.AxisZ, new Vector3(0f, 0f, size * 0.9f), new Vector3(size * 0.12f, size * 0.12f, size * 1.8f));

                // Calibrated body frame: arrow along the body's forward. After calibrating, it should point where you face.
                TrackerCalibration cal = t.Settings.Calibration;
                bool showBody = cal != null && cal.RoleAtCalibration == t.Role && current;
                if (m.BodyArrow.gameObject.activeSelf != showBody) m.BodyArrow.gameObject.SetActive(showBody);
                if (showBody)
                {
                    Quaternion body = (pose.Rotation * cal.MountRotation).ToUnity();
                    m.BodyArrow.SetPositionAndRotation(pose.Position.ToUnity() + body * new Vector3(0f, 0f, size * 2f), body);
                    m.BodyArrow.localScale = new Vector3(size * 0.25f, size * 0.25f, size * 4f);
                }

                Color c = identifying
                    ? Color.Lerp(Color.yellow, Color.white, 0.5f + 0.5f * Mathf.Sin(now * 12f))
                    : !current ? (t.Connected ? new Color(0.55f, 0.55f, 0.55f, 0.85f) : new Color(0.7f, 0.25f, 0.25f, 0.85f))
                    : RoleColor(t.Role);
                ApplyLook(m, c, ui.MarkersThroughWalls);

                bool showLabel = ui.ShowMarkerLabels || identifying;
                if (m.LabelCanvas.gameObject.activeSelf != showLabel) m.LabelCanvas.gameObject.SetActive(showLabel);
                if (showLabel)
                {
                    Transform lt = m.LabelCanvas.transform;
                    Vector3 lp = pose.Position.ToUnity() + Vector3.up * (size * 1.6f);
                    Vector3 toLabel = lp - camPos;
                    lt.SetPositionAndRotation(lp, toLabel.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toLabel, Vector3.up) : Quaternion.identity);
                    lt.localScale = Vector3.one * (0.0012f * Mathf.Max(0.35f, scale));
                    // Rebuild the label string only when what it shows changes (no per-frame allocations).
                    int signature = ((int)t.State << 8) | ((int)t.Role << 2) | (identifying ? 2 : 0) | (current ? 1 : 0) | (t.Connected ? 1 << 16 : 0);
                    if (signature != m.LabelSignature)
                    {
                        m.LabelSignature = signature;
                        string state = !t.Connected ? "DISCONNECTED (last known)" : current ? PoseValidity.Describe(t.State) : PoseValidity.Describe(t.State).ToUpperInvariant() + " (last known)";
                        m.Label.text = (identifying ? ">> " : "") + t.Label + "  " + TrackerIdentity.ShortId(t.Record.Serial) + "\n" + state;
                        m.Label.color = current ? UIStyle.Text : UIStyle.Warn;
                    }
                }
            }
        }

        private static void SetAxis(Transform axis, Vector3 localPos, Vector3 localScale)
        {
            axis.localPosition = localPos;
            axis.localScale = localScale;
        }

        private void SyncMarkers(TrackerService tracker)
        {
            _removeBuffer.Clear();
            foreach (string key in _markers.Keys) if (tracker.Find(key) == null) _removeBuffer.Add(key);
            foreach (string key in _removeBuffer)
            {
                Destroy(_markers[key]);
                _markers.Remove(key);
            }
            foreach (TrackerRuntime t in tracker.Trackers)
                if (!_markers.ContainsKey(t.Key)) _markers[t.Key] = Create(t.Key);
        }

        private Marker Create(string key)
        {
            var m = new Marker { Key = key };
            m.Root = new GameObject("GTSVRBT_Marker_" + key);
            m.Root.layer = UIStyle.Layer;
            m.Root.transform.SetParent(_parent, false);

            m.SphereMat = MakeMaterial(Color.white, true);
            m.BodyMat = MakeMaterial(new Color(1f, 0.85f, 0.2f), true);
            m.Sphere = Primitive(PrimitiveType.Sphere, m.Root.transform, m.SphereMat);
            m.AxisX = Primitive(PrimitiveType.Cube, m.Root.transform, _axisX);
            m.AxisY = Primitive(PrimitiveType.Cube, m.Root.transform, _axisY);
            m.AxisZ = Primitive(PrimitiveType.Cube, m.Root.transform, _axisZ);
            m.BodyArrow = Primitive(PrimitiveType.Cube, _parent, m.BodyMat);
            m.BodyArrow.SetParent(m.Root.transform, true);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.layer = UIStyle.Layer;
            labelGo.transform.SetParent(m.Root.transform, false);
            m.LabelCanvas = labelGo.AddComponent<Canvas>();
            m.LabelCanvas.renderMode = RenderMode.WorldSpace;
            m.LabelCanvas.sortingOrder = 31000;
            var rt = (RectTransform)labelGo.transform;
            rt.sizeDelta = new Vector2(420f, 90f);
            rt.pivot = new Vector2(0.5f, 0f);
            m.Label = UIFactory.AddText(UIFactory.Rect(rt, "Text"), string.Empty, 30, TextAnchor.LowerCenter, UIStyle.Text);
            UIFactory.Stretch(m.Label.rectTransform);
            m.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
            m.Label.material = UIStyle.OverlayMaterial;

            m.Root.SetActive(false);
            m.AppliedVisible = false;
            return m;
        }

        private static Transform Primitive(PrimitiveType type, Transform parent, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            // Remove the auto-added collider immediately so markers can never touch gameplay physics.
            Collider col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            go.layer = UIStyle.Layer;
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        private Material MakeMaterial(Color c, bool onTop)
        {
            Material mat = _shader != null ? new Material(_shader) : new Material(Canvas.GetDefaultCanvasMaterial());
            mat.name = "GTSVRBT_Marker";
            mat.color = c;
            SetDepth(mat, onTop);
            return mat;
        }

        private static void SetDepth(Material mat, bool onTop)
        {
            if (mat.HasProperty("_ZTest")) mat.SetInt("_ZTest", (int)(onTop ? CompareFunction.Always : CompareFunction.LessEqual));
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", onTop ? 0 : 1);
            if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)CullMode.Back);
            if (mat.HasProperty("unity_GUIZTestMode")) mat.SetInt("unity_GUIZTestMode", (int)(onTop ? CompareFunction.Always : CompareFunction.LessEqual));
            mat.renderQueue = onTop ? 4000 : 2450;
        }

        private void ApplyLook(Marker m, Color c, bool onTop)
        {
            if (m.AppliedColor != c)
            {
                m.SphereMat.color = c;
                m.AppliedColor = c;
            }
            if (m.AppliedOnTop != onTop)
            {
                SetDepth(m.SphereMat, onTop);
                SetDepth(m.BodyMat, onTop);
                SetDepth(_axisX, onTop);
                SetDepth(_axisY, onTop);
                SetDepth(_axisZ, onTop);
                m.AppliedOnTop = onTop;
            }
        }

        public static Color RoleColor(BodyRole role)
        {
            switch (role)
            {
                case BodyRole.Waist: return new Color(1f, 0.55f, 0.15f);
                case BodyRole.Chest: return new Color(0.95f, 0.3f, 0.65f);
                case BodyRole.LeftFoot: return new Color(0.2f, 0.7f, 1f);
                case BodyRole.RightFoot: return new Color(0.2f, 1f, 0.75f);
                case BodyRole.LeftKnee: return new Color(0.45f, 0.5f, 1f);
                case BodyRole.RightKnee: return new Color(0.5f, 1f, 0.4f);
                case BodyRole.LeftElbow: return new Color(0.75f, 0.45f, 1f);
                case BodyRole.RightElbow: return new Color(1f, 0.9f, 0.3f);
                case BodyRole.LeftHip: return new Color(0.3f, 0.85f, 0.9f);
                case BodyRole.RightHip: return new Color(0.9f, 0.75f, 0.5f);
                case BodyRole.Custom: return new Color(0.85f, 0.85f, 0.85f);
                default: return new Color(0.6f, 0.65f, 0.75f);
            }
        }

        private static void Destroy(Marker m)
        {
            if (m.Root != null) UnityEngine.Object.Destroy(m.Root);
            if (m.SphereMat != null) UnityEngine.Object.Destroy(m.SphereMat);
            if (m.BodyMat != null) UnityEngine.Object.Destroy(m.BodyMat);
        }

        public void Dispose()
        {
            foreach (Marker m in _markers.Values) Destroy(m);
            _markers.Clear();
            if (_axisX != null) UnityEngine.Object.Destroy(_axisX);
            if (_axisY != null) UnityEngine.Object.Destroy(_axisY);
            if (_axisZ != null) UnityEngine.Object.Destroy(_axisZ);
        }
    }
}
