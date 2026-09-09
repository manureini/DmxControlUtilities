using DmxControlUtilities.Lib.Models;

namespace DmxControlUtilities.Lib.Services
{
    /// <summary>
    /// Runtime playback state of a single cuelist. Fades interpolate typed HAL feature values
    /// (color crossfade in RGB, pan/tilt lerp, scalar lerp); discrete (step) features snap to
    /// the target when the fade completes.
    /// </summary>
    internal sealed class CuelistPlayback
    {
        private readonly Cuelist mCuelist;
        private readonly DeviceService mDeviceService;
        private readonly List<FeatureFade> mFades = new();
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

        /// <summary>
        /// Current output of this playback: device id -> (feature key -> typed value).
        /// </summary>
        public Dictionary<Guid, Dictionary<string, CueFeatureValue>> Values { get; private set; } = new();

        public bool IsPaused => mPausedAt.HasValue;

        private Cue CurrentCue => mCuelist.Cues[CurrentCueIndex];
        private TimeSpan FadeStartsAt => mCueTriggeredAt + TimeSpan.FromMilliseconds(CurrentCue.DelayMilliseconds);
        private TimeSpan FadeEndsAt => FadeStartsAt + TimeSpan.FromMilliseconds(CurrentCue.FadeMilliseconds);

        public void Go(TimeSpan pNow, Func<Guid, string, CueFeatureValue?> pReadOutput, Func<long> pNextOrder)
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

        public void Back(TimeSpan pNow, Func<Guid, string, CueFeatureValue?> pReadOutput, Func<long> pNextOrder)
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

        public void Update(TimeSpan pNow, Func<Guid, string, CueFeatureValue?> pReadOutput, Func<long> pNextOrder)
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

        private void UpdateValues(TimeSpan pNow, Func<Guid, string, CueFeatureValue?> pReadOutput, Func<long> pNextOrder)
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
                Values[fade.DeviceId][fade.Feature] = CueFeatureValue.Lerp(fade.From, fade.To, progress);
            }

            mFadeCompleted = progress >= 1;
        }

        private void BeginFade(Func<Guid, string, CueFeatureValue?> pReadOutput, Func<long> pNextOrder)
        {
            var targets = new Dictionary<Guid, Dictionary<string, CueFeatureValue>>();

            // Reconstruct tracking from the beginning so Back and looping release later-only values.
            foreach (var cue in mCuelist.Cues.Take(CurrentCueIndex + 1))
            {
                foreach (var device in cue.DeviceValues)
                {
                    if (!targets.TryGetValue(device.Key, out var values))
                        targets[device.Key] = values = new Dictionary<string, CueFeatureValue>();

                    foreach (var value in device.Value)
                        values[value.Key] = value.Value;
                }
            }

            // Fade start: the current output of the whole system for that feature (device controls,
            // other cuelists) or, when the feature is untouched there, the previous tracked target.
            var starts = new Dictionary<Guid, Dictionary<string, CueFeatureValue>>();

            foreach (var (deviceId, features) in targets)
            {
                var startsForDevice = new Dictionary<string, CueFeatureValue>();

                foreach (var (featureKey, target) in features)
                {
                    var from = pReadOutput(deviceId, featureKey) ?? target.Clone();
                    startsForDevice[featureKey] = from;

                    mFades.Add(new FeatureFade(deviceId, featureKey, from, target));
                }

                starts[deviceId] = startsForDevice;
            }

            Values = starts;
            ActivatedAt = FadeStartsAt;
            ActivationOrder = pNextOrder();
            mFadeStarted = true;
        }

        private sealed record FeatureFade(Guid DeviceId, string Feature, CueFeatureValue From, CueFeatureValue To);
    }
}
