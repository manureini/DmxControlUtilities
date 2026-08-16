namespace DmxControlUtilities.Web.Components.Tracks
{
    /// <summary>
    /// Shared display state for all tracks of a show (audio and light). Holds the zoom level
    /// and the timeline duration so every track uses the same pixels-per-second scale and
    /// aligns in a grid. Horizontal scrolling is synchronized separately via trackScroll.js.
    /// </summary>
    public class TrackDisplayState
    {
        private double _zoom = 1;
        private TimeSpan _duration = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Width in pixels the unzoomed timeline spans.
        /// </summary>
        public int BaseWidth { get; set; } = 2000;

        /// <summary>
        /// Shared zoom level of all tracks. 1 = fit the whole show into <see cref="BaseWidth"/>.
        /// </summary>
        public double Zoom
        {
            get => _zoom;
            set
            {
                value = Math.Clamp(value, 1, 50);

                if (Math.Abs(_zoom - value) < 0.0001)
                    return;

                _zoom = value;
                ZoomChanged?.Invoke();
            }
        }

        /// <summary>
        /// The length of the show timeline all tracks are scaled against.
        /// </summary>
        public TimeSpan Duration
        {
            get => _duration;
            set
            {
                if (_duration == value)
                    return;

                _duration = value;
                DurationChanged?.Invoke();
            }
        }

        /// <summary>
        /// Width of the whole (zoomed) timeline in pixels.
        /// </summary>
        public double DisplayWidth => BaseWidth * Zoom;

        /// <summary>
        /// Pixels per second of the time axis, shared by all tracks.
        /// </summary>
        public double PixelsPerSecond => _duration > TimeSpan.Zero
            ? DisplayWidth / _duration.TotalSeconds
            : 0;

        /// <summary>
        /// Raised when the zoom level changed.
        /// </summary>
        public event Action? ZoomChanged;

        /// <summary>
        /// Raised when the duration changed.
        /// </summary>
        public event Action? DurationChanged;
    }
}
