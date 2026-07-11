using NAudio.CoreAudioApi;
using System;
using System.Timers;


namespace DmxControlUtilities.Web.Services
{
    public class AudioVolumeService : IDisposable
    {
        private float _currentVolume;

        private System.Timers.Timer _volumeMonitorTimer;

        public event EventHandler<VolumeChangedEventArgs> VolumeChanged;

        public float CurrentVolume
        {
            get => _currentVolume;
            private set
            {
                if (Math.Abs(_currentVolume - value) > 0.005f) // Avoid repeated events for minor float differences
                {
                    _currentVolume = value;
                    VolumeChanged?.Invoke(this, new VolumeChangedEventArgs { Volume = value });
                }
            }
        }

        public AudioVolumeService()
        {
            _currentVolume = GetPrimaryDeviceVolume();

            _volumeMonitorTimer = new System.Timers.Timer(1000);
            _volumeMonitorTimer.Elapsed += (s, e) => UpdateVolume();
            _volumeMonitorTimer.AutoReset = true;
            _volumeMonitorTimer.Start();
        }

        private void UpdateVolume()
        {
            try
            {
                CurrentVolume = GetPrimaryDeviceVolume();
            }
            catch
            {
                // Suppress exceptions during volume polling
            }
        }

        public void SetPrimaryDeviceVolume(float volume)
        {
            var deviceEnumerator = new MMDeviceEnumerator();
            var device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
        }

        public float GetPrimaryDeviceVolume()
        {
            var deviceEnumerator = new MMDeviceEnumerator();
            var device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            return device.AudioEndpointVolume.MasterVolumeLevelScalar;
        }

        public void Dispose()
        {
            _volumeMonitorTimer?.Stop();
            _volumeMonitorTimer?.Dispose();
        }
    }

    public class VolumeChangedEventArgs : EventArgs
    {
        public float Volume { get; set; }
    }
}
