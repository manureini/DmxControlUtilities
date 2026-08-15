using NAudio.Wave;

namespace DmxControlUtilities.Web.Services
{
    public class AudioPlaybackService : IDisposable
    {
        private readonly object _lock = new();

        private AudioFileReader? _reader;
        private WaveOutEvent? _output;

        public TimeSpan Duration
        {
            get
            {
                lock (_lock)
                {
                    return _reader?.TotalTime ?? TimeSpan.Zero;
                }
            }
        }

        public TimeSpan Position
        {
            get
            {
                lock (_lock)
                {
                    return _reader?.CurrentTime ?? TimeSpan.Zero;
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

        public void Load(string pFilePath)
        {
            lock (_lock)
            {
                DisposePlayback();

                _reader = new AudioFileReader(pFilePath);
                _output = new WaveOutEvent();
                _output.Init(_reader);
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

                if (_reader != null)
                {
                    _reader.CurrentTime = TimeSpan.Zero;
                }
            }
        }

        public void Seek(TimeSpan pPosition)
        {
            lock (_lock)
            {
                if (_reader == null)
                    return;

                if (pPosition < TimeSpan.Zero)
                    pPosition = TimeSpan.Zero;

                if (pPosition > _reader.TotalTime)
                    pPosition = _reader.TotalTime;

                _reader.CurrentTime = pPosition;
            }
        }

        private void DisposePlayback()
        {
            _output?.Stop();
            _output?.Dispose();
            _output = null;

            _reader?.Dispose();
            _reader = null;
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
