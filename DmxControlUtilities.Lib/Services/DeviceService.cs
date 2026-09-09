using DmxControlUtilities.Lib.Models;
using DmxControlUtilities.Lib.Models.Ddf;
using DmxControlUtilities.Lib.Services.Hal;

namespace DmxControlUtilities.Lib.Services
{
    /// <summary>
    /// Holds the configured devices and writes their function values to the DMX universe.
    /// </summary>
    public class DeviceService
    {
        private readonly DmxFtdiService mDmxService;
        private readonly DeviceDescriptionService mDescriptionService;
        private readonly List<Device> mDevices = new();
        private readonly object mLock = new();
        private Dictionary<Guid, Dictionary<string, byte>> mPlaybackValues = new();
        private Dictionary<Guid, Dictionary<string, byte>> mProgrammerValues = new();

        public DeviceService(DmxFtdiService pDmxService, DeviceDescriptionService pDescriptionService)
        {
            mDmxService = pDmxService;
            mDescriptionService = pDescriptionService;
        }

        public IReadOnlyList<Device> Devices
        {
            get
            {
                lock (mLock)
                {
                    return mDevices.ToList();
                }
            }
        }

        public Device? GetDevice(Guid pId)
        {
            lock (mLock)
            {
                return mDevices.FirstOrDefault(d => d.Id == pId);
            }
        }

        public void AddDevice(Device pDevice)
        {
            lock (mLock)
            {
                mDevices.Add(pDevice);
            }

            ApplyDevice(pDevice);
        }

        /// <summary>
        /// Creates a device from the given description with all function default values applied.
        /// </summary>
        public Device CreateDevice(DeviceDescription pDescription, int pChannel)
        {
            var device = new Device
            {
                Name = pDescription.DisplayName,
                DescriptionId = pDescription.Id,
                Channel = pChannel,
            };

            foreach (var function in pDescription.Functions)
            {
                device.SetValue(function.Key, function.DefaultValue);
            }

            return device;
        }

        public DeviceDescription? GetDescription(Device pDevice)
        {
            return mDescriptionService.GetDescription(pDevice.DescriptionId);
        }

        public void RemoveDevice(Device pDevice)
        {
            lock (mLock)
            {
                mDevices.RemoveAll(d => d.Id == pDevice.Id);
            }
        }

        /// <summary>
        /// Writes the function values of the device to the DMX universe.
        /// </summary>
        public void ApplyDevice(Device pDevice)
        {
            lock (mLock)
            {
                ApplyDeviceCore(pDevice);
            }
        }

        private void ApplyDeviceCore(Device pDevice)
        {
            var description = GetDescription(pDevice);

            if (description == null)
                return;

            var controlValues = pDevice.GetValuesSnapshot();
            mPlaybackValues.TryGetValue(pDevice.Id, out var playbackValues);
            mProgrammerValues.TryGetValue(pDevice.Id, out var programmerValues);

            foreach (var function in description.Functions)
            {
                int channel = pDevice.Channel + function.DmxChannel;

                if (channel < 1 || channel > 512)
                    continue;

                byte value = controlValues.GetValueOrDefault(function.Key);

                if (playbackValues != null && playbackValues.TryGetValue(function.Key, out var playbackValue))
                    value = playbackValue;

                if (programmerValues != null && programmerValues.TryGetValue(function.Key, out var programmerValue))
                    value = programmerValue;

                mDmxService.SetChannel(channel, value);
            }
        }

        internal void SetPlaybackValues(IReadOnlyDictionary<Guid, Dictionary<string, byte>> pValues)
        {
            SetOutputLayer(pValues, ref mPlaybackValues);
        }

        internal void SetProgrammerValues(IReadOnlyDictionary<Guid, Dictionary<string, byte>> pValues)
        {
            SetOutputLayer(pValues, ref mProgrammerValues);
        }

        /// <summary>
        /// Replaces the cuelist playback layer with the channel rendering of the given typed HAL
        /// feature values. Each feature is applied through the HAL onto the device's current base
        /// control values, so composite features (color, 16-bit position) resolve to all of their
        /// DDF channels atomically.
        /// </summary>
        internal void SetPlaybackFeatures(IReadOnlyDictionary<Guid, Dictionary<string, CueFeatureValue>> pValues, HalService pHal)
        {
            SetOutputLayer(RenderFeatures(pValues, pHal), ref mPlaybackValues);
        }

        /// <summary>
        /// Replaces the programmer layer with the channel rendering of the given typed HAL feature
        /// values. See <see cref="SetPlaybackFeatures"/> for the rendering rules.
        /// </summary>
        internal void SetProgrammerFeatures(IReadOnlyDictionary<Guid, Dictionary<string, CueFeatureValue>> pValues, HalService pHal)
        {
            SetOutputLayer(RenderFeatures(pValues, pHal), ref mProgrammerValues);
        }

        private Dictionary<Guid, Dictionary<string, byte>> RenderFeatures(
            IReadOnlyDictionary<Guid, Dictionary<string, CueFeatureValue>> pValues, HalService pHal)
        {
            lock (mLock)
            {
                var rendered = new Dictionary<Guid, Dictionary<string, byte>>();

                foreach (var (deviceId, features) in pValues)
                {
                    var device = mDevices.FirstOrDefault(d => d.Id == deviceId);

                    if (device == null || features.Count == 0)
                        continue;

                    // Seed a temp device with the base control values so feature application
                    // (e.g. virtual dimmer scaling the current color) starts from the base state.
                    var temp = new Device
                    {
                        Id = device.Id,
                        Name = device.Name,
                        Channel = device.Channel,
                        DescriptionId = device.DescriptionId,
                    };

                    foreach (var (k, v) in device.GetValuesSnapshot())
                    {
                        temp.SetValue(k, v);
                    }

                    // Color before other features: virtual dimmer must scale the staged color.
                    foreach (var value in features.Values.OrderBy(v => v.Kind == CueFeatureValueKind.Color ? 0 : 1))
                    {
                        value.Apply(temp, pHal);
                    }

                    var baseSnapshot = device.GetValuesSnapshot();
                    var tempSnapshot = temp.GetValuesSnapshot();
                    var channels = new Dictionary<string, byte>();

                    foreach (var (k, v) in tempSnapshot)
                    {
                        if (v != baseSnapshot.GetValueOrDefault(k))
                            channels[k] = v;
                    }

                    if (channels.Count > 0)
                        rendered[deviceId] = channels;
                }

                return rendered;
            }
        }

        private void SetOutputLayer(IReadOnlyDictionary<Guid, Dictionary<string, byte>> pValues,
            ref Dictionary<Guid, Dictionary<string, byte>> pLayer)
        {
            lock (mLock)
            {
                var affectedDevices = pLayer.Keys.Union(pValues.Keys).ToHashSet();
                pLayer = pValues.ToDictionary(d => d.Key, d => new Dictionary<string, byte>(d.Value));

                foreach (var device in mDevices.Where(d => affectedDevices.Contains(d.Id)))
                {
                    ApplyDeviceCore(device);
                }
            }
        }

        public void ApplyAll()
        {
            lock (mLock)
            {
                foreach (var device in mDevices)
                {
                    ApplyDeviceCore(device);
                }
            }
        }
    }
}
