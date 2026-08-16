using DmxControlUtilities.Lib.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DmxControlUtilities.Web.Services
{
    public class AudioPlaybackService : IDisposable
    {
        private readonly object _lock = new();

        private MixingSampleProvider? _mixer;
        private WaveOutEvent? _output;

        // The source streams currently loaded into the mixer, kept so they can be
        // sought and disposed. Each entry maps a sample-provider input to its source stream.
        private readonly List<(ISampleProvider Provider, WaveStream Source)> _inputs = new();

        // WaveOutEvent.GetPosition() only counts bytes played since Init and is not affected by
        // repositioning the source streams. These track the last seek so Position stays correct.
        private TimeSpan _seekPosition;
        private long _outputBytesAtSeek;

        /// <summary>
        /// True when an output device has been initialized with loaded sources.
        /// While false, <see cref="Position"/> has no meaningful value.
        /// </summary>
        public bool IsLoaded
        {
            get
            {
                lock (_lock)
                {
                    return _output != null;
                }
            }
        }

        /// <summary>
        /// The total duration of the loaded sources (the longest track).
        /// </summary>
        public TimeSpan Duration
        {
            get
            {
                lock (_lock)
                {
                    return _inputs.Count == 0
                        ? TimeSpan.Zero
                        : _inputs.Max(i => i.Source.TotalTime);
                }
            }
        }

        /// <summary>
        /// The current playback position, taken from the output device so it stays
        /// accurate while mixing multiple tracks. Offset by the last seek, because the
        /// output device keeps counting from where it was initialized.
        /// </summary>
        public TimeSpan Position
        {
            get
            {
                lock (_lock)
                {
                    if (_output == null || _mixer == null)
                        return _seekPosition;

                    long bytes = _output.GetPosition() - _outputBytesAtSeek;

                    if (bytes < 0)
                        bytes = 0;

                    long frames = bytes / _mixer.WaveFormat.BlockAlign;
                    var elapsed = TimeSpan.FromSeconds((double)frames / _mixer.WaveFormat.SampleRate);

                    return _seekPosition + elapsed;
                }
            }
        }

        public bool IsPlaying
        {
            get
            {
                lock (_lock)
                {
                    return _output?.PlaybackState == PlaybackState.Playing;
                }
            }
        }

        /// <summary>
        /// Loads all given audio tracks and mixes them so they play together at the same position.
        /// Each track's files play at their start offsets. All tracks are audible simultaneously.
        /// </summary>
        public void Load(IEnumerable<TimecodeAudioTrack> pTracks)
        {
            var playlists = new List<PlaylistWaveStream>();

            foreach (var track in pTracks)
            {
                if (track.Muted)
                    continue;

                var files = track.AudioFiles
                    .Where(f => f.File != null)
                    .Select(f => (f.File!, f.StartOffset))
                    .ToList();

                if (files.Count == 0)
                    continue;

                var playlist = PlaylistWaveStream.Create(files);

                if (playlist != null)
                    playlists.Add(playlist);
            }

            lock (_lock)
            {
                DisposePlayback();

                // Mix at 48 kHz; resample any playlist that doesn't match so tracks
                // recorded at different sample rates can play together.
                var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
                {
                    ReadFully = true
                };

                foreach (var playlist in playlists)
                {
                    playlist.Position = 0;
                    ISampleProvider provider = playlist.ToSampleProvider();

                    if (provider.WaveFormat.SampleRate != mixer.WaveFormat.SampleRate)
                        provider = new WdlResamplingSampleProvider(provider, mixer.WaveFormat.SampleRate);

                    mixer.AddMixerInput(provider);
                    _inputs.Add((provider, playlist));
                }

                // Even with no audible inputs (all tracks muted) keep an output running so
                // Position advances and time-based output like light events keeps tracking.
                _mixer = mixer;
                _output = new WaveOutEvent();
                _output.Init(_mixer);
            }
        }

        /// <summary>
        /// Loads a single audio track for playback.
        /// </summary>
        public void Load(TimecodeAudioTrack pTrack)
        {
            Load(new[] { pTrack });
        }

        /// <summary>
        /// Loads audio from a stream for playback using Media Foundation. The stream is not
        /// disposed by this service and must remain open and readable for the lifetime of playback.
        /// </summary>
        public void Load(Stream pAudioStream)
        {
            if (pAudioStream.CanSeek)
                pAudioStream.Position = 0;

            lock (_lock)
            {
                DisposePlayback();

                var reader = new StreamMediaFoundationReader(pAudioStream);

                _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
                {
                    ReadFully = true
                };

                ISampleProvider provider = reader.ToSampleProvider();

                if (provider.WaveFormat.SampleRate != _mixer.WaveFormat.SampleRate)
                    provider = new WdlResamplingSampleProvider(provider, _mixer.WaveFormat.SampleRate);

                _mixer.AddMixerInput(provider);
                _inputs.Add((provider, reader));

                _output = new WaveOutEvent();
                _output.Init(_mixer);
            }
        }

        public void Play()
        {
            lock (_lock)
            {
                _output?.Play();
            }
        }

        public void Pause()
        {
            lock (_lock)
            {
                _output?.Pause();
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _output?.Stop();
                Seek(TimeSpan.Zero);
            }
        }

        public void Seek(TimeSpan pPosition)
        {
            lock (_lock)
            {
                if (pPosition < TimeSpan.Zero)
                    pPosition = TimeSpan.Zero;

                // Remember where we sought to and the device counter at that moment, so
                // Position keeps reporting the seeked position plus what played since.
                _seekPosition = pPosition;
                _outputBytesAtSeek = _output?.GetPosition() ?? 0;

                foreach (var (_, source) in _inputs)
                {
                    long target = (long)(pPosition.TotalSeconds * source.WaveFormat.SampleRate) * source.WaveFormat.BlockAlign;
                    target -= target % source.WaveFormat.BlockAlign;
                    source.Position = Math.Clamp(target, 0, source.Length);
                }

                // A playlist that reached its end was removed from the mixer, because
                // MixingSampleProvider drops inputs which return 0 samples. After seeking
                // back those inputs must be attached again or the track stays silent.
                RestoreMixerInputs();
            }
        }

        /// <summary>
        /// Re-adds inputs the mixer dropped when their source ran out of data.
        /// Must be called while holding the lock.
        /// </summary>
        private void RestoreMixerInputs()
        {
            if (_mixer == null)
                return;

            var active = _mixer.MixerInputs.ToHashSet();

            foreach (var (provider, _) in _inputs)
            {
                if (!active.Contains(provider))
                    _mixer.AddMixerInput(provider);
            }
        }

        private void DisposePlayback()
        {
            _output?.Stop();
            _output?.Dispose();
            _output = null;

            foreach (var (_, source) in _inputs)
            {
                source.Dispose();
            }

            _inputs.Clear();

            _mixer?.RemoveAllMixerInputs();
            _mixer = null;

            // A new output device starts counting at zero again.
            _seekPosition = TimeSpan.Zero;
            _outputBytesAtSeek = 0;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                DisposePlayback();
            }
        }
    }
}
