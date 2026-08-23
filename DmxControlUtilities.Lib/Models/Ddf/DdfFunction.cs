namespace DmxControlUtilities.Lib.Models.Ddf
{
    /// <summary>
    /// A single editable function channel of a device described by a DDF file,
    /// e.g. rgb/red, dimmer, strobe, rawstep "Program", position/pan.
    /// </summary>
    public class DdfFunction
    {
        /// <summary>
        /// Stable key used as dictionary key on <see cref="Device.Values"/>,
        /// e.g. "rgb/red", "dimmer", "rawstep/Program", "position/pan/fine".
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Display name, e.g. "Red", "Dimmer", "Program", "Pan (fine)".
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The DDF element this function originates from, e.g. rgb, dimmer, rawstep.
        /// </summary>
        public DdfFunctionType FunctionType { get; set; }

        /// <summary>
        /// 0-based DMX channel offset relative to the device's start channel.
        /// </summary>
        public int DmxChannel { get; set; }

        public byte DefaultValue { get; set; }

        /// <summary>
        /// Discrete steps of this function (rawstep, colorwheel, gobowheel). Empty for continuous functions.
        /// </summary>
        public List<DdfStep> Steps { get; set; } = new();

        /// <summary>
        /// Value ranges of this function (metadata). Empty for plain channels.
        /// </summary>
        public List<DdfRange> Ranges { get; set; } = new();
    }
}
