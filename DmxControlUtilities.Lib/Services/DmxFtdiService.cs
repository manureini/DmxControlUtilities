using System.IO.Ports;

namespace DmxControlUtilities.Lib.Services
{
    /// <summary>
    /// Sends DMX512 data to an USB DMX dongle with an FTDI chip (Open DMX / Enttec compatible)
    /// using the FTDI virtual COM port driver.
    /// The universe is continuously refreshed in a background thread as required by DMX512.
    /// </summary>
    public class DmxFtdiService : IDisposable
    {
        private const int mChannelCount = 512;

        private readonly byte[] mUniverse = new byte[mChannelCount + 1]; // index 0 = start code
        private readonly object mLock = new();

        private SerialPort? mSerialPort;
        private Thread? mSendThread;
        private volatile bool mRunning;

        public bool IsOpen => mSerialPort?.IsOpen == true;

        /// <summary>
        /// Returns the available serial ports. An FTDI dongle usually shows up as COMx.
        /// </summary>
        public static string[] GetPortNames() => SerialPort.GetPortNames();

        public void Open(string portName)
        {
            if (IsOpen)
                Close();

            mSerialPort = new SerialPort(portName, 250000, Parity.None, 8, StopBits.Two)
            {
                ReadTimeout = 100,
                WriteTimeout = 100,
                Handshake = Handshake.None
            };

            mSerialPort.Open();

            mRunning = true;

            mSendThread = new Thread(SendLoop)
            {
                IsBackground = true,
                Name = "DmxFtdiSender",
                Priority = ThreadPriority.Highest
            };

            mSendThread.Start();
        }

        /// <summary>
        /// Sets a single DMX channel value. Channels are 1 based (1 - 512).
        /// </summary>
        public void SetChannel(int channel, byte value)
        {
            if (channel < 1 || channel > mChannelCount)
                throw new ArgumentOutOfRangeException(nameof(channel), "Channel must be between 1 and 512");

            lock (mLock)
            {
                mUniverse[channel] = value;
            }
        }

        public byte GetChannel(int channel)
        {
            if (channel < 1 || channel > mChannelCount)
                throw new ArgumentOutOfRangeException(nameof(channel), "Channel must be between 1 and 512");

            lock (mLock)
            {
                return mUniverse[channel];
            }
        }

        /// <summary>
        /// Sets the complete universe. The values array starts at channel 1 and may contain up to 512 entries.
        /// </summary>
        public void SetUniverse(ReadOnlySpan<byte> values)
        {
            if (values.Length > mChannelCount)
                throw new ArgumentOutOfRangeException(nameof(values), "A universe has a maximum of 512 channels");

            lock (mLock)
            {
                Array.Clear(mUniverse, 1, mChannelCount);
                values.CopyTo(mUniverse.AsSpan(1));
            }
        }

        public void Blackout()
        {
            lock (mLock)
            {
                Array.Clear(mUniverse, 1, mChannelCount);
            }
        }

        private void SendLoop()
        {
            var buffer = new byte[mUniverse.Length];

            while (mRunning)
            {
                try
                {
                    lock (mLock)
                    {
                        Array.Copy(mUniverse, buffer, buffer.Length);
                    }

                    SendFrame(buffer);
                }
                catch (Exception)
                {
                    if (!mRunning)
                        return;
                }

                Thread.Sleep(25); // ~40 Hz refresh rate
            }
        }

        private void SendFrame(byte[] frame)
        {
            var port = mSerialPort;

            if (port?.IsOpen != true)
                return;

            // Break (>= 88 µs) followed by Mark After Break (>= 8 µs)
            port.BreakState = true;
            Thread.SpinWait(200);
            port.BreakState = false;
            Thread.SpinWait(20);

            frame[0] = 0; // DMX start code
            port.Write(frame, 0, frame.Length);
        }

        public void Close()
        {
            mRunning = false;

            mSendThread?.Join(500);
            mSendThread = null;

            if (mSerialPort != null)
            {
                if (mSerialPort.IsOpen)
                    mSerialPort.Close();

                mSerialPort.Dispose();
                mSerialPort = null;
            }
        }

        public void Dispose()
        {
            Close();
            GC.SuppressFinalize(this);
        }
    }
}
