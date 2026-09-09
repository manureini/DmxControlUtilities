using DmxControlUtilities.Lib.Models;
using System.ComponentModel.DataAnnotations;

namespace DmxControlUtilities.Lib.Services
{
    public class CuelistService
    {
        private readonly DeviceService mDeviceService;
        private readonly TimeProvider mTimeProvider;
        private readonly long mStartedAt;
        private readonly object mLock = new();
        private readonly Dictionary<Guid, Cuelist> mCuelists = new();
        private readonly Dictionary<Guid, CuelistPlayback> mPlaybacks = new();
        private long mActivationOrder;

        public CuelistService(DeviceService pDeviceService, TimeProvider? pTimeProvider = null)
        {
            mDeviceService = pDeviceService;
            mTimeProvider = pTimeProvider ?? TimeProvider.System;
            mStartedAt = mTimeProvider.GetTimestamp();
        }

        private TimeSpan Now => mTimeProvider.GetElapsedTime(mStartedAt);

        public IReadOnlyList<Cuelist> Cuelists
        {
            get
            {
                lock (mLock)
                {
                    return mCuelists.Values.Select(c => c.Clone()).ToList();
                }
            }
        }

        public Cuelist? GetCuelist(Guid pId)
        {
            lock (mLock)
            {
                return mCuelists.GetValueOrDefault(pId)?.Clone();
            }
        }

        public void AddCuelist(Cuelist pCuelist)
        {
            Validate(pCuelist);

            lock (mLock)
            {
                if (mCuelists.ContainsKey(pCuelist.Id))
                    throw new InvalidOperationException("A cuelist with this ID already exists.");

                mCuelists.Add(pCuelist.Id, pCuelist.Clone());
            }
        }

        public void SaveCuelist(Cuelist pCuelist)
        {
            Validate(pCuelist);

            lock (mLock)
            {
                if (!mCuelists.ContainsKey(pCuelist.Id))
                    throw new InvalidOperationException("Cuelist not found.");

                if (mPlaybacks.ContainsKey(pCuelist.Id))
                    throw new InvalidOperationException("Stop this cuelist before editing it.");

                mCuelists[pCuelist.Id] = pCuelist.Clone();
            }
        }

        public void RemoveCuelist(Guid pId)
        {
            lock (mLock)
            {
                UpdateCore(Now);
                mPlaybacks.Remove(pId);
                mCuelists.Remove(pId);
                RenderOutput();
            }
        }

        public Cue CaptureCue(IEnumerable<Guid> pDeviceIds, string pName)
        {
            var cue = new Cue { Name = pName };

            foreach (var id in pDeviceIds.Distinct())
            {
                var device = mDeviceService.GetDevice(id);
                var description = device != null ? mDeviceService.GetDescription(device) : null;
                if (device == null || description == null || description.Functions.Count == 0)
                    continue;

                var values = device.GetValuesSnapshot();
                cue.DeviceValues[id] = description.Functions.ToDictionary(f => f.Key,
                    f => values.GetValueOrDefault(f.Key, f.DefaultValue));
            }

            if (cue.DeviceValues.Count == 0)
                throw new InvalidOperationException("Select at least one configured device with a device description.");

            return cue;
        }

        public void Go(Guid pId)
        {
            lock (mLock)
            {
                if (!mCuelists.TryGetValue(pId, out var cuelist))
                    throw new InvalidOperationException("Cuelist not found.");

                if (cuelist.Cues.Count == 0)
                    return;

                var now = Now;
                UpdateCore(now);

                if (!mPlaybacks.TryGetValue(pId, out var playback))
                {
                    playback = new CuelistPlayback(cuelist.Clone(), mDeviceService);
                    mPlaybacks.Add(pId, playback);
                }

                playback.Go(now, ReadOutput, NextOrder);
                RenderOutput();
            }
        }

        public void Back(Guid pId)
        {
            lock (mLock)
            {
                var now = Now;
                UpdateCore(now);

                if (mPlaybacks.TryGetValue(pId, out var playback))
                    playback.Back(now, ReadOutput, NextOrder);

                RenderOutput();
            }
        }

        public void Pause(Guid pId)
        {
            lock (mLock)
            {
                var now = Now;
                UpdateCore(now);

                if (mPlaybacks.TryGetValue(pId, out var playback))
                    playback.Pause(now);

                RenderOutput();
            }
        }

        public void Resume(Guid pId)
        {
            lock (mLock)
            {
                var now = Now;
                UpdateCore(now);

                if (mPlaybacks.TryGetValue(pId, out var playback))
                    playback.Resume(now);

                RenderOutput();
            }
        }

        public void Stop(Guid pId)
        {
            lock (mLock)
            {
                UpdateCore(Now);
                mPlaybacks.Remove(pId);
                RenderOutput();
            }
        }

        public void StopAll()
        {
            lock (mLock)
            {
                mPlaybacks.Clear();
                RenderOutput();
            }
        }

        public void Update()
        {
            lock (mLock)
            {
                if (mPlaybacks.Count == 0)
                    return;

                UpdateCore(Now);
                RenderOutput();
            }
        }

        public CuelistPlaybackStatus GetStatus(Guid pId)
        {
            lock (mLock)
            {
                return mPlaybacks.TryGetValue(pId, out var playback)
                    ? playback.GetStatus(Now)
                    : new CuelistPlaybackStatus();
            }
        }

        private void UpdateCore(TimeSpan pNow)
        {
            foreach (var playback in mPlaybacks.Values.OrderBy(p => p.ActivatedAt).ThenBy(p => p.ActivationOrder).ToList())
                playback.Update(pNow, ReadOutput, NextOrder);
        }

        private long NextOrder() => ++mActivationOrder;

        private byte ReadOutput(Guid pDeviceId, string pKey)
        {
            byte value = mDeviceService.GetDevice(pDeviceId)?.GetValue(pKey) ?? 0;
            TimeSpan latest = TimeSpan.MinValue;
            long order = -1;

            foreach (var playback in mPlaybacks.Values)
            {
                if (playback.Values.TryGetValue(pDeviceId, out var values) && values.TryGetValue(pKey, out var candidate)
                    && (playback.ActivatedAt > latest || (playback.ActivatedAt == latest && playback.ActivationOrder > order)))
                {
                    value = candidate;
                    latest = playback.ActivatedAt;
                    order = playback.ActivationOrder;
                }
            }

            return value;
        }

        private void RenderOutput()
        {
            var output = new Dictionary<Guid, Dictionary<string, byte>>();

            foreach (var playback in mPlaybacks.Values.OrderBy(p => p.ActivatedAt).ThenBy(p => p.ActivationOrder))
            {
                foreach (var device in playback.Values)
                {
                    if (mDeviceService.GetDevice(device.Key) == null)
                        continue;

                    if (!output.TryGetValue(device.Key, out var values))
                        output[device.Key] = values = new Dictionary<string, byte>();

                    foreach (var value in device.Value)
                        values[value.Key] = value.Value;
                }
            }

            mDeviceService.SetPlaybackValues(output);
        }

        private static void Validate(Cuelist pCuelist)
        {
            ArgumentNullException.ThrowIfNull(pCuelist);
            Validator.ValidateObject(pCuelist, new ValidationContext(pCuelist), true);

            if (pCuelist.Id == Guid.Empty || pCuelist.Cues == null)
                throw new ValidationException("A cuelist requires an ID and a cue collection.");

            var ids = new HashSet<Guid>();
            foreach (var cue in pCuelist.Cues)
            {
                if (cue == null)
                    throw new ValidationException("A cuelist cannot contain a null cue.");

                Validator.ValidateObject(cue, new ValidationContext(cue), true);

                if (cue.Id == Guid.Empty || !ids.Add(cue.Id))
                    throw new ValidationException("Every cue must have a unique, nonempty ID.");

                if (!Enum.IsDefined(cue.Trigger))
                    throw new ValidationException("Unsupported cue trigger.");

                if (cue.DeviceValues == null || cue.DeviceValues.Any(d => d.Key == Guid.Empty || d.Value == null
                    || d.Value.Keys.Any(string.IsNullOrWhiteSpace)))
                    throw new ValidationException("Cue values must reference device IDs and function keys.");
            }
        }
    }
}
