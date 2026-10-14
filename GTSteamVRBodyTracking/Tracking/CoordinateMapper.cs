using GTSteamVRBodyTracking.Config;
using GTSteamVRBodyTracking.Core;
using GTSteamVRBodyTracking.Extensions;
using UnityEngine;

namespace GTSteamVRBodyTracking.Tracking
{
    internal enum MappingMode
    {
        Unavailable,
        TrackingOrigin,
        HmdAnchor
    }

    /// <summary>
    /// Resolves the OpenVR-tracking-space → Unity-world mapping at runtime, without assuming origins match.
    ///  * TrackingOrigin: verified when the camera's LOCAL pose equals the converted OpenVR headset pose
    ///    (i.e. camera.parent is the tracking origin). World = parent.TransformPoint(trackingPos) incl. scale.
    ///  * HmdAnchor: otherwise; world = cameraWorld · inverse(hmdTracking), scaled by parent.lossyScale.
    /// The measured agreement error is exposed so the UI can show what was verified.
    /// </summary>
    internal sealed class CoordinateMapper
    {
        private const float AgreePositionTolerance = 0.03f; // metres
        private const float AgreeAngleTolerance = 3f;       // degrees
        private const int FramesToTrustOrigin = 90;
        private const int FramesToDistrustOrigin = 20;

        private Camera _camera;
        private float _nextCameraLookup;
        private int _agreeStreak;
        private int _disagreeStreak;

        public CoordinateModeSetting Setting { get; set; } = CoordinateModeSetting.Auto;
        public MappingMode Mode { get; private set; } = MappingMode.Unavailable;
        public SimilarityTransform TrackingToWorld { get; private set; } = SimilarityTransform.Identity;
        public bool Valid { get; private set; }
        public float PositionError { get; private set; }
        public float AngleError { get; private set; }
        public string CameraName { get; private set; } = "none";
        public Camera Camera => _camera;

        public void Update(RigidPose hmdTracking, bool hmdValid, float now)
        {
            if (_camera == null && now >= _nextCameraLookup)
            {
                _nextCameraLookup = now + 2f;
                _camera = FindGameCamera();
                CameraName = _camera != null ? _camera.name : "none";
            }

            if (_camera == null || !hmdValid)
            {
                Valid = false;
                Mode = MappingMode.Unavailable;
                return;
            }

            Transform cam = _camera.transform;
            Transform parent = cam.parent;
            float scale = parent != null ? Mathf.Max(1e-4f, parent.lossyScale.x) : 1f;

            // Agreement check (camera-local pose is in tracking metres, unaffected by parent scale).
            RigidPose camLocal = cam.LocalPose();
            float posErr = Vec3.Distance(camLocal.Position, hmdTracking.Position);
            float angErr = Quat.AngleBetween(camLocal.Rotation, hmdTracking.Rotation);
            PositionError = Mathf.Lerp(PositionError, posErr, 0.1f);
            AngleError = Mathf.Lerp(AngleError, angErr, 0.1f);
            bool agrees = parent != null && posErr < AgreePositionTolerance && angErr < AgreeAngleTolerance;
            if (agrees) { _agreeStreak++; _disagreeStreak = 0; }
            else { _disagreeStreak++; _agreeStreak = 0; }

            MappingMode mode;
            switch (Setting)
            {
                case CoordinateModeSetting.TrackingOrigin:
                    mode = parent != null ? MappingMode.TrackingOrigin : MappingMode.HmdAnchor;
                    break;
                case CoordinateModeSetting.HmdAnchor:
                    mode = MappingMode.HmdAnchor;
                    break;
                default:
                    mode = Mode;
                    if (mode != MappingMode.TrackingOrigin && _agreeStreak >= FramesToTrustOrigin) mode = MappingMode.TrackingOrigin;
                    else if (mode == MappingMode.TrackingOrigin && _disagreeStreak >= FramesToDistrustOrigin) mode = MappingMode.HmdAnchor;
                    else if (mode == MappingMode.Unavailable) mode = MappingMode.HmdAnchor;
                    break;
            }

            Mode = mode;
            TrackingToWorld = mode == MappingMode.TrackingOrigin
                ? new SimilarityTransform(parent.position.ToCore(), parent.rotation.ToCore(), scale)
                : SimilarityTransform.FromAnchor(cam.WorldPose(), hmdTracking, scale);
            Valid = true;
        }

        public void ResetVerification()
        {
            _agreeStreak = 0;
            _disagreeStreak = 0;
            Mode = MappingMode.Unavailable;
            _camera = null;
            _nextCameraLookup = 0f;
        }

        public static string Describe(MappingMode mode)
        {
            switch (mode)
            {
                case MappingMode.TrackingOrigin: return "Tracking origin (verified)";
                case MappingMode.HmdAnchor: return "Headset anchor";
                default: return "Unavailable";
            }
        }

        private static Camera FindGameCamera()
        {
            try
            {
                GorillaLocomotion.GTPlayer player = GorillaLocomotion.GTPlayer.Instance;
                if (player != null && player.mainCamera != null) return player.mainCamera;
            }
            catch (System.Exception) { /* game not initialised yet */ }

            try
            {
                GorillaTagger tagger = GorillaTagger.Instance;
                if (tagger != null && tagger.mainCamera != null)
                {
                    Camera c = tagger.mainCamera.GetComponent<Camera>();
                    if (c != null) return c;
                }
            }
            catch (System.Exception) { }

            return Camera.main;
        }
    }
}
