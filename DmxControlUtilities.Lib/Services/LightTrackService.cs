using DmxControlUtilities.Lib.Models;

namespace DmxControlUtilities.Lib.Services
{
    /// <summary>
    /// Applies the light events of a timecode show to the devices depending on the playback position.
    /// </summary>
    public class LightTrackService
    {
        private readonly DeviceService mDeviceService;
        private readonly object mLock = new();

        private TimecodeShow? mShow;
        private TimeSpan mLastPosition = TimeSpan.MinValue;

        public LightTrackService(DeviceService pDeviceService)
        {
            mDeviceService = pDeviceService;
        }

        public TimecodeShow? Show
        {
            get
            {
                lock (mLock)
                {
                    return mShow;
                }
            }
        }

        public void SetShow(TimecodeShow? pShow)
        {
            lock (mLock)
            {
                mShow = pShow;
                mLastPosition = TimeSpan.MinValue;
            }
        }

        public IReadOnlyList<LightEvent> Events
        {
            get
            {
                lock (mLock)
                {
                    return mShow?.AllEvents.OrderBy(e => e.Time).ToList() ?? new List<LightEvent>();
                }
            }
        }

        /// <summary>
        /// Adds an event to the given light event track. When no track is given, the first track is used or a new one is created.
        /// </summary>
        public void AddEvent(LightEvent pEvent, LightEventTrack? pTrack = null)
        {
            lock (mLock)
            {
                if (mShow == null)
                    return;

                var track = pTrack ?? mShow.LightEventTracks.FirstOrDefault();

                if (track == null)
                {
                    track = new LightEventTrack();
                    mShow.Tracks.Add(track);
                }

                track.Events.Add(pEvent);
            }
        }

        public void RemoveEvent(LightEvent pEvent)
        {
            lock (mLock)
            {
                if (mShow == null)
                    return;

                foreach (var track in mShow.LightEventTracks)
                {
                    track.Events.RemoveAll(e => e.Id == pEvent.Id);
                }
            }
        }

        public void Clear()
        {
            lock (mLock)
            {
                if (mShow != null)
                {
                    foreach (var track in mShow.LightEventTracks)
                    {
                        track.Events.Clear();
                    }
                }
            }

            Reset();
        }

        /// <summary>
        /// Resets the internal playback state, so all events get applied again on the next update.
        /// </summary>
        public void Reset()
        {
            lock (mLock)
            {
                mLastPosition = TimeSpan.MinValue;
            }
        }

        /// <summary>
        /// Applies all events which are between the last and the current playback position.
        /// When the position jumped (seek), the last event before the position is applied per device.
        /// </summary>
        public void UpdatePosition(TimeSpan pPosition)
        {
            List<LightEvent> toApply;

            lock (mLock)
            {
                if (mShow == null)
                    return;

                var lastPosition = mLastPosition;
                mLastPosition = pPosition;

                bool isContinuous = lastPosition != TimeSpan.MinValue
                    && pPosition >= lastPosition
                    && (pPosition - lastPosition) < TimeSpan.FromSeconds(1);

                if (isContinuous)
                {
                    toApply = mShow.AllEvents
                        .Where(e => e.Time > lastPosition && e.Time <= pPosition)
                        .OrderBy(e => e.Time)
                        .ToList();
                }
                else
                {
                    toApply = mShow.AllEvents
                        .Where(e => e.Time <= pPosition)
                        .GroupBy(e => e.DeviceId)
                        .Select(g => g.OrderBy(e => e.Time).Last())
                        .ToList();
                }
            }

            foreach (var lightEvent in toApply)
            {
                ApplyEvent(lightEvent);
            }
        }

        public void ApplyEvent(LightEvent pEvent)
        {
            var device = mDeviceService.GetDevice(pEvent.DeviceId);

            if (device == null)
                return;

            device.R = pEvent.R;
            device.G = pEvent.G;
            device.B = pEvent.B;

            mDeviceService.ApplyDevice(device);
        }
    }
}
