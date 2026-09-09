namespace DmxControlUtilities.Lib.Models
{
    public enum CuelistPlaybackState
    {
        Stopped,
        Delaying,
        Fading,
        Holding,
        Paused
    }

    public sealed record CuelistPlaybackStatus
    {
        public CuelistPlaybackState State { get; init; }
        public Guid? CurrentCueId { get; init; }
        public int? CurrentCueIndex { get; init; }
        public string CurrentCueName { get; init; } = string.Empty;
        public int RemainingDelayMilliseconds { get; init; }
        public int RemainingFadeMilliseconds { get; init; }
        public int? NextCueMilliseconds { get; init; }
        public bool IsActive => State != CuelistPlaybackState.Stopped;
    }
}
