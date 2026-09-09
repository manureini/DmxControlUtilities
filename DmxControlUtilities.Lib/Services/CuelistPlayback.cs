using DmxControlUtilities.Lib.Models;

namespace DmxControlUtilities.Lib.Services
{
    internal sealed class CuelistPlayback
    {
        private static readonly string[] mResolutionSuffixes = { "", "/fine", "/ultra", "/ultrafine" };
        private readonly Cuelist mCuelist;
        private readonly DeviceService mDeviceService;
        private readonly List<ChannelFade> mFades = new();
        private TimeSpan mCueTriggeredAt;
        private TimeSpan? mPausedAt;
        private bool mFadeStarted;
        private bool mFadeCompleted;

        public CuelistPlayback(Cuelist pCuelist, DeviceService pDeviceService)
        {
            mCuelist = pCuelist;
            mDeviceService = pDeviceService;
        }

        public int CurrentCueIndex { get; private set; } = -1;
        public TimeSpan ActivatedAt { get; private set; }
        public long ActivationOrder { get; private set; }
        public Dictionary<Guid, Dictionary<string, byte>> Values { get; private set; } = new();
        public bool IsPaused => mPausedAt.HasValue;

        private Cue CurrentCue => mCuelist.Cues[CurrentCueIndex];
        private TimeSpan FadeStartsAt => mCueTriggeredAt + TimeSpan.FromMilliseconds(CurrentCue.DelayMilliseconds);
        private TimeSpan FadeEndsAt => FadeStartsAt + TimeSpan.FromMilliseconds(CurrentCue.FadeMilliseconds);

        public void Go(TimeSpan pNow, Func<Guid, string, byte> pReadOutput, Func<long> pNextOrder)
        {
            if (IsPaused)
            {
                Resume(pNow);
                return;
            }

            int index = GetNextIndex();
            if (index < 0)
                return;

            StartCue(index, pNow);
            UpdateValues(pNow, pReadOutput, pNextOrder);
        }

        public void Back(TimeSpan pNow, Func<Guid, string, byte> pReadOutput, Func<long> pNextOrder)
        {
            int index = CurrentCueIndex - 1;
            if (index < 0)
                index = mCuelist.Loop ? mCuelist.Cues.Count - 1 : 0;

            mPausedAt = null;
            StartCue(index, pNow);
            UpdateValues(pNow, pReadOutput, pNextOrder);
        }

        public void Pause(TimeSpan pNow)
        {
            mPausedAt ??= pNow;
        }

        public void Resume(TimeSpan pNow)
        {
            if (mPausedAt is not TimeSpan pausedAt)
                return;

            mCueTriggeredAt += pNow - pausedAt;
            mPausedAt = null;
        }

        public void Update(TimeSpan pNow, Func<Guid, string, byte> pReadOutput, Func<long> pNextOrder)
        {
            if (IsPaused || CurrentCueIndex < 0)
                return;

            // Bound catch-up to one traversal per frame, including all-zero automatic loops.
            for (int i = 0; i < mCuelist.Cues.Count; i++)
            {
                var nextAt = GetNextTriggerAt();
                if (nextAt == null || nextAt > pNow)
                    break;

                UpdateValues(nextAt.Value, pReadOutput, pNextOrder);
                bool immediate = nextAt <= mCueTriggeredAt;
                StartCue(GetNextIndex(), immediate ? pNow : nextAt.Value);

                if (immediate)
                    break;
            }

            UpdateValues(pNow, pReadOutput, pNextOrder);
        }

        public CuelistPlaybackStatus GetStatus(TimeSpan pNow)
        {
            if (CurrentCueIndex < 0)
                return new CuelistPlaybackStatus();

            var now = mPausedAt ?? pNow;
            var nextAt = GetNextTriggerAt();

            return new CuelistPlaybackStatus
            {
                State = IsPaused ? CuelistPlaybackState.Paused
                    : now < FadeStartsAt ? CuelistPlaybackState.Delaying
                    : now < FadeEndsAt ? CuelistPlaybackState.Fading
                    : CuelistPlaybackState.Holding,
                CurrentCueId = CurrentCue.Id,
                CurrentCueIndex = CurrentCueIndex,
                CurrentCueName = CurrentCue.Name,
                RemainingDelayMilliseconds = RemainingMilliseconds(FadeStartsAt - now),
                RemainingFadeMilliseconds = Math.Min(CurrentCue.FadeMilliseconds, RemainingMilliseconds(FadeEndsAt - now)),
                NextCueMilliseconds = nextAt.HasValue ? RemainingMilliseconds(nextAt.Value - now) : null
            };
        }

        private static int RemainingMilliseconds(TimeSpan pTime)
        {
            return (int)Math.Clamp(Math.Ceiling(pTime.TotalMilliseconds), 0, int.MaxValue);
        }

        private int GetNextIndex()
        {
            if (CurrentCueIndex + 1 < mCuelist.Cues.Count)
                return CurrentCueIndex + 1;

            return mCuelist.Loop && mCuelist.Cues.Count > 0 ? 0 : -1;
        }

        private TimeSpan? GetNextTriggerAt()
        {
            int index = GetNextIndex();
            if (index < 0 || mCuelist.Cues[index].Trigger == CueTrigger.Manual)
                return null;

            var next = mCuelist.Cues[index];
            var origin = next.Trigger == CueTrigger.Follow ? FadeEndsAt : mCueTriggeredAt;
            return origin + TimeSpan.FromMilliseconds(next.TriggerMilliseconds);
        }

        private void StartCue(int pIndex, TimeSpan pNow)
        {
            CurrentCueIndex = pIndex;
            mCueTriggeredAt = pNow;
            mFadeStarted = false;
            mFadeCompleted = false;
            mFades.Clear();
        }

        private void UpdateValues(TimeSpan pNow, Func<Guid, string, byte> pReadOutput, Func<long> pNextOrder)
        {
            if (pNow < FadeStartsAt || mFadeCompleted)
                return;

            if (!mFadeStarted)
                BeginFade(pReadOutput, pNextOrder);

            double progress = CurrentCue.FadeMilliseconds == 0
                ? 1
                : Math.Clamp((pNow - FadeStartsAt).TotalMilliseconds / CurrentCue.FadeMilliseconds, 0, 1);

            foreach (var fade in mFades)
            {
                long value = fade.IsDiscrete && progress < 1
                    ? fade.From
                    : (long)Math.Round(fade.From + (fade.To - fade.From) * progress, MidpointRounding.AwayFromZero);

                for (int i = fade.Keys.Length - 1; i >= 0; i--)
                {
                    if (fade.Keys[i] is string key)
                        Values[fade.DeviceId][key] = (byte)(value & 255);

                    value >>= 8;
                }
            }

            mFadeCompleted = progress >= 1;
        }

        private void BeginFade(Func<Guid, string, byte> pReadOutput, Func<long> pNextOrder)
        {
            var targets = new Dictionary<Guid, Dictionary<string, byte>>();

            // Reconstruct tracking from the beginning so Back and looping release later-only values.
            foreach (var cue in mCuelist.Cues.Take(CurrentCueIndex + 1))
            {
                foreach (var device in cue.DeviceValues)
                {
                    if (!targets.TryGetValue(device.Key, out var values))
                        targets[device.Key] = values = new Dictionary<string, byte>();

                    foreach (var value in device.Value)
                        values[value.Key] = value.Value;
                }
            }

            var starts = targets.ToDictionary(d => d.Key,
                d => d.Value.Keys.ToDictionary(k => k, k => pReadOutput(d.Key, k)));

            foreach (var device in targets)
            {
                var configuredDevice = mDeviceService.GetDevice(device.Key);
                var description = configuredDevice != null ? mDeviceService.GetDescription(configuredDevice) : null;

                foreach (var target in device.Value)
                {
                    if (mResolutionSuffixes.Skip(1).Any(s => target.Key.EndsWith(s, StringComparison.Ordinal)
                        && device.Value.ContainsKey(target.Key[..^s.Length])))
                        continue;

                    int resolution = Array.FindLastIndex(mResolutionSuffixes, s => device.Value.ContainsKey(target.Key + s));
                    var keys = new string?[resolution + 1];
                    long from = 0;
                    long to = 0;

                    // Fade a 16/24/32-bit function as one number, not as independent DMX bytes.
                    for (int i = 0; i <= resolution; i++)
                    {
                        string key = target.Key + mResolutionSuffixes[i];
                        keys[i] = device.Value.ContainsKey(key) ? key : null;
                        from = (from << 8) | starts[device.Key].GetValueOrDefault(key);
                        to = (to << 8) | device.Value.GetValueOrDefault(key);
                    }

                    mFades.Add(new ChannelFade(device.Key, keys, from, to,
                        description?.GetFunction(target.Key)?.Steps.Count > 0));
                }
            }

            Values = starts;
            ActivatedAt = FadeStartsAt;
            ActivationOrder = pNextOrder();
            mFadeStarted = true;
        }

        private sealed record ChannelFade(Guid DeviceId, string?[] Keys, long From, long To, bool IsDiscrete);
    }
}
