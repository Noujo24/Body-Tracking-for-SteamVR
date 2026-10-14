using System.Text;
using GTSteamVRBodyTracking.Core;
using Valve.VR;

namespace GTSteamVRBodyTracking.Tracking
{
    /// <summary>
    /// Reads device slots from IVRSystem into <see cref="ScanEntry"/> values (no events are polled,
    /// so the game's OpenVR event queue is never consumed). Identity strings are cached per index
    /// while the slot stays connected, and re-read on reconnect or on a forced refresh.
    /// </summary>
    internal sealed class TrackerScanner
    {
        private struct CachedInfo
        {
            public bool Valid;
            public DeviceClassKind Class;
            public string Serial;
            public string Model;
            public string Manufacturer;
            public string ControllerType;
            public string TrackingSystem;
            public bool ProvidesBattery;
        }

        private readonly ScanEntry[] _entries;
        private readonly CachedInfo[] _cache;
        private readonly StringBuilder _buffer = new StringBuilder((int)OpenVR.k_unMaxPropertyStringSize);

        public ScanEntry[] Entries => _entries;
        public int Count { get; private set; }

        public TrackerScanner()
        {
            _entries = new ScanEntry[OpenVR.k_unMaxTrackedDeviceCount];
            _cache = new CachedInfo[OpenVR.k_unMaxTrackedDeviceCount];
        }

        public void Invalidate()
        {
            for (int i = 0; i < _cache.Length; i++) _cache[i].Valid = false;
        }

        public int Scan(CVRSystem system, bool refreshStrings)
        {
            int count = 0;
            for (uint i = 0; i < _entries.Length; i++)
            {
                ETrackedDeviceClass cls = system.GetTrackedDeviceClass(i);
                bool connected = cls != ETrackedDeviceClass.Invalid && system.IsTrackedDeviceConnected(i);
                if (!connected)
                {
                    _cache[i].Valid = false;
                    continue;
                }

                DeviceClassKind kind = ToKind(cls);
                ref CachedInfo info = ref _cache[i];
                if (!info.Valid || info.Class != kind || refreshStrings)
                {
                    info.Valid = true;
                    info.Class = kind;
                    info.Serial = ReadString(system, i, ETrackedDeviceProperty.Prop_SerialNumber_String);
                    info.Model = ReadString(system, i, ETrackedDeviceProperty.Prop_ModelNumber_String);
                    info.Manufacturer = ReadString(system, i, ETrackedDeviceProperty.Prop_ManufacturerName_String);
                    info.ControllerType = ReadString(system, i, ETrackedDeviceProperty.Prop_ControllerType_String);
                    info.TrackingSystem = ReadString(system, i, ETrackedDeviceProperty.Prop_TrackingSystemName_String);
                    ETrackedPropertyError err = ETrackedPropertyError.TrackedProp_Success;
                    info.ProvidesBattery = system.GetBoolTrackedDeviceProperty(i, ETrackedDeviceProperty.Prop_DeviceProvidesBatteryStatus_Bool, ref err)
                                           && err == ETrackedPropertyError.TrackedProp_Success;
                }

                float battery = -1f;
                if (kind == DeviceClassKind.GenericTracker && info.ProvidesBattery)
                {
                    ETrackedPropertyError err = ETrackedPropertyError.TrackedProp_Success;
                    float b = system.GetFloatTrackedDeviceProperty(i, ETrackedDeviceProperty.Prop_DeviceBatteryPercentage_Float, ref err);
                    if (err == ETrackedPropertyError.TrackedProp_Success && b >= 0f && b <= 1f) battery = b;
                }

                _entries[count++] = new ScanEntry
                {
                    Index = i,
                    Class = kind,
                    Connected = true,
                    Serial = info.Serial,
                    Model = info.Model,
                    Manufacturer = info.Manufacturer,
                    ControllerType = info.ControllerType,
                    TrackingSystem = info.TrackingSystem,
                    Battery = battery
                };
            }
            Count = count;
            return count;
        }

        private string ReadString(CVRSystem system, uint index, ETrackedDeviceProperty prop)
        {
            _buffer.Clear();
            ETrackedPropertyError err = ETrackedPropertyError.TrackedProp_Success;
            system.GetStringTrackedDeviceProperty(index, prop, _buffer, (uint)_buffer.Capacity, ref err);
            return err == ETrackedPropertyError.TrackedProp_Success ? _buffer.ToString() : string.Empty;
        }

        private static DeviceClassKind ToKind(ETrackedDeviceClass cls)
        {
            switch (cls)
            {
                case ETrackedDeviceClass.HMD: return DeviceClassKind.Hmd;
                case ETrackedDeviceClass.Controller: return DeviceClassKind.Controller;
                case ETrackedDeviceClass.GenericTracker: return DeviceClassKind.GenericTracker;
                case ETrackedDeviceClass.TrackingReference: return DeviceClassKind.TrackingReference;
                case ETrackedDeviceClass.DisplayRedirect: return DeviceClassKind.DisplayRedirect;
                default: return DeviceClassKind.Invalid;
            }
        }
    }
}
