using DmxControlUtilities.Lib.Models.Ddf;
using DmxControlUtilities.Lib.Services.Hal;

namespace DmxControlUtilities.Lib.Models
{
    /// <summary>
    /// The kind of a staged/cue value: which logical feature shape it has.
    /// </summary>
    public enum CueFeatureValueKind
    {
        Color,
        Position,
        Scalar,
    }

    /// <summary>
    /// A value of a single logical HAL feature of one device, as stored in the programmer
    /// and in cues. The <see cref="Feature"/> key is the DDF group key of the feature
    /// (e.g. "rgb", "dimmer", "position", "rawstep/Program"). Raw DDF channels are never
    /// stored - channel resolution happens when the value is applied through the HAL.
    /// </summary>
    public abstract class CueFeatureValue
    {
        /// <summary>
        /// DDF group key of the feature (e.g. "rgb", "dimmer", "position").
        /// </summary>
        public string Feature { get; set; } = string.Empty;

        /// <summary>
        /// Display name of the feature (e.g. "Color", "Dimmer", "Program").
        /// </summary>
        public string Name { get; set; } = string.Empty;

        public abstract CueFeatureValueKind Kind { get; }

        /// <summary>
        /// True when the feature has discrete DDF steps; fades snap to the target at the end
        /// instead of interpolating.
        /// </summary>
        public bool IsDiscrete { get; set; }

        public abstract CueFeatureValue Clone();

        /// <summary>
        /// Writes this feature value onto the device through the HAL.
        /// The device must already be seeded with the values of any lower layers.
        /// </summary>
        public abstract void Apply(Device pDevice, HalService pHal);

        /// <summary>
        /// Interpolates between <paramref name="pFrom"/> and <paramref name="pTo"/> (same kind).
        /// Discrete values hold <paramref name="pFrom"/> until the fade completes.
        /// </summary>
        public static CueFeatureValue Lerp(CueFeatureValue pFrom, CueFeatureValue pTo, double pProgress)
        {
            if (pTo.IsDiscrete && pProgress < 1)
                return pFrom.Clone();

            return (pFrom, pTo) switch
            {
                (ColorCueValue a, ColorCueValue b) => new ColorCueValue
                {
                    Feature = b.Feature,
                    Name = b.Name,
                    R = LerpByte(a.R, b.R, pProgress),
                    G = LerpByte(a.G, b.G, pProgress),
                    B = LerpByte(a.B, b.B, pProgress),
                },
                (PositionCueValue a, PositionCueValue b) => new PositionCueValue
                {
                    Feature = b.Feature,
                    Name = b.Name,
                    Pan = a.Pan + (b.Pan - a.Pan) * pProgress,
                    Tilt = a.Tilt + (b.Tilt - a.Tilt) * pProgress,
                },
                (ScalarCueValue a, ScalarCueValue b) => new ScalarCueValue
                {
                    Feature = b.Feature,
                    Name = b.Name,
                    Value = a.Value + (b.Value - a.Value) * pProgress,
                },
                _ => pTo.Clone(),
            };
        }

        private static byte LerpByte(byte pA, byte pB, double pProgress)
        {
            return (byte)Math.Clamp((int)Math.Round(pA + (pB - pA) * pProgress, MidpointRounding.AwayFromZero), 0, 255);
        }

        /// <summary>
        /// Builds the typed value of the HAL feature from the given staged channel values,
        /// or null when the staged values do not touch any of the feature's channels.
        /// Unstaged feature channels fall back to <paramref name="pCurrentValues"/>.
        /// </summary>
        public static CueFeatureValue? FromStagedValues(
            DeviceDescription pDescription,
            HalFeature pFeature,
            IReadOnlyDictionary<string, byte> pStagedValues,
            IReadOnlyDictionary<string, byte> pCurrentValues)
        {
            if (!pFeature.GetKeys().Any(pStagedValues.ContainsKey))
                return null;

            byte Read(string pKey) => pStagedValues.TryGetValue(pKey, out byte v) ? v : pCurrentValues.GetValueOrDefault(pKey);

            bool discrete = pFeature.Steps.Count > 0 || pDescription.Functions
                .Where(f => f.Key.Equals(pFeature.Feature, StringComparison.OrdinalIgnoreCase)
                    || f.Key.StartsWith(pFeature.Feature + "/", StringComparison.OrdinalIgnoreCase))
                .Any(f => f.Steps.Count > 0);

            switch (pFeature.Type)
            {
                case FeatureType.Color:
                {
                    var (r, g, b) = ReadColor(pDescription, Read);
                    return new ColorCueValue { Feature = pFeature.Feature, Name = pFeature.Name, R = r, G = g, B = b };
                }

                case FeatureType.Position:
                {
                    var (pan, tilt) = ReadPosition(pDescription, Read);
                    return new PositionCueValue { Feature = pFeature.Feature, Name = pFeature.Name, Pan = pan, Tilt = tilt };
                }

                default:
                    return new ScalarCueValue
                    {
                        Feature = pFeature.Feature,
                        Name = pFeature.Name,
                        Value = ReadScalar(pDescription, pFeature, Read),
                        IsDiscrete = discrete,
                    };
            }
        }

        /// <summary>
        /// Reads the current typed value of the matching feature directly from a device's
        /// channel values (used to seed fade starts from live output).
        /// </summary>
        public static CueFeatureValue? FromDeviceValues(
            DeviceDescription pDescription,
            HalFeature pFeature,
            IReadOnlyDictionary<string, byte> pValues)
        {
            byte Read(string pKey) => pValues.GetValueOrDefault(pKey);

            switch (pFeature.Type)
            {
                case FeatureType.Color:
                {
                    var (r, g, b) = ReadColor(pDescription, Read);
                    return new ColorCueValue { Feature = pFeature.Feature, Name = pFeature.Name, R = r, G = g, B = b };
                }

                case FeatureType.Position:
                {
                    var (pan, tilt) = ReadPosition(pDescription, Read);
                    return new PositionCueValue { Feature = pFeature.Feature, Name = pFeature.Name, Pan = pan, Tilt = tilt };
                }

                default:
                {
                    if (!pFeature.GetKeys().Any(pValues.ContainsKey))
                        return null;

                    return new ScalarCueValue
                    {
                        Feature = pFeature.Feature,
                        Name = pFeature.Name,
                        Value = ReadScalar(pDescription, pFeature, Read),
                        IsDiscrete = pFeature.Steps.Count > 0,
                    };
                }
            }
        }

        private static (byte R, byte G, byte B) ReadColor(DeviceDescription pDescription, Func<string, byte> pRead)
        {
            if (pDescription.GetFunction(DdfChannelKey.Rgb(ColorChannel.Red)) != null)
            {
                return (pRead(DdfChannelKey.Rgb(ColorChannel.Red)),
                    pRead(DdfChannelKey.Rgb(ColorChannel.Green)),
                    pRead(DdfChannelKey.Rgb(ColorChannel.Blue)));
            }

            if (pDescription.GetFunction(DdfChannelKey.Cmy(CmyChannel.Cyan)) != null)
            {
                var (r, g, b) = HalColor.CmyToRgb(
                    pRead(DdfChannelKey.Cmy(CmyChannel.Cyan)) / 255.0,
                    pRead(DdfChannelKey.Cmy(CmyChannel.Magenta)) / 255.0,
                    pRead(DdfChannelKey.Cmy(CmyChannel.Yellow)) / 255.0);

                return (ToByte(r), ToByte(g), ToByte(b));
            }

            if (pDescription.GetFunction(DdfChannelKey.Hsv(HsvChannel.Hue)) != null)
            {
                var (r, g, b) = HalColor.HsvToRgb(
                    pRead(DdfChannelKey.Hsv(HsvChannel.Hue)) / 255.0 * 360.0,
                    pRead(DdfChannelKey.Hsv(HsvChannel.Saturation)) / 255.0,
                    pRead(DdfChannelKey.Hsv(HsvChannel.Value)) / 255.0);

                return (ToByte(r), ToByte(g), ToByte(b));
            }

            var wheel = pDescription.GetFunctionsByType(DdfFunctionType.Colorwheel).FirstOrDefault();

            if (wheel != null)
            {
                byte value = pRead(wheel.Key);
                var step = wheel.Steps.FirstOrDefault(s => value >= s.MinDmx && value <= s.MaxDmx);
                var color = HalColor.FromHex(step?.Value);

                if (color != null)
                    return color.Value;
            }

            return (0, 0, 0);
        }

        private static (double Pan, double Tilt) ReadPosition(DeviceDescription pDescription, Func<string, byte> pRead)
        {
            return (ReadAxis(pDescription, PositionAxis.Pan, pRead), ReadAxis(pDescription, PositionAxis.Tilt, pRead));
        }

        private static double ReadAxis(DeviceDescription pDescription, PositionAxis pAxis, Func<string, byte> pRead)
        {
            if (pDescription.GetFunction(DdfChannelKey.Position(pAxis)) == null)
                return 0.5;

            double coarse = pRead(DdfChannelKey.Position(pAxis));

            if (pDescription.GetFunction(DdfChannelKey.Position(pAxis, Resolution.Fine)) != null)
            {
                double fine = pRead(DdfChannelKey.Position(pAxis, Resolution.Fine));
                return Math.Clamp((coarse + fine / 255.0) / 255.0, 0, 1);
            }

            return coarse / 255.0;
        }

        private static double ReadScalar(DeviceDescription pDescription, HalFeature pFeature, Func<string, byte> pRead)
        {
            string key = pFeature.GetKeys().FirstOrDefault() ?? pFeature.Feature;
            var function = pDescription.GetFunction(key);

            if (function == null)
                return 0;

            byte dmx = pRead(key);

            if (function.Ranges.FirstOrDefault() is { } range && (range.MinDmx != 0 || range.MaxDmx != 255))
            {
                int span = range.MaxDmx - range.MinDmx;
                return span == 0 ? 0 : Math.Clamp((dmx - range.MinDmx) / (double)span, 0, 1);
            }

            return dmx / 255.0;
        }

        private static byte ToByte(double pValue)
        {
            return (byte)Math.Clamp((int)Math.Round(pValue * 255, MidpointRounding.AwayFromZero), 0, 255);
        }

        /// <summary>
        /// Short human-readable form for grids (e.g. "#ff8000", "50% / 25%", "75%").
        /// </summary>
        public abstract string ToDisplayString();
    }

    /// <summary>
    /// A color feature value as RGB bytes.
    /// </summary>
    public sealed class ColorCueValue : CueFeatureValue
    {
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }

        public override CueFeatureValueKind Kind => CueFeatureValueKind.Color;

        public override CueFeatureValue Clone()
        {
            return new ColorCueValue { Feature = Feature, Name = Name, IsDiscrete = IsDiscrete, R = R, G = G, B = B };
        }

        public override void Apply(Device pDevice, HalService pHal)
        {
            pHal.SetColor(pDevice, R, G, B);
        }

        public override string ToDisplayString() => $"#{R:x2}{G:x2}{B:x2}";
    }

    /// <summary>
    /// A position feature value as normalized pan/tilt (0..1 each).
    /// </summary>
    public sealed class PositionCueValue : CueFeatureValue
    {
        public double Pan { get; set; }
        public double Tilt { get; set; }

        public override CueFeatureValueKind Kind => CueFeatureValueKind.Position;

        public override CueFeatureValue Clone()
        {
            return new PositionCueValue { Feature = Feature, Name = Name, IsDiscrete = IsDiscrete, Pan = Pan, Tilt = Tilt };
        }

        public override void Apply(Device pDevice, HalService pHal)
        {
            pHal.SetPosition(pDevice, Pan, Tilt);
        }

        public override string ToDisplayString()
        {
            return $"{Percent(Pan)}% / {Percent(Tilt)}%";
        }

        private static int Percent(double pValue) => (int)Math.Round(Math.Clamp(pValue, 0, 1) * 100);
    }

    /// <summary>
    /// A scalar (single-axis) feature value normalized to 0..1 (dimmer, strobe, zoom, ...).
    /// </summary>
    public sealed class ScalarCueValue : CueFeatureValue
    {
        public double Value { get; set; }

        public override CueFeatureValueKind Kind => CueFeatureValueKind.Scalar;

        public override CueFeatureValue Clone()
        {
            return new ScalarCueValue { Feature = Feature, Name = Name, IsDiscrete = IsDiscrete, Value = Value };
        }

        public override void Apply(Device pDevice, HalService pHal)
        {
            pHal.SetFeatureValue(pDevice, Feature, Value);
        }

        public override string ToDisplayString()
        {
            return $"{(int)Math.Round(Math.Clamp(Value, 0, 1) * 100)}%";
        }
    }
}
