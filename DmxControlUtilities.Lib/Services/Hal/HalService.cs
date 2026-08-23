using DmxControlUtilities.Lib.Models;
using DmxControlUtilities.Lib.Models.Ddf;

namespace DmxControlUtilities.Lib.Services.Hal
{
    /// <summary>
    /// Hardware Abstraction Layer (HAL) style access to a device.
    /// Translates hardware-independent function values (color as RGB, dimmer 0..1,
    /// position pan/tilt 0..1, color temperature in Kelvin) into the concrete DDF
    /// function channel values of the device's description, independent of the
    /// DMX channel layout or color mixing system (RGB, CMY, HSV, color wheel).
    /// <para>
    /// All setters write into <see cref="Device.Values"/>. Call
    /// <c>DeviceService.ApplyDevice</c> afterwards to send the values to the DMX universe.
    /// </para>
    /// </summary>
    public class HalService
    {
        // HAL ground-state defaults.
        private const int mDefaultColorTempKelvin = 6500;

        private readonly DeviceDescriptionService mDescriptionService;
        private readonly DeviceService? mDeviceService;

        public HalService(DeviceDescriptionService pDescriptionService, DeviceService? pDeviceService = null)
        {
            mDescriptionService = pDescriptionService;
            mDeviceService = pDeviceService;
        }

        private DeviceDescription? GetDescription(Device pDevice)
        {
            return mDescriptionService.GetDescription(pDevice.DescriptionId);
        }

        /// <summary>
        /// Pushes the device's current values to the DMX universe. Called automatically by the
        /// typed setters; only call this directly when values were changed outside of them.
        /// </summary>
        public void Apply(Device pDevice)
        {
            mDeviceService?.ApplyDevice(pDevice);
        }

        #region Color

        /// <summary>
        /// Sets the device color as RGB (0..255 each). The color is applied to whichever
        /// color mixing system the device supports: rgb (direct), cmy, hsv, or the nearest
        /// color on a color wheel.
        /// </summary>
        public void SetColor(Device pDevice, byte pR, byte pG, byte pB)
        {
            SetColorCore(pDevice, pR, pG, pB);
            Apply(pDevice);
        }

        private void SetColorCore(Device pDevice, byte pR, byte pG, byte pB)
        {
            var description = GetDescription(pDevice);

            if (description == null)
                return;

            double r = pR / 255.0, g = pG / 255.0, b = pB / 255.0;

            bool handled = false;

            // Additional LED colors (white/amber) are mixed in per the device's
            // whitechanneldefaultmode / amberchanneldefaultmode automix settings.
            ApplyWhiteAmberAutomix(description, ref r, ref g, ref b, out double white, out double amber);

            // RGB (direct), including extended colors white/amber/uv are left untouched.
            if (SetIfPresent(pDevice, description, DdfChannelKey.Rgb(ColorChannel.Red), ToByte(r)) |
                SetIfPresent(pDevice, description, DdfChannelKey.Rgb(ColorChannel.Green), ToByte(g)) |
                SetIfPresent(pDevice, description, DdfChannelKey.Rgb(ColorChannel.Blue), ToByte(b)))
            {
                handled = true;

                SetIfPresent(pDevice, description, DdfChannelKey.Rgb(ColorChannel.White), ToByte(white));
                SetIfPresent(pDevice, description, DdfChannelKey.Rgb(ColorChannel.Amber), ToByte(amber));
            }

            // CMY (subtractive).
            var cmy = description.GetFunctionsByPrefix("cmy/").ToList();

            if (cmy.Count > 0)
            {
                var (c, m, y) = HalColor.RgbToCmy(r, g, b);

                SetIfPresent(pDevice, description, DdfChannelKey.Cmy(CmyChannel.Cyan), ToByte(c));
                SetIfPresent(pDevice, description, DdfChannelKey.Cmy(CmyChannel.Magenta), ToByte(m));
                SetIfPresent(pDevice, description, DdfChannelKey.Cmy(CmyChannel.Yellow), ToByte(y));

                handled = true;
            }

            // HSV.
            var hsv = description.GetFunctionsByPrefix("hsv/").ToList();

            if (hsv.Count > 0)
            {
                var (h, s, v) = HalColor.RgbToHsv(r, g, b);

                SetIfPresent(pDevice, description, DdfChannelKey.Hsv(HsvChannel.Hue), ToByte(h / 360.0));
                SetIfPresent(pDevice, description, DdfChannelKey.Hsv(HsvChannel.Saturation), ToByte(s));
                SetIfPresent(pDevice, description, DdfChannelKey.Hsv(HsvChannel.Value), ToByte(v));

                handled = true;
            }

            // Color wheel: pick nearest fixed color.
            if (!handled)
            {
                var wheel = description.GetFunctionsByType(DdfFunctionType.Colorwheel).FirstOrDefault();

                if (wheel != null && wheel.Steps.Count > 0)
                {
                    var stepColors = wheel.Steps.Select(s => HalColor.FromHex(s.Value)).ToList();
                    int index = HalColor.FindNearestColorStep(stepColors, pR, pG, pB);

                    if (index >= 0)
                    {
                        pDevice.SetValue(wheel.Key, (byte)wheel.Steps[index].MinDmx);
                    }
                }
            }
        }

        /// <summary>
        /// Computes the white/amber LED contributions from an RGB color (all 0..1) according to the
        /// device's whitechanneldefaultmode/amberchanneldefaultmode. May reduce r/g/b for "onlywhite".
        /// Returns the white and amber channel intensities (0..1).
        /// </summary>
        private static void ApplyWhiteAmberAutomix(DeviceDescription pDescription, ref double r, ref double g, ref double b, out double pWhite, out double pAmber)
        {
            pWhite = 0;
            pAmber = 0;

            // Amber is derived from the original color's hue, before any white reduction.
            if (pDescription.AmberChannelDefaultMode == "add")
            {
                var (h, _, v) = HalColor.RgbToHsv(r, g, b);
                pAmber = HalColor.TrapezoidIntensity(h, 0, 60, 60, 120) * v;
            }

            double white = Math.Min(r, Math.Min(g, b));

            switch (pDescription.WhiteChannelDefaultMode)
            {
                case "addwhite":
                    // All four LEDs at full for white: keep rgb, add white on top.
                    pWhite = white;
                    break;

                case "onlywhite":
                    // Only the white LED for white: subtract the white component from rgb.
                    pWhite = white;
                    r -= white;
                    g -= white;
                    b -= white;
                    break;
            }
        }

        /// <summary>
        /// Returns the current color of the device as RGB bytes (best effort).
        /// </summary>
        public (byte R, byte G, byte B) GetColor(Device pDevice)
        {
            var description = GetDescription(pDevice);

            if (description == null)
                return (0, 0, 0);

            // RGB.
            if (description.GetFunction(DdfChannelKey.Rgb(ColorChannel.Red)) != null)
            {
                return (pDevice.GetValue(DdfChannelKey.Rgb(ColorChannel.Red)),
                    pDevice.GetValue(DdfChannelKey.Rgb(ColorChannel.Green)),
                    pDevice.GetValue(DdfChannelKey.Rgb(ColorChannel.Blue)));
            }

            // CMY.
            if (description.GetFunction(DdfChannelKey.Cmy(CmyChannel.Cyan)) != null)
            {
                var (r, g, b) = HalColor.CmyToRgb(
                    pDevice.GetValue(DdfChannelKey.Cmy(CmyChannel.Cyan)) / 255.0,
                    pDevice.GetValue(DdfChannelKey.Cmy(CmyChannel.Magenta)) / 255.0,
                    pDevice.GetValue(DdfChannelKey.Cmy(CmyChannel.Yellow)) / 255.0);

                return (ToByte(r), ToByte(g), ToByte(b));
            }

            // HSV.
            if (description.GetFunction(DdfChannelKey.Hsv(HsvChannel.Hue)) != null)
            {
                var (r, g, b) = HalColor.HsvToRgb(
                    pDevice.GetValue(DdfChannelKey.Hsv(HsvChannel.Hue)) / 255.0 * 360.0,
                    pDevice.GetValue(DdfChannelKey.Hsv(HsvChannel.Saturation)) / 255.0,
                    pDevice.GetValue(DdfChannelKey.Hsv(HsvChannel.Value)) / 255.0);

                return (ToByte(r), ToByte(g), ToByte(b));
            }

            // Color wheel: read the hex of the active step.
            var wheel = description.GetFunctionsByType(DdfFunctionType.Colorwheel).FirstOrDefault();

            if (wheel != null)
            {
                byte value = pDevice.GetValue(wheel.Key);
                var step = wheel.Steps.FirstOrDefault(s => value >= s.MinDmx && value <= s.MaxDmx);
                var color = HalColor.FromHex(step?.Value);

                if (color != null)
                    return color.Value;
            }

            return (0, 0, 0);
        }

        #endregion

        #region Dimmer

        /// <summary>
        /// Sets the intensity 0..1. Uses the hardware dimmer channel when present,
        /// otherwise scales the rgb channels virtually (HAL virtual dimmer).
        /// </summary>
        public void SetDimmer(Device pDevice, double pValue)
        {
            SetDimmerCore(pDevice, pValue);
            Apply(pDevice);
        }

        private void SetDimmerCore(Device pDevice, double pValue)
        {
            var description = GetDescription(pDevice);

            if (description == null)
                return;

            pValue = Math.Clamp(pValue, 0, 1);

            var dimmer = description.GetFunction(DdfChannelKey.Function(FunctionChannel.Dimmer));

            if (dimmer != null)
            {
                pDevice.SetValue(dimmer.Key, ToByte(pValue));
                return;
            }

            // Virtual dimmer: scale rgb channels.
            if (description.HasRgb)
            {
                var (r, g, b) = GetColor(pDevice);

                // Only scale relative to full color; store raw scaled values.
                pDevice.SetValue(DdfChannelKey.Rgb(ColorChannel.Red), ToByte(r / 255.0 * pValue));
                pDevice.SetValue(DdfChannelKey.Rgb(ColorChannel.Green), ToByte(g / 255.0 * pValue));
                pDevice.SetValue(DdfChannelKey.Rgb(ColorChannel.Blue), ToByte(b / 255.0 * pValue));
            }
        }

        /// <summary>
        /// Returns the intensity 0..1 (hardware dimmer, else max rgb channel).
        /// </summary>
        public double GetDimmer(Device pDevice)
        {
            var description = GetDescription(pDevice);

            if (description == null)
                return 0;

            var dimmer = description.GetFunction(DdfChannelKey.Function(FunctionChannel.Dimmer));

            if (dimmer != null)
                return pDevice.GetValue(dimmer.Key) / 255.0;

            var (r, g, b) = GetColor(pDevice);
            return Math.Max(r, Math.Max(g, b)) / 255.0;
        }

        #endregion

        #region Position

        /// <summary>
        /// Sets pan/tilt 0..1 each. 16-bit (fine) channels are set when present.
        /// </summary>
        public void SetPosition(Device pDevice, double pPan, double pTilt)
        {
            SetPositionCore(pDevice, pPan, pTilt);
            Apply(pDevice);
        }

        private void SetPositionCore(Device pDevice, double pPan, double pTilt)
        {
            var description = GetDescription(pDevice);

            if (description == null)
                return;

            SetAxis(pDevice, description, PositionAxis.Pan, pPan);
            SetAxis(pDevice, description, PositionAxis.Tilt, pTilt);
        }

        private static void SetAxis(Device pDevice, DeviceDescription pDescription, PositionAxis pAxis, double pValue)
        {
            if (pDescription.GetFunction(DdfChannelKey.Position(pAxis)) == null)
                return;

            pValue = Math.Clamp(pValue, 0, 1);

            pDevice.SetValue(DdfChannelKey.Position(pAxis), ToByte(pValue));

            // 16-bit: distribute the fractional part into the fine channel.
            if (pDescription.GetFunction(DdfChannelKey.Position(pAxis, Resolution.Fine)) != null)
            {
                double coarse = pValue * 255.0;
                double fraction = coarse - Math.Floor(coarse);

                pDevice.SetValue(DdfChannelKey.Position(pAxis, Resolution.Fine), ToByte(fraction));
            }
        }

        /// <summary>
        /// Returns pan/tilt 0..1 each (combined coarse+fine when 16-bit).
        /// </summary>
        public (double Pan, double Tilt) GetPosition(Device pDevice)
        {
            var description = GetDescription(pDevice);

            if (description == null)
                return (0.5, 0.5);

            return (GetAxis(pDevice, description, PositionAxis.Pan), GetAxis(pDevice, description, PositionAxis.Tilt));
        }

        private static double GetAxis(Device pDevice, DeviceDescription pDescription, PositionAxis pAxis)
        {
            if (pDescription.GetFunction(DdfChannelKey.Position(pAxis)) == null)
                return 0.5;

            double coarse = pDevice.GetValue(DdfChannelKey.Position(pAxis));

            if (pDescription.GetFunction(DdfChannelKey.Position(pAxis, Resolution.Fine)) != null)
            {
                double fine = pDevice.GetValue(DdfChannelKey.Position(pAxis, Resolution.Fine));
                return Math.Clamp((coarse + fine / 255.0) / 255.0, 0, 1);
            }

            return coarse / 255.0;
        }

        #endregion

        #region Color temperature / strobe

        /// <summary>
        /// Sets the color temperature in Kelvin when the device has a colortemp function.
        /// The value is mapped onto the function's range (default 2500K..8000K).
        /// </summary>
        public void SetColorTemperature(Device pDevice, int pKelvin)
        {
            var description = GetDescription(pDevice);
            var colortemp = description?.GetFunction(DdfChannelKey.Function(FunctionChannel.Colortemp));

            if (colortemp == null)
                return;

            var range = colortemp.Ranges.FirstOrDefault();
            double minVal = range?.MinValue ?? 2500;
            double maxVal = range?.MaxValue ?? 8000;
            int minDmx = range?.MinDmx ?? 0;
            int maxDmx = range?.MaxDmx ?? 255;

            double fraction = maxVal == minVal ? 0 : (pKelvin - minVal) / (maxVal - minVal);
            fraction = Math.Clamp(fraction, 0, 1);

            int dmx = (int)Math.Round(minDmx + fraction * (maxDmx - minDmx));
            pDevice.SetValue(colortemp.Key, (byte)Math.Clamp(dmx, 0, 255));
            Apply(pDevice);
        }

        /// <summary>
        /// Sets the strobe frequency 0..1 mapped onto the strobe function's linear range.
        /// Does nothing when the device has no hardware strobe (virtual strobe out of scope).
        /// </summary>
        public void SetStrobe(Device pDevice, double pValue)
        {
            SetStrobeCore(pDevice, pValue);
            Apply(pDevice);
        }

        private void SetStrobeCore(Device pDevice, double pValue)
        {
            var description = GetDescription(pDevice);
            var strobe = description.GetFunction(DdfChannelKey.Function(FunctionChannel.Strobe))
                ?? description.GetFunction("strobo");

            if (strobe == null)
                return;

            var range = strobe.Ranges.FirstOrDefault(r => r.Type.Equals("linear", StringComparison.OrdinalIgnoreCase))
                ?? strobe.Ranges.FirstOrDefault();

            pValue = Math.Clamp(pValue, 0, 1);

            int minDmx = range?.MinDmx ?? 0;
            int maxDmx = range?.MaxDmx ?? 255;

            int dmx = (int)Math.Round(minDmx + pValue * (maxDmx - minDmx));
            pDevice.SetValue(strobe.Key, (byte)Math.Clamp(dmx, 0, 255));
        }

        #endregion

        #region Ground state

        /// <summary>
        /// Applies the HAL ground-state defaults to the device:
        /// color = white, position = center, ptspeed = 100%, colortemp = 6500K.
        /// Function default values from the DDF are respected where no explicit ground state exists.
        /// </summary>
        public void ResetToDefaults(Device pDevice)
        {
            var description = GetDescription(pDevice);

            if (description == null)
                return;

            foreach (var function in description.Functions)
            {
                pDevice.SetValue(function.Key, function.DefaultValue);
            }

            // Ground state: color white, position center, ptspeed 100%.
            SetColorCore(pDevice, 255, 255, 255);
            SetPositionCore(pDevice, 0.5, 0.5);

            var ptSpeed = description.GetFunction(DdfChannelKey.Function(FunctionChannel.Ptspeed));

            if (ptSpeed != null)
                pDevice.SetValue(ptSpeed.Key, 255);

            if (description.GetFunction(DdfChannelKey.Function(FunctionChannel.Colortemp)) != null)
            {
                SetColorTemperature(pDevice, mDefaultColorTempKelvin);
            }

            Apply(pDevice);
        }

        #endregion

        /// <summary>
        /// Sets a single channel value identified by its key, dispatching through the typed
        /// channel enums. Dynamic keys (rawstep/..., matrix/..., radix/...) and unrecognized
        /// keys are applied raw. Fine/ultra resolution channels are applied raw (they are
        /// derived from the coarse value by the typed setters).
        /// </summary>
        public void SetValue(Device pDevice, string pKey, byte pValue)
        {
            SetValueCore(pDevice, pKey, pValue);
            Apply(pDevice);
        }

        /// <summary>
        /// Sets multiple channel values (dispatching each through <see cref="SetValue"/>) and
        /// applies the device to the DMX universe once at the end.
        /// </summary>
        public void SetValues(Device pDevice, IEnumerable<KeyValuePair<string, byte>> pValues)
        {
            foreach (var pair in pValues)
            {
                SetValueCore(pDevice, pair.Key, pair.Value);
            }

            Apply(pDevice);
        }

        private void SetValueCore(Device pDevice, string pKey, byte pValue)
        {
            if (!DdfChannelKey.TryParse(pKey, out var parsed))
            {
                pDevice.SetValue(pKey, pValue);
                return;
            }

            double normalized = pValue / 255.0;

            if (parsed.Color != null)
            {
                var (r, g, b) = GetColor(pDevice);

                switch (parsed.Color.Value)
                {
                    case ColorChannel.Red: r = pValue; break;
                    case ColorChannel.Green: g = pValue; break;
                    case ColorChannel.Blue: b = pValue; break;
                    default:
                        // Extended color channels (white/amber/...) are applied directly.
                        pDevice.SetValue(pKey, pValue);
                        return;
                }

                SetColorCore(pDevice, r, g, b);
                return;
            }

            if (parsed.Cmy != null)
            {
                var (r, g, b) = GetColor(pDevice);
                var (c, m, y) = HalColor.RgbToCmy(r / 255.0, g / 255.0, b / 255.0);

                switch (parsed.Cmy.Value)
                {
                    case CmyChannel.Cyan: c = normalized; break;
                    case CmyChannel.Magenta: m = normalized; break;
                    case CmyChannel.Yellow: y = normalized; break;
                }

                var (rr, gg, bb) = HalColor.CmyToRgb(c, m, y);
                SetColorCore(pDevice, ToByte(rr), ToByte(gg), ToByte(bb));
                return;
            }

            if (parsed.Hsv != null)
            {
                var (r, g, b) = GetColor(pDevice);
                var (h, s, v) = HalColor.RgbToHsv(r / 255.0, g / 255.0, b / 255.0);

                switch (parsed.Hsv.Value)
                {
                    case HsvChannel.Hue: h = normalized * 360.0; break;
                    case HsvChannel.Saturation: s = normalized; break;
                    case HsvChannel.Value: v = normalized; break;
                }

                var (rr, gg, bb) = HalColor.HsvToRgb(h, s, v);
                SetColorCore(pDevice, ToByte(rr), ToByte(gg), ToByte(bb));
                return;
            }

            if (parsed.Position != null)
            {
                var (pan, tilt) = GetPosition(pDevice);

                if (parsed.Position.Value == PositionAxis.Pan)
                    SetPositionCore(pDevice, normalized, tilt);
                else
                    SetPositionCore(pDevice, pan, normalized);

                return;
            }

            if (parsed.Function != null)
            {
                switch (parsed.Function.Value)
                {
                    case FunctionChannel.Dimmer:
                        SetDimmerCore(pDevice, normalized);
                        return;

                    case FunctionChannel.Strobe:
                        SetStrobeCore(pDevice, normalized);
                        return;

                    default:
                        pDevice.SetValue(pKey, pValue);
                        return;
                }
            }

            pDevice.SetValue(pKey, pValue);
        }

        private static bool SetIfPresent(Device pDevice, DeviceDescription pDescription, string pKey, byte pValue)
        {
            if (pDescription.GetFunction(pKey) == null)
                return false;

            pDevice.SetValue(pKey, pValue);
            return true;
        }

        private static byte ToByte(double pValue)
        {
            return (byte)Math.Clamp((int)Math.Round(pValue * 255.0), 0, 255);
        }

        #region Features

        /// <summary>
        /// Returns the logical features of the device (typed by <see cref="FeatureType"/>),
        /// with higher-resolution DDF channels collapsed into their coarse feature. Values are
        /// normalized 0..1. DDF internals (keys, DMX offsets, resolutions) are not exposed.
        /// </summary>
        public IReadOnlyList<HalFeature> GetFeatures(Device pDevice)
        {
            var description = GetDescription(pDevice);
            var features = new List<HalFeature>();

            if (description == null)
                return features;

            // Group functions by their top-level key segment, keeping only coarse (base) channels.
            foreach (var group in description.Functions.GroupBy(f => f.Key.Split('/')[0]))
            {
                var coarse = group.Where(IsCoarseFunction).ToList();

                if (coarse.Count == 0)
                    continue;

                var type = coarse[0].FunctionType;

                switch (type)
                {
                    case DdfFunctionType.Rgb:
                        features.Add(new ColorFeature(this, pDevice));
                        break;

                    case DdfFunctionType.Position:
                        features.Add(new PositionFeature(this, pDevice));
                        break;

                    case DdfFunctionType.Dimmer:
                        features.Add(new DimmerFeature(this, pDevice));
                        break;

                    case DdfFunctionType.Strobe:
                        features.Add(new StrobeFeature(this, pDevice));
                        break;

                    default:
                        break;
                }
            }

            return features;
        }

        private static bool IsCoarseFunction(DdfFunction pFunction)
        {
            if (!DdfChannelKey.TryParse(pFunction.Key, out var parsed))
                return true; // dynamic key (rawstep/..., matrix/...) - no resolution suffix.

            return parsed.Resolution == Resolution.Coarse;
        }

        private static double FromByte(byte pValue) => pValue / 255.0;

        /// <summary>
        /// A single color feature. Internally converts the requested RGB value to the device's
        /// color mixing system (rgb/cmy/hsv/colorwheel) via <see cref="HalService.SetColor"/>.
        /// </summary>
        public sealed class ColorFeature : HalFeature
        {
            public ColorFeature(HalService pHal, Device pDevice)
                : base(pHal, pDevice, FeatureType.Color, "Color")
            {
            }

            /// <summary>
            /// The current color as RGB (0..255 each).
            /// </summary>
            public (byte R, byte G, byte B) GetColor() => mHal.GetColor(mDevice);

            /// <summary>
            /// Sets the color as RGB (0..255 each), converting to the device's color mixing system.
            /// </summary>
            public void SetColor(byte pR, byte pG, byte pB) => mHal.SetColor(mDevice, pR, pG, pB);

            /// <summary>
            /// Normalized brightness (max rgb channel) for the generic feature contract.
            /// </summary>
            public override double GetValue()
            {
                var (r, g, b) = GetColor();
                return Math.Max(r, Math.Max(g, b)) / 255.0;
            }

            public override void SetValue(double pValue)
            {
                var (r, g, b) = GetColor();
                double brightness = Math.Max(r, Math.Max(g, b)) / 255.0;

                if (brightness <= 0)
                {
                    byte v = ToByte(Math.Clamp(pValue, 0, 1));
                    SetColor(v, v, v);
                    return;
                }

                double scale = Math.Clamp(pValue, 0, 1) / brightness;
                SetColor(ToByte(r / 255.0 * scale), ToByte(g / 255.0 * scale), ToByte(b / 255.0 * scale));
            }
        }

        /// <summary>
        /// A single position feature exposing both axes (pan/tilt). Internally distributes
        /// each 0..1 axis onto its coarse+fine DDF channels via <see cref="HalService.SetPosition"/>.
        /// </summary>
        public sealed class PositionFeature : HalFeature
        {
            public PositionFeature(HalService pHal, Device pDevice)
                : base(pHal, pDevice, FeatureType.Position, "Position")
            {
            }

            /// <summary>
            /// The combined pan/tilt position, each 0..1 (combined coarse+fine).
            /// </summary>
            public (double Pan, double Tilt) GetPosition() => mHal.GetPosition(mDevice);

            /// <summary>
            /// Sets pan/tilt together (0..1 each), distributing onto coarse+fine channels.
            /// </summary>
            public void SetPosition(double pPan, double pTilt) => mHal.SetPosition(mDevice, pPan, pTilt);

            /// <summary>
            /// Pan (x) axis for the generic feature contract.
            /// </summary>
            public override double GetValue() => GetPosition().Pan;

            /// <summary>
            /// Sets the pan (x) axis, keeping the current tilt.
            /// </summary>
            public override void SetValue(double pValue)
            {
                var pos = GetPosition();
                SetPosition(Math.Clamp(pValue, 0, 1), pos.Tilt);
            }
        }

        private sealed class DimmerFeature : HalFeature
        {
            public DimmerFeature(HalService pHal, Device pDevice)
                : base(pHal, pDevice, FeatureType.Dimmer, "Dimmer")
            {
            }

            public override double GetValue() => mHal.GetDimmer(mDevice);

            public override void SetValue(double pValue) => mHal.SetDimmer(mDevice, pValue);
        }

        private sealed class StrobeFeature : HalFeature
        {
            public StrobeFeature(HalService pHal, Device pDevice)
                : base(pHal, pDevice, FeatureType.Strobe, "Strobe")
            {

            }

            public override double GetValue() => FromByte(mDevice.GetValue(DdfChannelKey.Function(FunctionChannel.Strobe)));

            public override void SetValue(double pValue) => mHal.SetStrobe(mDevice, pValue);
        }



        #endregion
    }
}
