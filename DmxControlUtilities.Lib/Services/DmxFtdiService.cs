using System.Diagnostics;
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

        // DMX512 requires a Break of at least 88 µs and a Mark After Break of at least 8 µs.
        // Use generous margins so the signal is reliably detected even with OS jitter.
        private const int mBreakMicroseconds = 100;
        private const int mMarkAfterBreakMicroseconds = 10;

        // Target time per frame. A full 513 byte frame at 250 kBaud takes ~23 ms,
        // so ~30 ms (~33 Hz) leaves headroom for Break/MAB and OS jitter.
        private const int mFramePeriodMilliseconds = 30;

        // Delay between reconnect attempts after the dongle disappeared (e.g. USB unplug).
        private const int mReconnectDelayMilliseconds = 1000;

        private readonly byte[] mUniverse = new byte[mChannelCount + 1]; // index 0 = start code
        private readonly object mLock = new();
        private readonly object mPortLock = new();

        private SerialPort? mSerialPort;
        private Thread? mSendThread;
        private volatile bool mRunning;
        private string? mPortName;

        public bool IsOpen => mSerialPort?.IsOpen == true;

        /// <summary>
        /// Raised when the connection to the dongle is lost or re-established.
        /// The argument is true when connected. May be raised from the background send thread.
        /// </summary>
        public event Action<bool>? ConnectionChanged;

        /// <summary>
        /// Returns the available serial ports. An FTDI dongle usually shows up as COMx.
        /// </summary>
        public static string[] GetPortNames() => SerialPort.GetPortNames();

        public void Open(string portName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(portName);

            lock (mPortLock)
            {
                CloseCore();

                mPortName = portName;
                OpenPort();

                mRunning = true;

                mSendThread = new Thread(SendLoop)
                {
                    IsBackground = true,
                    Name = "DmxFtdiSender",
                    Priority = ThreadPriority.Highest
                };

                mSendThread.Start();
            }
        }

        private void OpenPort()
        {
            var port = new SerialPort(mPortName!, 250000, Parity.None, 8, StopBits.Two)
            {
                ReadTimeout = 100,
                WriteTimeout = 100,
                Handshake = Handshake.None
            };

            port.Open();
            mSerialPort = port;
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
            long framePeriodTicks = Stopwatch.Frequency * mFramePeriodMilliseconds / 1000;
            long nextFrame = Stopwatch.GetTimestamp();
            bool wasConnected = true;

            while (mRunning)
            {
                try
                {
                    lock (mLock)
                    {
                        Array.Copy(mUniverse, buffer, buffer.Length);
                    }

                    if (mSerialPort?.IsOpen != true)
                        throw new InvalidOperationException("The serial port is closed.");

                    SendFrame(buffer);

                    if (!wasConnected)
                    {
                        wasConnected = true;
                        RaiseConnectionChanged(true);
                    }
                }
                catch (Exception)
                {
                    if (!mRunning)
                        return;

                    if (wasConnected)
                    {
                        wasConnected = false;
                        RaiseConnectionChanged(false);
                    }

                    if (!TryReconnect())
                    {
                        // Wait before the next attempt, but stay responsive to Close().
                        for (int i = 0; i < mReconnectDelayMilliseconds / 50 && mRunning; i++)
                            Thread.Sleep(50);
                    }

                    nextFrame = Stopwatch.GetTimestamp();
                    continue;
                }

                // Pace to a fixed frame rate, compensating for the time the frame itself took.
                nextFrame += framePeriodTicks;
                long now = Stopwatch.GetTimestamp();

                if (nextFrame <= now)
                {
                    // We fell behind (OS jitter, port stall) - resynchronize instead of bursting.
                    nextFrame = now;
                    continue;
                }

                // Sleep coarsely, then spin the last ~2 ms for an accurate frame period.
                long remainingMs = (nextFrame - now) * 1000 / Stopwatch.Frequency;

                if (remainingMs > 2)
                    Thread.Sleep((int)(remainingMs - 2));

                while (mRunning && Stopwatch.GetTimestamp() < nextFrame)
                {
                    Thread.SpinWait(100);
                }
            }
        }

        private bool TryReconnect()
        {
            lock (mPortLock)
            {
                if (!mRunning || mPortName == null)
                    return false;

                try
                {
                    mSerialPort?.Dispose();
                    mSerialPort = null;

                    OpenPort();
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        private void RaiseConnectionChanged(bool connected)
        {
            try
            {
                ConnectionChanged?.Invoke(connected);
            }
            catch (Exception)
            {
                // Never let a subscriber exception kill the send loop.
            }
        }

        private void SendFrame(byte[] frame)
        {
            // Ensure the previous frame has fully left the driver's TX buffer.
            // Asserting the Break while bytes are still in flight would corrupt them.
            WaitForTxDrain(mSerialPort);

            // Break (>= 88 µs) followed by Mark After Break (>= 8 µs)
            mSerialPort.BreakState = true;
            WaitMicroseconds(mBreakMicroseconds);
            mSerialPort.BreakState = false;
            WaitMicroseconds(mMarkAfterBreakMicroseconds);

            frame[0] = 0; // DMX start code
            mSerialPort.Write(frame, 0, frame.Length);
        }

        private static void WaitForTxDrain(SerialPort port)
        {
            // Poll BytesToWrite with a timeout so a stalled driver can't hang the send loop.
            long timeout = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 10; // 100 ms

            while (port.IsOpen && port.BytesToWrite > 0 && Stopwatch.GetTimestamp() < timeout)
            {
                Thread.Sleep(1);
            }
        }

        /// <summary>
        /// Busy waits for the given number of microseconds. Thread.Sleep and Thread.SpinWait
        /// are too inaccurate for the sub-millisecond timing required by DMX512.
        /// </summary>
        private static void WaitMicroseconds(double microseconds)
        {
            long targetTicks = (long)(Stopwatch.Frequency * microseconds / 1_000_000d);
            long start = Stopwatch.GetTimestamp();

            while (Stopwatch.GetTimestamp() - start < targetTicks)
            {
                Thread.SpinWait(50);
            }
        }

        public void Close()
        {
            lock (mPortLock)
            {
                CloseCore();
            }
        }

        private void CloseCore()
        {
            mRunning = false;
            mPortName = null;

            if (mSendThread != null && mSendThread != Thread.CurrentThread)
                mSendThread.Join(500);

            mSendThread = null;

            if (mSerialPort != null)
            {
                try
                {
                    if (mSerialPort.IsOpen)
                    {
                        // Leave the bus dark instead of freezing on the last frame.
                        mSerialPort.BreakState = false;
                        mSerialPort.Close();
                    }
                }
                catch (Exception)
                {
                    // The device may already be gone (e.g. USB unplug) - ignore.
                }

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
