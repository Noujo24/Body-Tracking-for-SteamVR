using System;
using System.Collections.Generic;

namespace GTSteamVRBodyTracking.Core
{
    /// <summary>Device class as reported by OpenVR (values mirror Valve.VR.ETrackedDeviceClass).</summary>
    public enum DeviceClassKind
    {
        Invalid = 0,
        Hmd = 1,
        Controller = 2,
        GenericTracker = 3,
        TrackingReference = 4,
        DisplayRedirect = 5
    }

    /// <summary>One device slot as observed during a discovery scan. Produced by the OpenVR backend.</summary>
    public struct ScanEntry
    {
        public uint Index;
        public DeviceClassKind Class;
        public bool Connected;
        public string Serial;
        public string Model;
        public string Manufacturer;
        public string ControllerType;
        public string TrackingSystem;
        /// <summary>0..1, or negative when the device does not report battery status.</summary>
        public float Battery;
    }

    /// <summary>A body tracker known to this session. Identity never depends on the device index.</summary>
    public sealed class TrackerRecord
    {
        public const uint NoIndex = uint.MaxValue;

        public string Key { get; internal set; }
        public bool HasStableId { get; internal set; }
        public string Serial { get; internal set; } = string.Empty;
        public string Model { get; internal set; } = string.Empty;
        public string Manufacturer { get; internal set; } = string.Empty;
        public string ControllerType { get; internal set; } = string.Empty;
        public string TrackingSystem { get; internal set; } = string.Empty;
        public float Battery { get; internal set; } = -1f;
        public uint DeviceIndex { get; internal set; } = NoIndex;
        public bool Connected { get; internal set; }
        public bool DuplicateSerial { get; internal set; }
        public int ConnectCount { get; internal set; }
        public long FirstSeenOrder { get; internal set; }

        public string DisplayName => string.IsNullOrEmpty(Model) ? "Tracker" : Model;

        public string DisplayId => HasStableId ? Serial : "(no serial, index " + DeviceIndex + ")";
    }

    public struct RegistryChanges
    {
        public int Added;
        public int Reconnected;
        public int Disconnected;
        public int Removed;
        public int IndexChanged;

        public bool ListChanged => Added + Removed > 0;
        public bool AnyChange => Added + Reconnected + Disconnected + Removed + IndexChanged > 0;
    }

    /// <summary>
    /// Reconciles OpenVR scan results into a stable list of body trackers.
    /// Rules (see Docs/GAME_INTEGRATION.md §9):
    ///  * only <see cref="DeviceClassKind.GenericTracker"/> counts as a body tracker;
    ///  * identity = serial number; the device index is only a transient lookup;
    ///  * devices without a reliable serial get a session-only key and are dropped on disconnect;
    ///  * two connected devices reporting the same serial are both flagged and neither is treated as stable.
    /// </summary>
    public sealed class DeviceRegistry
    {
        private readonly List<TrackerRecord> _trackers = new List<TrackerRecord>();
        private readonly Dictionary<string, TrackerRecord> _byKey = new Dictionary<string, TrackerRecord>(StringComparer.Ordinal);
        private readonly HashSet<TrackerRecord> _seen = new HashSet<TrackerRecord>();
        private readonly Dictionary<string, int> _serialCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private long _order;

        public IReadOnlyList<TrackerRecord> Trackers => _trackers;

        public int HmdCount { get; private set; }
        public int ControllerCount { get; private set; }
        public int BaseStationCount { get; private set; }
        public string HmdTrackingSystem { get; private set; } = string.Empty;
        public string HmdModel { get; private set; } = string.Empty;

        public int ConnectedTrackerCount
        {
            get
            {
                int n = 0;
                foreach (TrackerRecord t in _trackers) if (t.Connected) n++;
                return n;
            }
        }

        public TrackerRecord Find(string key) =>
            key != null && _byKey.TryGetValue(key, out TrackerRecord r) ? r : null;

        public RegistryChanges Reconcile(IList<ScanEntry> entries, int count)
        {
            var changes = new RegistryChanges();
            _seen.Clear();
            _serialCounts.Clear();
            HmdCount = ControllerCount = BaseStationCount = 0;

            // Pass 1: class statistics and duplicate-serial detection among connected trackers.
            for (int i = 0; i < count; i++)
            {
                ScanEntry e = entries[i];
                if (!e.Connected) continue;
                switch (e.Class)
                {
                    case DeviceClassKind.Hmd:
                        HmdCount++;
                        HmdTrackingSystem = e.TrackingSystem ?? string.Empty;
                        HmdModel = e.Model ?? string.Empty;
                        break;
                    case DeviceClassKind.Controller: ControllerCount++; break;
                    case DeviceClassKind.TrackingReference: BaseStationCount++; break;
                    case DeviceClassKind.GenericTracker:
                        string s = TrackerIdentity.Normalize(e.Serial);
                        if (TrackerIdentity.IsReliable(s))
                            _serialCounts[s] = _serialCounts.TryGetValue(s, out int c) ? c + 1 : 1;
                        break;
                }
            }

            // Pass 2: map connected trackers to records.
            for (int i = 0; i < count; i++)
            {
                ScanEntry e = entries[i];
                if (!e.Connected || e.Class != DeviceClassKind.GenericTracker) continue;

                string serial = TrackerIdentity.Normalize(e.Serial);
                bool duplicate = _serialCounts.TryGetValue(serial, out int n) && n > 1;
                bool stable = TrackerIdentity.IsReliable(serial) && !duplicate;
                string key = stable ? TrackerIdentity.StableKey(serial) : TrackerIdentity.SessionKey(e.Index);

                TrackerRecord rec = Find(key);
                if (rec == null)
                {
                    rec = new TrackerRecord { Key = key, HasStableId = stable, FirstSeenOrder = _order++ };
                    _byKey[key] = rec;
                    _trackers.Add(rec);
                    changes.Added++;
                }
                else if (!rec.Connected)
                {
                    changes.Reconnected++;
                }
                else if (rec.DeviceIndex != e.Index)
                {
                    changes.IndexChanged++;
                }

                if (!rec.Connected) rec.ConnectCount++;
                rec.Connected = true;
                rec.DeviceIndex = e.Index;
                rec.Serial = serial;
                rec.DuplicateSerial = duplicate;
                rec.Model = e.Model ?? string.Empty;
                rec.Manufacturer = e.Manufacturer ?? string.Empty;
                rec.ControllerType = e.ControllerType ?? string.Empty;
                rec.TrackingSystem = e.TrackingSystem ?? string.Empty;
                rec.Battery = e.Battery;
                _seen.Add(rec);
            }

            // Pass 3: everything not seen is disconnected; session-only records cannot be re-identified.
            for (int i = _trackers.Count - 1; i >= 0; i--)
            {
                TrackerRecord rec = _trackers[i];
                if (_seen.Contains(rec)) continue;

                if (!rec.HasStableId)
                {
                    _trackers.RemoveAt(i);
                    _byKey.Remove(rec.Key);
                    changes.Removed++;
                    continue;
                }

                if (rec.Connected)
                {
                    rec.Connected = false;
                    changes.Disconnected++;
                }
                rec.DeviceIndex = TrackerRecord.NoIndex;
            }

            return changes;
        }

        /// <summary>Marks every tracker disconnected (used when the OpenVR session is lost).</summary>
        public RegistryChanges MarkAllDisconnected()
        {
            HmdCount = ControllerCount = BaseStationCount = 0;
            return Reconcile(Array.Empty<ScanEntry>(), 0);
        }

        /// <summary>Removes a disconnected record (the "Forget" action). Connected devices cannot be forgotten.</summary>
        public bool Forget(string key)
        {
            TrackerRecord rec = Find(key);
            if (rec == null || rec.Connected) return false;
            _byKey.Remove(key);
            _trackers.Remove(rec);
            return true;
        }

        /// <summary>Adds a placeholder for a saved tracker that has not been seen yet this session.</summary>
        public TrackerRecord AddSavedPlaceholder(string serial, string model)
        {
            serial = TrackerIdentity.Normalize(serial);
            if (!TrackerIdentity.IsReliable(serial)) return null;
            string key = TrackerIdentity.StableKey(serial);
            TrackerRecord existing = Find(key);
            if (existing != null) return existing;
            var rec = new TrackerRecord
            {
                Key = key,
                HasStableId = true,
                Serial = serial,
                Model = model ?? string.Empty,
                FirstSeenOrder = _order++
            };
            _byKey[key] = rec;
            _trackers.Add(rec);
            return rec;
        }
    }
}
