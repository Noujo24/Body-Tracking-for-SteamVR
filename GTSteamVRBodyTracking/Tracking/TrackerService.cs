using System;
using System.Collections.Generic;
using GTSteamVRBodyTracking.Core;
using GTSteamVRBodyTracking.Tools;
using UnityEngine;
using Valve.VR;

namespace GTSteamVRBodyTracking.Tracking
{
    /// <summary>Live state of one body tracker. Owned by <see cref="TrackerService"/>.</summary>
    internal sealed class TrackerRuntime
    {
        public TrackerRecord Record;
        /// <summary>Saved settings, or a session-only object when the device has no stable serial.</summary>
        public TrackerSettings Settings;
        public bool SettingsPersistent;

        public TrackingState State = TrackingState.Disconnected;
        public RigidPose RawTracking;
        public RigidPose FilteredTracking;
        public RigidPose World;
        public bool PoseValid;
        public bool WorldValid;
        public float LastValidTime = -1f;
        public int LastValidFrame = -1;
        public readonly PoseFilter Filter = new PoseFilter();

        public string Key => Record.Key;
        public bool Connected => Record.Connected;
        public BodyRole Role => Settings.Role;
        public bool Enabled => Settings.Enabled;
        public string Label => BodyRoles.DisplayName(Settings.Role);
    }

    /// <summary>
    /// SteamVR tracker discovery and pose sampling. Completely independent of the avatar and UI:
    /// it can run (and be tested) with avatar integration disabled.
    /// </summary>
    internal sealed class TrackerService
    {
        private readonly ConfigStore _store;
        private readonly TrackerScanner _scanner = new TrackerScanner();
        private readonly TrackedDevicePose_t[] _renderPoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private readonly TrackedDevicePose_t[] _gamePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private readonly List<TrackerRuntime> _trackers = new List<TrackerRuntime>();
        private readonly Dictionary<string, TrackerRuntime> _byKey = new Dictionary<string, TrackerRuntime>(StringComparer.Ordinal);

        private float _nextScan;
        private bool _refreshRequested;
        private int _sampledFrame = -1;
        private int _scanCount;
        private bool _compositorFailed;

        public OpenVRSession Session { get; }
        public DeviceRegistry Registry { get; } = new DeviceRegistry();
        public CoordinateMapper Mapper { get; } = new CoordinateMapper();
        public IReadOnlyList<TrackerRuntime> Trackers => _trackers;

        public float DiscoveryInterval { get; set; } = 1f;
        public RigidPose HmdTracking { get; private set; }
        public bool HmdValid { get; private set; }
        public string PoseSource { get; private set; } = "none";

        /// <summary>Changes when trackers are added/removed (rebuild lists).</summary>
        public int ListRevision { get; private set; }
        /// <summary>Changes when any connection/validity/identity status changes (refresh labels).</summary>
        public int StatusRevision { get; private set; }

        public int ValidCount { get; private set; }
        public float LastScanTime { get; private set; } = -1f;

        public TrackerService(ConfigStore store, bool allowStandalone, float now)
        {
            _store = store;
            Session = new OpenVRSession(allowStandalone, now);
            // Saved trackers are listed (as disconnected) until seen, so missing devices are visible.
            foreach (TrackerSettings s in store.Data.Trackers) Registry.AddSavedPlaceholder(s.Serial, s.LastKnownModel);
            SyncRuntimes();
        }

        public TrackerRuntime Find(string key) =>
            key != null && _byKey.TryGetValue(key, out TrackerRuntime r) ? r : null;

        public void RequestRefresh()
        {
            _refreshRequested = true;
            _nextScan = 0f;
            Mapper.ResetVerification();
        }

        /// <summary>Called after the config was reloaded or reset so runtimes point at the new settings objects.</summary>
        public void RebindSettings()
        {
            foreach (TrackerSettings s in _store.Data.Trackers) Registry.AddSavedPlaceholder(s.Serial, s.LastKnownModel);
            foreach (TrackerRuntime r in _trackers) BindSettings(r);
            SyncRuntimes();
            StatusRevision++;
        }

        public bool Forget(string key)
        {
            TrackerRuntime r = Find(key);
            if (r == null || r.Connected) return false;
            if (r.SettingsPersistent) _store.Remove(r.Record.Serial);
            Registry.Forget(key);
            SyncRuntimes();
            return true;
        }

        /// <summary>Discovery: session upkeep every frame (cheap), device scan on an interval.</summary>
        public void Tick(float now)
        {
            int sessionRevision = Session.Revision;
            bool wasConnected = Session.IsConnected;
            Session.Update(now);
            if (Session.Revision != sessionRevision) StatusRevision++;

            if (!Session.IsConnected)
            {
                if (wasConnected)
                {
                    _scanner.Invalidate();
                    ApplyChanges(Registry.MarkAllDisconnected());
                }
                return;
            }

            if (now < _nextScan) return;
            _nextScan = now + DiscoveryInterval;

            try
            {
                // Full string refresh on request and every 10th scan as a safety net.
                bool refresh = _refreshRequested || (_scanCount++ % 10 == 0);
                _refreshRequested = false;
                int count = _scanner.Scan(Session.System, refresh);
                RegistryChanges changes = Registry.Reconcile(_scanner.Entries, count);
                ApplyChanges(changes);
                LastScanTime = now;
            }
            catch (Exception e)
            {
                ModLog.Exception("scan", e, "Tracker discovery scan failed");
            }
        }

        private void ApplyChanges(RegistryChanges changes)
        {
            if (!changes.AnyChange) return;
            SyncRuntimes();
            StatusRevision++;
            if (changes.Added + changes.Reconnected + changes.Disconnected + changes.Removed > 0)
                ModLog.Info($"Trackers: +{changes.Added} added, {changes.Reconnected} reconnected, {changes.Disconnected} disconnected, {changes.Removed} removed. Connected now: {Registry.ConnectedTrackerCount}.");
        }

        /// <summary>Keeps the runtime list in the same order and membership as the registry (no stale or duplicate entries).</summary>
        private void SyncRuntimes()
        {
            bool membershipChanged = _trackers.Count != Registry.Trackers.Count;
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (TrackerRecord rec in Registry.Trackers) keep.Add(rec.Key);

            for (int i = _trackers.Count - 1; i >= 0; i--)
            {
                if (keep.Contains(_trackers[i].Key)) continue;
                _byKey.Remove(_trackers[i].Key);
                _trackers.RemoveAt(i);
                membershipChanged = true;
            }

            for (int i = 0; i < Registry.Trackers.Count; i++)
            {
                TrackerRecord rec = Registry.Trackers[i];
                if (!_byKey.TryGetValue(rec.Key, out TrackerRuntime rt))
                {
                    rt = new TrackerRuntime { Record = rec };
                    BindSettings(rt);
                    _byKey[rec.Key] = rt;
                    membershipChanged = true;
                }
                else if (rt.Settings == null || (rec.HasStableId && !rt.SettingsPersistent))
                {
                    BindSettings(rt);
                }

                if (i < _trackers.Count) _trackers[i] = rt;
                else _trackers.Add(rt);
            }
            if (_trackers.Count > Registry.Trackers.Count)
                _trackers.RemoveRange(Registry.Trackers.Count, _trackers.Count - Registry.Trackers.Count);

            if (membershipChanged) ListRevision++;
        }

        private void BindSettings(TrackerRuntime rt)
        {
            TrackerSettings saved = rt.Record.HasStableId ? _store.GetOrCreate(rt.Record.Serial, rt.Record.Model, markDirty: false) : null;
            if (saved != null)
            {
                rt.Settings = saved;
                rt.SettingsPersistent = true;
            }
            else if (rt.Settings == null || rt.SettingsPersistent)
            {
                rt.Settings = new TrackerSettings { Serial = rt.Record.Serial, LastKnownModel = rt.Record.Model };
                rt.SettingsPersistent = false;
            }
        }

        /// <summary>
        /// Samples all poses once per frame (guarded by frame count). Safe to call from LateUpdate and from
        /// the avatar patch; whichever comes first does the work.
        /// </summary>
        public void SampleIfNeeded()
        {
            int frame = Time.frameCount;
            if (frame == _sampledFrame) return;
            _sampledFrame = frame;

            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            int valid = 0;

            if (!Session.IsConnected || !TryReadPoses())
            {
                HmdValid = false;
                Mapper.Update(RigidPose.Identity, false, now);
                for (int i = 0; i < _trackers.Count; i++) Invalidate(_trackers[i], _trackers[i].Connected ? TrackingState.NoPose : TrackingState.Disconnected);
                ValidCount = 0;
                return;
            }

            // Headset pose from the same pose array → consistent anchor for this frame.
            TrackedDevicePose_t hmd = _renderPoses[OpenVR.k_unTrackedDeviceIndex_Hmd];
            HmdValid = false;
            if (PoseValidity.Evaluate(hmd.bDeviceIsConnected, hmd.bPoseIsValid, (int)hmd.eTrackingResult) == TrackingState.Valid
                && TryConvert(ref hmd.mDeviceToAbsoluteTracking, out RigidPose hmdPose))
            {
                HmdValid = true;
                HmdTracking = hmdPose;
            }
            Mapper.Update(HmdTracking, HmdValid, now);

            for (int i = 0; i < _trackers.Count; i++)
            {
                TrackerRuntime t = _trackers[i];
                TrackerRecord rec = t.Record;
                if (!rec.Connected || rec.DeviceIndex >= _renderPoses.Length)
                {
                    Invalidate(t, TrackingState.Disconnected);
                    continue;
                }

                TrackedDevicePose_t p = _renderPoses[rec.DeviceIndex];
                TrackingState state = PoseValidity.Evaluate(p.bDeviceIsConnected, p.bPoseIsValid, (int)p.eTrackingResult);
                if (state == TrackingState.Valid && !TryConvert(ref p.mDeviceToAbsoluteTracking, out t.RawTracking))
                    state = TrackingState.BadData;

                if (state != TrackingState.Valid)
                {
                    Invalidate(t, state);
                    continue;
                }

                TrackerSettings s = t.Settings;
                float gap = t.LastValidTime < 0f ? float.MaxValue : now - t.LastValidTime;
                t.FilteredTracking = t.Filter.Process(t.RawTracking, s.PositionOffset, s.RotationOffset, s.Smoothing, dt, gap);
                t.PoseValid = true;
                t.LastValidTime = now;
                t.LastValidFrame = frame;
                if (Mapper.Valid)
                {
                    t.World = Mapper.TrackingToWorld.Apply(t.FilteredTracking);
                    t.WorldValid = true;
                }
                else
                {
                    t.WorldValid = false;
                }
                SetState(t, TrackingState.Valid);
                valid++;
            }
            ValidCount = valid;
        }

        private bool TryReadPoses()
        {
            try
            {
                if (Session.State == SessionState.Shared && !_compositorFailed)
                {
                    CVRCompositor compositor = OpenVR.Compositor;
                    if (compositor != null)
                    {
                        EVRCompositorError err = compositor.GetLastPoses(_renderPoses, _gamePoses);
                        if (err == EVRCompositorError.None)
                        {
                            PoseSource = "compositor (render poses)";
                            return true;
                        }
                        ModLog.WarnThrottled("getlastposes", $"Compositor.GetLastPoses returned {err}; falling back to standing-universe poses.", 60f);
                    }
                }

                Session.System.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0f, _renderPoses);
                PoseSource = "system (standing)";
                return true;
            }
            catch (Exception e)
            {
                // A broken compositor path is not retried every frame.
                if (!_compositorFailed && Session.State == SessionState.Shared)
                {
                    _compositorFailed = true;
                    ModLog.Exception("poses-compositor", e, "Reading compositor poses failed; using IVRSystem poses from now on");
                    return TryReadPoses();
                }
                ModLog.Exception("poses", e, "Reading tracker poses failed");
                return false;
            }
        }

        private static bool TryConvert(ref HmdMatrix34_t m, out RigidPose pose) =>
            OpenVRMath.TryConvertMatrix(m.m0, m.m1, m.m2, m.m3, m.m4, m.m5, m.m6, m.m7, m.m8, m.m9, m.m10, m.m11, out pose);

        private void Invalidate(TrackerRuntime t, TrackingState state)
        {
            // Never keep or blend a stale pose: drop the filter history and mark everything invalid.
            t.PoseValid = false;
            t.WorldValid = false;
            t.Filter.Reset();
            SetState(t, state);
        }

        private void SetState(TrackerRuntime t, TrackingState state)
        {
            if (t.State == state) return;
            t.State = state;
            StatusRevision++;
        }

        public void Shutdown()
        {
            Session.Shutdown();
            for (int i = 0; i < _trackers.Count; i++) Invalidate(_trackers[i], TrackingState.Disconnected);
        }
    }
}
