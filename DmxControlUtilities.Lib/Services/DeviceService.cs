using DmxControlUtilities.Lib.Models;

namespace DmxControlUtilities.Lib.Services
{
    /// <summary>
    /// Holds the configured devices and writes their RGB values to the DMX universe.
    /// </summary>
    public class DeviceService
    {
        private readonly DmxFtdiService mDmxService;
        private readonly List<Device> mDevices = new();
        private readonly object mLock = new();

        public DeviceService(DmxFtdiService pDmxService)
        {
            mDmxService = pDmxService;
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

        public void RemoveDevice(Device pDevice)
        {
            lock (mLock)
            {
                mDevices.RemoveAll(d => d.Id == pDevice.Id);
            }
        }

        /// <summary>
        /// Writes the RGB values of the device to the DMX universe.
        /// </summary>
        public void ApplyDevice(Device pDevice)
        {
            if (pDevice.Channel < 1 || pDevice.Channel + 2 > 512)
                return;

            mDmxService.SetChannel(pDevice.Channel, pDevice.R);
            mDmxService.SetChannel(pDevice.Channel + 1, pDevice.G);
            mDmxService.SetChannel(pDevice.Channel + 2, pDevice.B);
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
