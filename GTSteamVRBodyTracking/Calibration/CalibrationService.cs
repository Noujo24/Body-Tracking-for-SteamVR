using System;
using System.Collections.Generic;
using System.Text;
using GTSteamVRBodyTracking.Core;
using GTSteamVRBodyTracking.Tools;
using GTSteamVRBodyTracking.Tracking;
using GTSteamVRBodyTracking.Extensions;
using UnityEngine;

namespace GTSteamVRBodyTracking.Calibration
{
    internal enum CalibrationPhase
    {
        Idle,
        Countdown,
        Capturing,
        Succeeded,
        Failed
    }

    /// <summary>
    /// Captures neutral-pose references in TRACKING space. Every capture recomputes from the current
    /// offset-corrected (unsmoothed) poses, so repeated calibration never accumulates.
    /// Results are applied in memory immediately (so alignment can be checked) and persisted on Save.
    /// </summary>
    internal sealed class CalibrationService
    {
        public const float CountdownSeconds = 3f;
        public const float CaptureSeconds = 1f;
        private const float MinSampleRatio = 0.7f;

        private sealed class Target
        {
            public TrackerRuntime Tracker;
            public readonly PoseAccumulator Samples = new PoseAccumulator();
        }

        private readonly TrackerService _tracker;
        private readonly ConfigStore _store;
        private readonly List<Target> _targets = new List<Target>();
        private readonly PoseAccumulator _hmd = new PoseAccumulator();
        private float _phaseStart;
        private int _frames;

        public CalibrationPhase Phase { get; private set; } = CalibrationPhase.Idle;
        public string ResultText { get; private set; } = string.Empty;
        public int Revision { get; private set; }

        public CalibrationService(TrackerService tracker, ConfigStore store)
        {
            _tracker = tracker;
            _store = store;
        }

        public float CountdownRemaining(float now) => Mathf.Max(0f, CountdownSeconds - (now - _phaseStart));
        public float CaptureProgress(float now) => Mathf.Clamp01((now - _phaseStart) / CaptureSeconds);

        /// <summary>Trackers that will be calibrated: connected, enabled and assigned to a role.</summary>
        public int CountTargets(out int valid)
        {
            int n = 0;
            valid = 0;
            foreach (TrackerRuntime t in _tracker.Trackers)
            {
                if (!t.Connected || !t.Enabled || t.Role == BodyRole.Unassigned) continue;
                n++;
                if (t.State == TrackingState.Valid) valid++;
            }
            return n;
        }

        public bool Begin(float now, out string reason)
        {
            reason = null;
            if (!_tracker.Session.IsConnected) { reason = "SteamVR is not connected."; return false; }
            if (CountTargets(out int valid) == 0) { reason = "No connected, enabled tracker has a body role. Assign roles first."; return false; }
            if (valid == 0) { reason = "None of the assigned trackers currently has a valid pose."; return false; }

            _targets.Clear();
            foreach (TrackerRuntime t in _tracker.Trackers)
                if (t.Connected && t.Enabled && t.Role != BodyRole.Unassigned)
                    _targets.Add(new Target { Tracker = t });

            SetPhase(CalibrationPhase.Countdown, now);
            ResultText = string.Empty;
            return true;
        }

        public void Cancel()
        {
            if (Phase == CalibrationPhase.Countdown || Phase == CalibrationPhase.Capturing)
            {
                SetPhase(CalibrationPhase.Idle, 0f);
                ResultText = "Calibration cancelled.";
            }
        }

        public void Acknowledge()
        {
            if (Phase == CalibrationPhase.Succeeded || Phase == CalibrationPhase.Failed) SetPhase(CalibrationPhase.Idle, 0f);
        }

        /// <summary>Called every frame after pose sampling.</summary>
        public void Update(float now, float rigYawOffsetIfAvailable, bool rigAvailable)
        {
            if (Phase == CalibrationPhase.Countdown && now - _phaseStart >= CountdownSeconds)
            {
                foreach (Target t in _targets) t.Samples.Clear();
                _hmd.Clear();
                _frames = 0;
                SetPhase(CalibrationPhase.Capturing, now);
            }

            if (Phase != CalibrationPhase.Capturing) return;

            _frames++;
            if (_tracker.HmdValid) _hmd.Add(_tracker.HmdTracking);
            foreach (Target t in _targets)
            {
                TrackerRuntime r = t.Tracker;
                if (!r.PoseValid) continue;
                // Offsets applied, smoothing NOT applied: the reference is the true current orientation.
                t.Samples.Add(PoseFilter.ApplyOffsets(r.RawTracking, r.Settings.PositionOffset, r.Settings.RotationOffset));
            }

            if (now - _phaseStart >= CaptureSeconds) Finish(rigYawOffsetIfAvailable, rigAvailable);
        }

        private void Finish(float rigYawOffset, bool rigAvailable)
        {
            var problems = new List<string>();
            int needed = Mathf.Max(1, Mathf.FloorToInt(_frames * MinSampleRatio));

            if (_hmd.Count < needed) problems.Add("headset tracking was lost during capture");
            else if (!_hmd.IsStable(out float hp, out float ha)) problems.Add($"head moved too much ({hp * 100f:F0} cm / {ha:F0}°)");

            foreach (Target t in _targets)
            {
                string name = BodyRoles.DisplayName(t.Tracker.Role) + " " + TrackerIdentity.ShortId(t.Tracker.Record.Serial);
                if (t.Samples.Count < needed) problems.Add($"{name} lost tracking");
                else if (!t.Samples.IsStable(out float p, out float a)) problems.Add($"{name} moved ({p * 100f:F0} cm / {a:F0}°)");
            }

            if (problems.Count > 0)
            {
                ResultText = "Calibration failed: " + string.Join("; ", problems) + ". Stand still and try again. Previous calibration kept.";
                ModLog.Warn(ResultText);
                SetPhase(CalibrationPhase.Failed, 0f);
                return;
            }

            RigidPose hmd = _hmd.Mean();
            string stamp = DateTime.UtcNow.ToString("o");
            var sb = new StringBuilder("Calibrated: ");
            foreach (Target t in _targets)
            {
                RigidPose mean = t.Samples.Mean();
                t.Tracker.Settings.Calibration = new TrackerCalibration
                {
                    MountRotation = CalibrationMath.ComputeMount(mean.Rotation, hmd.Rotation),
                    HeadRelativePosition = CalibrationMath.HeadRelativePosition(mean.Position, hmd),
                    RoleAtCalibration = t.Tracker.Role,
                    CapturedUtc = stamp
                };
                sb.Append(BodyRoles.DisplayName(t.Tracker.Role)).Append(", ");
            }
            sb.Length -= 2;

            CalibrationInfo info = _store.Data.Calibration;
            info.Valid = true;
            info.CapturedUtc = stamp;
            info.ForwardYawDegrees = hmd.Rotation.GetYawDegrees();
            info.TrackerCount = _targets.Count;
            if (rigAvailable) info.RigYawOffsetDegrees = rigYawOffset;
            _store.MarkDirty();

            ResultText = sb + ". Check marker alignment, then press Save calibration.";
            ModLog.Info($"Calibration captured for {_targets.Count} tracker(s); forward yaw {info.ForwardYawDegrees:F1}°, rig yaw offset {info.RigYawOffsetDegrees:F1}°.");
            SetPhase(CalibrationPhase.Succeeded, 0f);
        }

        /// <summary>Yaw-only recenter of every calibrated tracker (user faces forward, upright).</summary>
        public int Recenter(float rigYawOffset, bool rigAvailable, out string message)
        {
            if (!_tracker.HmdValid) { message = "Recenter failed: headset pose not valid."; return 0; }
            int n = 0;
            foreach (TrackerRuntime t in _tracker.Trackers)
            {
                TrackerCalibration c = t.Settings.Calibration;
                if (c == null || !t.PoseValid || !t.Enabled) continue;
                RigidPose corrected = PoseFilter.ApplyOffsets(t.RawTracking, t.Settings.PositionOffset, t.Settings.RotationOffset);
                c.MountRotation = CalibrationMath.RecenterMount(c.MountRotation, corrected.Rotation, _tracker.HmdTracking.Rotation);
                n++;
            }
            if (n > 0)
            {
                _store.Data.Calibration.ForwardYawDegrees = _tracker.HmdTracking.Rotation.GetYawDegrees();
                if (rigAvailable) _store.Data.Calibration.RigYawOffsetDegrees = rigYawOffset;
                _store.MarkDirty();
            }
            message = n > 0 ? $"Recentered {n} tracker(s) to your current facing. Save to keep it." : "Nothing to recenter: no calibrated tracker has a valid pose.";
            Revision++;
            return n;
        }

        public void ClearAll()
        {
            foreach (TrackerSettings s in _store.Data.Trackers) s.Calibration = null;
            foreach (TrackerRuntime t in _tracker.Trackers) t.Settings.Calibration = null;
            _store.Data.Calibration = new CalibrationInfo();
            _store.MarkDirty();
            Revision++;
        }

        private void SetPhase(CalibrationPhase phase, float now)
        {
            Phase = phase;
            _phaseStart = now;
            Revision++;
        }
    }
}
