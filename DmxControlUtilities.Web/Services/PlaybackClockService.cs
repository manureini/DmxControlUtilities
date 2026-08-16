using DmxControlUtilities.Lib.Models;
using System.Diagnostics;

namespace DmxControlUtilities.Web.Services
{
    /// <summary>
    /// The single source of truth for the position of a timecode show. The position advances
    /// from a monotonic clock while playing, independent of whether any audio is audible, so
    /// time-based output like light events keeps tracking even when all tracks are muted.
    /// Audio playback is synchronized to this clock.
    /// </summary>
    public class PlaybackClockService : IDisposable
    {
        private readonly object _lock = new();
        private readonly AudioPlaybackService _audio;

        // Monotonic clock used to advance the position while playing.
        private readonly Stopwatch _stopwatch = new();

        // The position the clock was at when it was last started or seeked.
        private TimeSpan _basePosition;

        private TimeSpan _duration = TimeSpan.FromMinutes(1);

        public PlaybackClockService(AudioPlaybackService pAudio)
        {
            _audio = pAudio;
        }

        /// <summary>
        /// The current position of the show timeline.
        /// </summary>
        public TimeSpan Position
        {
            get
            {
                lock (_lock)
                {
                    var position = _basePosition;

                    if (_stopwatch.IsRunning)
                        position += _stopwatch.Elapsed;

                    if (_duration > TimeSpan.Zero && position > _duration)
                        position = _duration;

                    return position;
                }
            }
        }

        public bool IsPlaying
        {
            get
            {
                lock (_lock)
                {
                    return _stopwatch.IsRunning;
                }
            }
        }

        /// <summary>
        /// The length of the show timeline. Set from the show content; the cursor and light
        /// events are scaled against this. Defaults to one minute when the show is empty.
        /// </summary>
        public TimeSpan Duration
        {
            get
            {
                lock (_lock)
                {
                    return _duration;
                }
            }
        }

        /// <summary>
        /// Loads the given audio tracks so they can be played in sync with this clock.
        /// Loading rebuilds the audio output, so when the clock is playing the new output is
        /// started at the current position to keep audio in sync.
        /// </summary>
        public void LoadTracks(IEnumerable<TimecodeAudioTrack> pTracks)
        {
            lock (_lock)
            {
                _audio.Load(pTracks);

                // Load rebuilds the audio output, which stops it. If the clock is playing,
                // restart the new output at the current position so audio keeps following.
                if (_stopwatch.IsRunning)
                {
                    _audio.Seek(_basePosition + _stopwatch.Elapsed);
                    _audio.Play();
                }
            }
        }

        /// <summary>
        /// Updates the show duration. The position is clamped to the new duration.
        /// </summary>
        public void SetDuration(TimeSpan pDuration)
        {
            lock (_lock)
            {
                _duration = pDuration < TimeSpan.Zero ? TimeSpan.Zero : pDuration;

                if (_basePosition > _duration)
                    _basePosition = _duration;
            }
        }

        /// <summary>
        /// Starts the clock at the current position and starts audio playback.
        /// </summary>
        public void Play()
        {
            lock (_lock)
            {
                if (_stopwatch.IsRunning)
                    return;

                // Start audio at the current position so it stays in sync with the clock.
                _audio.Seek(_basePosition);
                _audio.Play();

                _stopwatch.Restart();
            }
        }

        /// <summary>
        /// Pauses the clock and audio, keeping the current position.
        /// </summary>
        public void Pause()
        {
            lock (_lock)
            {
                CaptureElapsed();
                _audio.Pause();
            }
        }

        /// <summary>
        /// Stops the clock and audio and resets the position to the start.
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                _stopwatch.Reset();
                _basePosition = TimeSpan.Zero;
                _audio.Stop();
            }
        }

        /// <summary>
        /// Moves the position and synchronizes audio to it.
        /// </summary>
        public void Seek(TimeSpan pPosition)
        {
            lock (_lock)
            {
                if (pPosition < TimeSpan.Zero)
                    pPosition = TimeSpan.Zero;

                if (_duration > TimeSpan.Zero && pPosition > _duration)
                    pPosition = _duration;

                _basePosition = pPosition;

                if (_stopwatch.IsRunning)
                    _stopwatch.Restart();

                _audio.Seek(pPosition);
            }
        }

        // Adds the elapsed time of a running clock to the base position and stops the clock.
        // Must be called while holding the lock.
        private void CaptureElapsed()
        {
            if (!_stopwatch.IsRunning)
                return;

            _stopwatch.Stop();
            _basePosition += _stopwatch.Elapsed;

            if (_duration > TimeSpan.Zero && _basePosition > _duration)
                _basePosition = _duration;
        }

        public void Dispose()
        {
            // AudioPlaybackService is disposed by the container.
        }
    }
}
