using NAudio.Wave;

namespace DmxControlUtilities.Web.Services
{
    /// <summary>
    /// A <see cref="WaveStream"/> which plays multiple source streams sequentially as one continuous stream.
    /// All sources are decoded to a common format so seeking works across file boundaries.
    /// Ranges between files are silent when a source stream is shorter than expected.
    /// </summary>
    public class PlaylistWaveStream : WaveStream
    {
        private class Entry
        {
            public required WaveStream Reader;
            public long StartByte;
            public long LengthBytes;
        }

        private readonly List<Entry> mEntries;
        private readonly WaveFormat mWaveFormat;
        private readonly long mLength;
        private long mPosition;

        public override WaveFormat WaveFormat => mWaveFormat;

        public override long Length => mLength;

        public override long Position
        {
            get => mPosition;
            set
            {
                if (value < 0)
                    value = 0;

                if (value > mLength)
                    value = mLength;

                mPosition = value;

                foreach (var entry in mEntries)
                {
                    long local = value - entry.StartByte;
                    entry.Reader.Position = Math.Clamp(local, 0, entry.Reader.Length);
                }
            }
        }

        private PlaylistWaveStream(List<Entry> pEntries, WaveFormat pWaveFormat, long pLength)
        {
            mEntries = pEntries;
            mWaveFormat = pWaveFormat;
            mLength = pLength;
        }

        /// <summary>
        /// Creates a playlist stream from the given audio streams, played sequentially in order.
        /// Returns null when no stream could be decoded.
        /// </summary>
        public static PlaylistWaveStream? Create(IEnumerable<Stream> pStreams)
        {
            return Create(pStreams.Select(s => (s, TimeSpan.Zero)), sequential: true);
        }

        /// <summary>
        /// Creates a playlist stream from the given audio files. When sequential is true, the start
        /// offsets are ignored and each file starts after the previous one. Otherwise each file
        /// starts at its own offset and ranges between files are silent.
        /// Returns null when no stream could be decoded.
        /// </summary>
        public static PlaylistWaveStream? Create(IEnumerable<(Stream Stream, TimeSpan StartOffset)> pFiles, bool sequential = false)
        {
            var entries = new List<Entry>();
            WaveFormat? format = null;
            long position = 0;

            foreach (var (stream, startOffset) in pFiles)
            {
                WaveStream reader = CreateReader(stream);

                if (format == null)
                {
                    format = reader.WaveFormat;
                }
                else if (!reader.WaveFormat.Equals(format))
                {
                    reader = new WaveChannel32(reader);
                }

                long length = reader.Length;

                long start = sequential
                    ? position
                    : (long)(startOffset.TotalSeconds * format.SampleRate) * format.BlockAlign;

                // align to block size
                start -= start % format.BlockAlign;

                entries.Add(new Entry
                {
                    Reader = reader,
                    StartByte = start,
                    LengthBytes = length
                });

                position = Math.Max(position, start + length);
            }

            if (format == null)
                return null;

            return new PlaylistWaveStream(entries, format, position);
        }

        private static WaveStream CreateReader(Stream pStream)
        {
            if (pStream.CanSeek)
                pStream.Position = 0;

            return new StreamMediaFoundationReader(pStream);
        }

        public override int Read(byte[] pBuffer, int pOffset, int pCount)
        {
            int totalRead = 0;

            while (totalRead < pCount && mPosition < mLength)
            {
                var entry = mEntries.FirstOrDefault(e => mPosition >= e.StartByte && mPosition < e.StartByte + e.LengthBytes);

                if (entry == null)
                {
                    // gap between files: silence
                    long nextStart = mEntries.Where(e => e.StartByte > mPosition).Select(e => e.StartByte).DefaultIfEmpty(mLength).Min();
                    int silence = (int)Math.Min(nextStart - mPosition, pCount - totalRead);

                    Array.Clear(pBuffer, pOffset + totalRead, silence);

                    totalRead += silence;
                    mPosition += silence;
                    continue;
                }

                int toRead = (int)Math.Min(entry.LengthBytes - (mPosition - entry.StartByte), pCount - totalRead);

                // align to block size
                toRead -= toRead % mWaveFormat.BlockAlign;

                if (toRead <= 0)
                    break;

                int read = entry.Reader.Read(pBuffer, pOffset + totalRead, toRead);

                if (read == 0)
                {
                    // source stream ended early: treat the rest of the entry as silence
                    int remaining = (int)(entry.LengthBytes - (mPosition - entry.StartByte));

                    if (remaining > pCount - totalRead)
                        remaining = pCount - totalRead;

                    Array.Clear(pBuffer, pOffset + totalRead, remaining);

                    totalRead += remaining;
                    mPosition += remaining;
                    continue;
                }

                totalRead += read;
                mPosition += read;
            }

            return totalRead;
        }

        protected override void Dispose(bool pDisposing)
        {
            if (pDisposing)
            {
                foreach (var entry in mEntries)
                {
                    entry.Reader.Dispose();
                }
            }

            base.Dispose(pDisposing);
        }
    }
}
