using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.Wave;
using System.Numerics;

namespace DmxControlUtilities.Web.Services
{
    public class AudioSpectrogramService
    {
        private const int FftSize = 2048;

        private string? _cachedFilePath;
        private List<double[]>? _cachedColumns;
        private double _cachedMaxMagnitude;

        public byte[] CreateSpectrogramBmp(string pAudioFilePath, int pMaxWidth = 2000, int pHeight = 512,
            double pThreshold = 0, double pRatio = 1, double pBandwidth = 2)
        {
            List<double[]> columns;
            double maxMagnitude;

            if (_cachedFilePath == pAudioFilePath && _cachedColumns != null)
            {
                columns = _cachedColumns;
                maxMagnitude = _cachedMaxMagnitude;
            }
            else
            {
                float[] samples = ReadMonoSamples(pAudioFilePath, out _);

                int step = Math.Max(FftSize / 2, (samples.Length - FftSize) / Math.Max(1, pMaxWidth));

                var window = Window.Hann(FftSize);
                columns = new List<double[]>();

                for (long pos = 0; pos + FftSize <= samples.Length; pos += step)
                {
                    var buffer = new Complex[FftSize];

                    for (int i = 0; i < FftSize; i++)
                    {
                        buffer[i] = new Complex(samples[pos + i] * window[i], 0);
                    }

                    Fourier.Forward(buffer, FourierOptions.Matlab);

                    var magnitudes = new double[FftSize / 2];

                    for (int i = 0; i < magnitudes.Length; i++)
                    {
                        magnitudes[i] = buffer[i].Magnitude;
                    }

                    columns.Add(magnitudes);
                }

                if (columns.Count == 0)
                    throw new InvalidOperationException("Audio file too short for analysis");

                maxMagnitude = columns.Max(c => c.Max());
                if (maxMagnitude <= 0)
                    maxMagnitude = 1;

                _cachedFilePath = pAudioFilePath;
                _cachedColumns = columns;
                _cachedMaxMagnitude = maxMagnitude;
            }

            int width = columns.Count;
            int height = pHeight;
            int bins = FftSize / 2;

            double minDb = -80;

            var pixels = new byte[width * height * 3];

            for (int x = 0; x < width; x++)
            {
                var column = columns[x];

                for (int y = 0; y < height; y++)
                {
                    // logarithmic frequency mapping, y = 0 is bottom row (low frequencies)
                    double fracLow = Math.Pow((double)y / height, pBandwidth);
                    double fracHigh = Math.Pow((double)(y + 1) / height, pBandwidth);

                    int binLow = (int)(fracLow * (bins - 1));
                    int binHigh = Math.Max(binLow + 1, (int)(fracHigh * (bins - 1)));

                    double magnitude = 0;
                    for (int b = binLow; b < binHigh && b < bins; b++)
                    {
                        magnitude = Math.Max(magnitude, column[b]);
                    }

                    double db = 20 * Math.Log10(magnitude / maxMagnitude + 1e-10);
                    double intensity = Math.Clamp((db - minDb) / -minDb, 0, 1);

                    // Threshold: cut off intensities below the threshold, rescale the rest
                    if (intensity < pThreshold)
                    {
                        intensity = 0;
                    }
                    else if (pThreshold < 1)
                    {
                        intensity = (intensity - pThreshold) / (1 - pThreshold);
                    }

                    // Ratio: compress or expand the dynamic range
                    intensity = Math.Pow(intensity, 1.0 / Math.Max(pRatio, 0.01));

                    var (r, g, b2) = MapColor(intensity);

                    // BMP is stored bottom-up, so row y = 0 in the buffer is the bottom of the image
                    int offset = (y * width + x) * 3;
                    pixels[offset + 0] = b2;
                    pixels[offset + 1] = g;
                    pixels[offset + 2] = r;
                }
            }

            return CreateBmp(width, height, pixels);
        }

        private static (byte R, byte G, byte B) MapColor(double pIntensity)
        {
            // black -> blue -> cyan -> green -> yellow -> red heatmap
            double v = pIntensity * 4;

            return v switch
            {
                < 1 => ((byte)0, (byte)0, (byte)(v * 255)),
                < 2 => ((byte)0, (byte)((v - 1) * 255), (byte)255),
                < 3 => ((byte)0, (byte)255, (byte)((3 - v) * 255)),
                < 4 => ((byte)((v - 3) * 255), (byte)255, (byte)0),
                _ => ((byte)255, (byte)((5 - Math.Min(v, 5)) * 255), (byte)0)
            };
        }

        private static float[] ReadMonoSamples(string pFilePath, out int pSampleRate)
        {
            using var reader = new AudioFileReader(pFilePath);

            pSampleRate = reader.WaveFormat.SampleRate;
            int channels = reader.WaveFormat.Channels;

            var samples = new List<float>((int)(reader.Length / 4 / channels));
            var buffer = new float[reader.WaveFormat.SampleRate * channels];

            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i += channels)
                {
                    float sum = 0;
                    for (int c = 0; c < channels && i + c < read; c++)
                    {
                        sum += buffer[i + c];
                    }
                    samples.Add(sum / channels);
                }
            }

            return samples.ToArray();
        }

        private static byte[] CreateBmp(int pWidth, int pHeight, byte[] pPixelsBgr)
        {
            int rowSize = (pWidth * 3 + 3) & ~3;
            int dataSize = rowSize * pHeight;
            int fileSize = 54 + dataSize;

            var bmp = new byte[fileSize];

            bmp[0] = (byte)'B';
            bmp[1] = (byte)'M';
            BitConverter.GetBytes(fileSize).CopyTo(bmp, 2);
            BitConverter.GetBytes(54).CopyTo(bmp, 10);
            BitConverter.GetBytes(40).CopyTo(bmp, 14);
            BitConverter.GetBytes(pWidth).CopyTo(bmp, 18);
            BitConverter.GetBytes(pHeight).CopyTo(bmp, 22);
            BitConverter.GetBytes((short)1).CopyTo(bmp, 26);
            BitConverter.GetBytes((short)24).CopyTo(bmp, 28);
            BitConverter.GetBytes(dataSize).CopyTo(bmp, 34);

            for (int y = 0; y < pHeight; y++)
            {
                Array.Copy(pPixelsBgr, y * pWidth * 3, bmp, 54 + y * rowSize, pWidth * 3);
            }

            return bmp;
        }
    }
}
