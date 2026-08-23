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
        /// All editable function channels of this device, ordered by DMX channel.
        /// </summary>
        public List<DdfFunction> Functions { get; set; } = new();

        public string DisplayName => string.IsNullOrWhiteSpace(Vendor)
            ? $"{Model} ({Mode})"
            : $"{Vendor} {Model} ({Mode})";
    }
}
