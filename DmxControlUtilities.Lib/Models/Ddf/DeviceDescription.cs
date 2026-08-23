namespace DmxControlUtilities.Lib.Models.Ddf
{
    /// <summary>
    /// Parsed DMXControl 3 device definition file (DDF).
    /// </summary>
    public class DeviceDescription
    {
        /// <summary>
        /// Stable id: file name without extension.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public string Vendor { get; set; } = string.Empty;

        public string Mode { get; set; } = string.Empty;

        public string Author { get; set; } = string.Empty;

        public string Image { get; set; } = string.Empty;

        /// <summary>
        /// Full path of the parsed file (not part of the DDF itself).
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Number of DMX addresses this device occupies (dmxaddresscount attribute,
        /// or computed from the highest used channel when missing).
        /// </summary>
        public int ChannelCount { get; set; }

        /// <summary>
        /// Default behavior of the white channel when only RGB is set (whitechanneldefaultmode
        /// attribute, e.g. "addwhite", "none"). Empty when not declared. Stored verbatim; the HAL
        /// interprets it.
        /// </summary>
        public string WhiteChannelDefaultMode { get; set; } = string.Empty;

        /// <summary>
        /// Default behavior of the amber channel when only RGB is set (amberchanneldefaultmode
        /// attribute, e.g. "add", "none"). Empty when not declared. Stored verbatim; the HAL
        /// interprets it.
        /// </summary>
        public string AmberChannelDefaultMode { get; set; } = string.Empty;

        /// <summary>
        /// All editable function channels of this device, ordered by DMX channel.
        /// </summary>
        public List<DdfFunction> Functions { get; set; } = new();

        public string DisplayName => string.IsNullOrWhiteSpace(Vendor)
            ? $"{Model} ({Mode})"
            : $"{Vendor} {Model} ({Mode})";

        /// <summary>
        /// Returns the function with the given key, or null.
        /// </summary>
        public DdfFunction? GetFunction(string pKey)
        {
            return Functions.FirstOrDefault(f => f.Key.Equals(pKey, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns all functions whose key starts with the given prefix (e.g. "rgb/").
        /// </summary>
        public IEnumerable<DdfFunction> GetFunctionsByPrefix(string pPrefix)
        {
            return Functions.Where(f => f.Key.StartsWith(pPrefix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns all functions of the given type (e.g. dimmer, colorwheel).
        /// </summary>
        public IEnumerable<DdfFunction> GetFunctionsByType(DdfFunctionType pFunctionType)
        {
            return Functions.Where(f => f.FunctionType == pFunctionType);
        }

        /// <summary>
        /// True when the device has at least one rgb color channel.
        /// </summary>
        public bool HasRgb => Functions.Any(f => f.Key.StartsWith(DdfChannelKey.Rgb(ColorChannel.Red), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// True when the device has a hardware dimmer channel.
        /// </summary>
        public bool HasDimmer => Functions.Any(f => f.Key.Equals(DdfChannelKey.Function(FunctionChannel.Dimmer), StringComparison.OrdinalIgnoreCase));
    }
}
