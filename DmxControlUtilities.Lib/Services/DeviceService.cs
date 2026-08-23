using DmxControlUtilities.Lib.Models;
using DmxControlUtilities.Lib.Models.Ddf;

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
                device.Values[function.Key] = function.DefaultValue;
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
            var description = GetDescription(pDevice);

            if (description == null)
                return;

            foreach (var function in description.Functions)
            {
                int channel = pDevice.Channel + function.DmxChannel;

                if (channel < 1 || channel > 512)
                    continue;

                mDmxService.SetChannel(channel, pDevice.GetValue(function.Key));
            }
        }

        public void ApplyAll()
        {
            foreach (var device in Devices)
            {
                ApplyDevice(device);
            }
        }
    }
}
