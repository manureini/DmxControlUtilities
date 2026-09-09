using DmxControlUtilities.Lib.Models;
using DmxControlUtilities.Lib.Services.Hal;
using System.ComponentModel.DataAnnotations;

namespace DmxControlUtilities.Lib.Services
{
    public class CuelistService
    {
        private readonly DeviceService mDeviceService;
        private readonly HalService mHalService;
        private readonly TimeProvider mTimeProvider;
        private readonly long mStartedAt;
        private readonly object mLock = new();
        private readonly Dictionary<Guid, Cuelist> mCuelists = new();
        private readonly Dictionary<Guid, CuelistPlayback> mPlaybacks = new();
        private long mActivationOrder;

        public CuelistService(DeviceService pDeviceService, HalService pHalService, TimeProvider? pTimeProvider = null)
        {
            mDeviceService = pDeviceService;
            mHalService = pHalService;
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

        public (Cuelist Cuelist, Cue Cue)? GetCue(Guid pCueId)
        {
            lock (mLock)
            {
                foreach (var list in mCuelists.Values)
                {
                    var cue = list.Cues.FirstOrDefault(c => c.Id == pCueId);
                    if (cue != null)
                        return (list.Clone(), cue.Clone());
                }
                return null;
            }
        }

        public void StoreProgrammerIntoCue(Guid pCuelistId, Guid? pCueId, string pCueName, Dictionary<Guid, Dictionary<string, CueFeatureValue>> pProgrammerValues)
        {
            lock (mLock)
            {
                if (!mCuelists.TryGetValue(pCuelistId, out var cuelist))
                    throw new InvalidOperationException("Cuelist not found.");

                if (mPlaybacks.ContainsKey(pCuelistId))
                    throw new InvalidOperationException("Stop this cuelist before editing it.");

                var updated = cuelist.Clone();

                Dictionary<string, CueFeatureValue> CopyFeatures(Dictionary<string, CueFeatureValue> pFeatures)
                {
                    return pFeatures.ToDictionary(f => f.Key, f => f.Value.Clone());
                }

                if (pCueId.HasValue)
                {
                    int index = updated.Cues.FindIndex(c => c.Id == pCueId.Value);
                    if (index < 0)
                        throw new InvalidOperationException("Cue not found in cuelist.");

                    var targetCue = updated.Cues[index];
                    targetCue.Name = pCueName;
                    targetCue.DeviceValues = pProgrammerValues.ToDictionary(d => d.Key, d => CopyFeatures(d.Value));
                }
                else
                {
                    var newCue = new Cue
                    {
                        Name = pCueName,
                        DeviceValues = pProgrammerValues.ToDictionary(d => d.Key, d => CopyFeatures(d.Value))
                    };
                    updated.Cues.Add(newCue);
                }

                Validate(updated);
                mCuelists[pCuelistId] = updated;
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
                var features = new Dictionary<string, CueFeatureValue>();

                foreach (var feature in mHalService.GetFeatures(device))
                {
                    var value = CueFeatureValue.FromDeviceValues(description, feature, values);

                    if (value != null)
                        features[feature.Feature] = value;
                }

                if (features.Count > 0)
                    cue.DeviceValues[id] = features;
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

        private CueFeatureValue? ReadOutput(Guid pDeviceId, string pFeature)
        {
            CueFeatureValue? value = null;
            TimeSpan latest = TimeSpan.MinValue;
            long order = -1;

            foreach (var playback in mPlaybacks.Values)
            {
                if (playback.Values.TryGetValue(pDeviceId, out var values) && values.TryGetValue(pFeature, out var candidate)
                    && (playback.ActivatedAt > latest || (playback.ActivatedAt == latest && playback.ActivationOrder > order)))
                {
                    value = candidate;
                    latest = playback.ActivatedAt;
                    order = playback.ActivationOrder;
                }
            }

            if (value != null)
                return value;

            // Fall back to the device's current base control values, read through the HAL feature.
            var device = mDeviceService.GetDevice(pDeviceId);
            var description = device != null ? mDeviceService.GetDescription(device) : null;

            if (device == null || description == null)
                return null;

            var halFeature = mHalService.GetFeatures(device).FirstOrDefault(f => f.Feature == pFeature);

            if (halFeature == null)
                return null;

            return CueFeatureValue.FromDeviceValues(description, halFeature, device.GetValuesSnapshot());
        }

        private void RenderOutput()
        {
            var output = new Dictionary<Guid, Dictionary<string, CueFeatureValue>>();

            foreach (var playback in mPlaybacks.Values.OrderBy(p => p.ActivatedAt).ThenBy(p => p.ActivationOrder))
            {
                foreach (var device in playback.Values)
                {
                    if (mDeviceService.GetDevice(device.Key) == null)
                        continue;

                    if (!output.TryGetValue(device.Key, out var values))
                        output[device.Key] = values = new Dictionary<string, CueFeatureValue>();

                    foreach (var value in device.Value)
                        values[value.Key] = value.Value;
                }
            }

            mDeviceService.SetPlaybackFeatures(output, mHalService);
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
                    throw new ValidationException("Cue values must reference device IDs and feature keys.");
            }
        }
    }
}
