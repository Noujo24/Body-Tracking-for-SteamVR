namespace GTSteamVRBodyTracking.Core
{
    /// <summary>
    /// Per-tracker processing in TRACKING space (before world mapping), so player locomotion
    /// never introduces smoothing lag: raw pose → local offsets → exponential smoothing.
    /// </summary>
    public sealed class PoseFilter
    {
        /// <summary>Jumps larger than this snap instead of gliding (tracking re-acquire, universe change).</summary>
        public const float SnapDistance = 0.5f;
        public const float SnapAngle = 90f;
        public const float MaxGapSeconds = 0.25f;

        private RigidPose _state;
        private bool _hasState;

        public bool HasState => _hasState;
        public RigidPose State => _state;

        /// <summary>Forget history (call whenever the pose becomes invalid so stale data is never blended in).</summary>
        public void Reset() => _hasState = false;

        public static RigidPose ApplyOffsets(RigidPose raw, Vec3 positionOffset, Vec3 rotationOffsetEuler)
        {
            Quat rot = (raw.Rotation * Quat.Euler(rotationOffsetEuler)).GetNormalized();
            Vec3 pos = raw.Position + raw.Rotation * positionOffset;
            return new RigidPose(pos, rot);
        }

        /// <summary>Converts the 0..0.95 smoothing setting into a frame-rate independent lerp factor.</summary>
        public static float SmoothingToLerp(float smoothing, float deltaTime)
        {
            float s = MathUtil.Clamp(smoothing, ConfigLimits.MinSmoothing, ConfigLimits.MaxSmoothing);
            if (s <= 0f) return 1f;
            float frames = MathUtil.Clamp(deltaTime * 60f, 0f, 10f);
            return 1f - (float)System.Math.Pow(s, frames);
        }

        public RigidPose Process(RigidPose raw, Vec3 positionOffset, Vec3 rotationOffsetEuler, float smoothing, float deltaTime, float secondsSinceLastValid)
        {
            RigidPose target = ApplyOffsets(raw, positionOffset, rotationOffsetEuler);

            bool snap = !_hasState
                        || secondsSinceLastValid > MaxGapSeconds
                        || Vec3.Distance(_state.Position, target.Position) > SnapDistance
                        || Quat.AngleBetween(_state.Rotation, target.Rotation) > SnapAngle;

            if (snap)
            {
                _state = target;
            }
            else
            {
                float t = SmoothingToLerp(smoothing, deltaTime);
                _state = new RigidPose(
                    Vec3.Lerp(_state.Position, target.Position, t),
                    Quat.Slerp(_state.Rotation, target.Rotation, t));
            }

            _hasState = true;
            return _state;
        }
    }
}
