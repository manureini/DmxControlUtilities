namespace DmxControlUtilities.Lib.Models.Ddf
{
    /// <summary>
    /// Color channels of an rgb color mixing function.
    /// </summary>
    public enum ColorChannel
    {
        Red,
        Green,
        Blue,
        White,
        Warmwhite,
        Naturalwhite,
        Coldwhite,
        Amber,
        Uv,
        Indigo,
        Cyan,
        Lime,
        Mint,
        Redorange,
    }

    /// <summary>
    /// Channels of a CMY (subtractive) color mixing function.
    /// </summary>
    public enum CmyChannel
    {
        Cyan,
        Magenta,
        Yellow,
    }

    /// <summary>
    /// Channels of an HSV color mixing function.
    /// </summary>
    public enum HsvChannel
    {
        Hue,
        Saturation,
        Value,
    }

    /// <summary>
    /// Position axes.
    /// </summary>
    public enum PositionAxis
    {
        Pan,
        Tilt,
    }

    /// <summary>
    /// Single-channel functions addressed directly by name.
    /// </summary>
    public enum FunctionChannel
    {
        Dimmer,
        Strobe,
        Colortemp,
        Ptspeed,
        Colorwheel,
        Shutter,
        Focus,
        Iris,
        Zoom,
        Frost,
        Prism,
        Fog,
        Fan,
        Switch,
        Rotation,
        Index,
    }

    /// <summary>
    /// Resolution suffix for higher-resolution (16/24/32-bit) channels.
    /// </summary>
    public enum Resolution
    {
        Coarse,
        Fine,
        Ultra,
        Ultrafine,
    }

    /// <summary>
    /// Builds the string keys used on <see cref="DdfFunction.Key"/> and <see cref="Device.Values"/>
    /// from strongly typed channel enums.
    /// </summary>
    public static class DdfChannelKey
    {
        public static string Rgb(ColorChannel pChannel, Resolution pResolution = Resolution.Coarse)
        {
            return "rgb/" + ToName(pChannel) + ResolutionSuffix(pResolution);
        }

        public static string Rgb(string pColorName, Resolution pResolution = Resolution.Coarse)
        {
            return "rgb/" + pColorName.ToLowerInvariant() + ResolutionSuffix(pResolution);
        }

        public static string Cmy(CmyChannel pChannel, Resolution pResolution = Resolution.Coarse)
        {
            return "cmy/" + ToName(pChannel) + ResolutionSuffix(pResolution);
        }

        public static string Hsv(HsvChannel pChannel, Resolution pResolution = Resolution.Coarse)
        {
            return "hsv/" + ToName(pChannel) + ResolutionSuffix(pResolution);
        }

        public static string Position(PositionAxis pAxis, Resolution pResolution = Resolution.Coarse)
        {
            return "position/" + ToName(pAxis) + ResolutionSuffix(pResolution);
        }

        public static string Function(FunctionChannel pChannel, Resolution pResolution = Resolution.Coarse)
        {
            return ToName(pChannel) + ResolutionSuffix(pResolution);
        }

        /// <summary>
        /// Parses a key (e.g. "rgb/red", "position/pan/fine", "dimmer") back into its
        /// strongly typed parts. Returns false for dynamic keys (rawstep/..., matrix/..., radix/...).
        /// </summary>
        public static bool TryParse(string pKey, out ParsedKey pParsed)
        {
            pParsed = default;

            if (string.IsNullOrWhiteSpace(pKey))
                return false;

            var segments = pKey.Split('/');
            string group = segments[0].ToLowerInvariant();

            // Resolution suffix (fine/ultra/ultrafine) is the last segment.
            Resolution resolution = Resolution.Coarse;

            // segments[1] is the channel name, unless it is a resolution suffix.
            bool hasChannel = segments.Length > 1;

            if (segments.Length > 1)
            {
                string last = segments[^1].ToLowerInvariant();

                if (last == "fine")
                {
                    resolution = Resolution.Fine;

                    // "position/pan/fine": channel present; "dimmer/fine": segments[1] is the suffix.
                    if (segments.Length == 2)
                        hasChannel = false;
                }
                else if (last == "ultra")
                {
                    resolution = Resolution.Ultra;

                    if (segments.Length == 2)
                        hasChannel = false;
                }
                else if (last == "ultrafine")
                {
                    resolution = Resolution.Ultrafine;

                    if (segments.Length == 2)
                        hasChannel = false;
                }
            }

            string? channelName = hasChannel ? segments[1] : null;

            switch (group)
            {
                case "rgb":
                    if (channelName != null && TryParseEnum<ColorChannel>(channelName, out var color))
                    {
                        pParsed = new ParsedKey(pKey, resolution, Color: color);
                        return true;
                    }
                    return false;

                case "cmy":
                    if (channelName != null && TryParseEnum<CmyChannel>(channelName, out var cmy))
                    {
                        pParsed = new ParsedKey(pKey, resolution, Cmy: cmy);
                        return true;
                    }
                    return false;

                case "hsv":
                    if (channelName != null && TryParseEnum<HsvChannel>(channelName, out var hsv))
                    {
                        pParsed = new ParsedKey(pKey, resolution, Hsv: hsv);
                        return true;
                    }
                    return false;

                case "position":
                    if (channelName != null && TryParseEnum<PositionAxis>(channelName, out var axis))
                    {
                        pParsed = new ParsedKey(pKey, resolution, Position: axis);
                        return true;
                    }
                    return false;

                default:
                    if (channelName == null && TryParseEnum<FunctionChannel>(group, out var function))
                    {
                        pParsed = new ParsedKey(pKey, resolution, Function: function);
                        return true;
                    }
                    return false;
            }
        }

        private static bool TryParseEnum<TEnum>(string pName, out TEnum pValue) where TEnum : struct, Enum
        {
            return Enum.TryParse(pName, true, out pValue);
        }

        private static string ResolutionSuffix(Resolution pResolution)
        {
            return pResolution switch
            {
                Resolution.Fine => "/fine",
                Resolution.Ultra => "/ultra",
                Resolution.Ultrafine => "/ultrafine",
                _ => string.Empty,
            };
        }

        private static string ToName<TEnum>(TEnum pValue) where TEnum : struct, Enum
        {
            return pValue.ToString().ToLowerInvariant();
        }

        /// <summary>
        /// The strongly typed parts of a parsed channel key. Only the property matching
        /// the key's group is non-null.
        /// </summary>
        public readonly record struct ParsedKey(
            string Key,
            Resolution Resolution,
            ColorChannel? Color = null,
            CmyChannel? Cmy = null,
            HsvChannel? Hsv = null,
            PositionAxis? Position = null,
            FunctionChannel? Function = null)
        {
            public bool IsColor => Color != null || Cmy != null || Hsv != null;
        }
    }
}
