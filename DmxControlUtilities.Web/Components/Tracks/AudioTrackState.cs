namespace DmxControlUtilities.Web.Components.Tracks
{
    /// <summary>
    /// Holds the UI state of an audio track section (spectrogram images, zoom, playback position).
    /// </summary>
    public class AudioTrackState
    {
        /// <summary>
        /// One spectrogram image per audio file of the track.
        /// </summary>
        public List<string?> ImageDataUrls = new();

        /// <summary>
        /// Duration of each audio file of the track (same order as the files).
        /// </summary>
        public List<TimeSpan> FileDurations = new();

        public int BaseWidth;

        public double Zoom = 1;

        public double RenderZoom = 1;

        private TimeSpan _duration;

        /// <summary>
        /// The duration <see cref="BaseWidth"/> was calculated for. Used to keep a constant
        /// pixels per second scale when the track gets longer (e.g. a file is moved to the right).
        /// </summary>
        private TimeSpan _scaleDuration;

        public TimeSpan Duration
        {
            get => _duration;
            set
            {
                if (_duration == value)
                    return;

                _duration = value;

                if (_scaleDuration <= TimeSpan.Zero)
                    _scaleDuration = value;

                DurationChanged?.Invoke();
            }
        }

        public TimeSpan Position;

        /// <summary>
        /// Raised when the duration of the track changed (e.g. a longer audio file was loaded).
        /// </summary>
        public event Action? DurationChanged;

        /// <summary>
        /// Resets the time scale, so <see cref="BaseWidth"/> maps to the full current duration again.
        /// Has to be called when the images are (re)generated.
        /// </summary>
        public void ResetScale()
        {
            _scaleDuration = _duration;
        }

        /// <summary>
        /// Pixels per second of the time axis. Stays constant when the duration changes,
        /// so moving a file does not change the width of the spectrogram images.
        /// </summary>
        public double PixelsPerSecond => _scaleDuration > TimeSpan.Zero
            ? BaseWidth * Zoom / _scaleDuration.TotalSeconds
            : 0;

        public double DisplayWidth => _duration > TimeSpan.Zero && PixelsPerSecond > 0
            ? _duration.TotalSeconds * PixelsPerSecond
            : BaseWidth * Zoom;
    }
}
