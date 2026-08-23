using System.Globalization;

namespace DmxControlUtilities.Lib.Services.Hal
{
    /// <summary>
    /// Color conversion helpers for the HAL-style abstraction (RGB, CMY, HSV, color wheel).
    /// All channel values are normalized 0..1 doubles unless noted otherwise.
    /// </summary>
    public static class HalColor
    {
        /// <summary>
        /// Converts RGB (0..1) to CMY (0..1, subtractive).
        /// </summary>
        public static (double C, double M, double Y) RgbToCmy(double r, double g, double b)
        {
            return (1.0 - r, 1.0 - g, 1.0 - b);
        }

        /// <summary>
        /// Converts CMY (0..1, subtractive) to RGB (0..1).
        /// </summary>
        public static (double R, double G, double B) CmyToRgb(double c, double m, double y)
        {
            return (1.0 - c, 1.0 - m, 1.0 - y);
        }

        /// <summary>
        /// Converts RGB (0..1) to HSV (h 0..360, s/v 0..1).
        /// </summary>
        public static (double H, double S, double V) RgbToHsv(double r, double g, double b)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double h = 0;

            if (delta > 0)
            {
                if (max == r)
                    h = 60 * (((g - b) / delta) % 6);
                else if (max == g)
                    h = 60 * ((b - r) / delta + 2);
                else
                    h = 60 * ((r - g) / delta + 4);
            }

            if (h < 0)
                h += 360;

            double s = max == 0 ? 0 : delta / max;

            return (h, s, max);
        }

        /// <summary>
        /// Converts HSV (h 0..360, s/v 0..1) to RGB (0..1).
        /// </summary>
        public static (double R, double G, double B) HsvToRgb(double h, double s, double v)
        {
            h = (h % 360 + 360) % 360;

            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;

            (double r, double g, double b) = h switch
            {
                < 60 => (c, x, 0.0),
                < 120 => (x, c, 0.0),
                < 180 => (0.0, c, x),
                < 240 => (0.0, x, c),
                < 300 => (x, 0.0, c),
                _ => (c, 0.0, x),
            };

            return (r + m, g + m, b + m);
        }

        /// <summary>
        /// Parses a "#rrggbb" hex string to RGB bytes. Returns null when invalid.
        /// </summary>
        public static (byte R, byte G, byte B)? FromHex(string? pHex)
        {
            if (string.IsNullOrWhiteSpace(pHex))
                return null;

            string hex = pHex.TrimStart('#');

            if (hex.Length != 6)
                return null;

            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
                return null;

            return ((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }

        /// <summary>
        /// Returns the index of the step whose hex color is nearest (Euclidean in RGB space)
        /// to the given RGB color. Steps without a valid hex value are ignored. Returns -1 when none.
        /// </summary>
        public static int FindNearestColorStep(IReadOnlyList<(byte R, byte G, byte B)?> pStepColors, byte pR, byte pG, byte pB)
        {
            int best = -1;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < pStepColors.Count; i++)
            {
                var color = pStepColors[i];

                if (color == null)
                    continue;

                int dr = color.Value.R - pR;
                int dg = color.Value.G - pG;
                int db = color.Value.B - pB;
                int distance = dr * dr + dg * dg + db * db;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }
    }
}
