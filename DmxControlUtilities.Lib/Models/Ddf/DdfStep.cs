namespace DmxControlUtilities.Lib.Models.Ddf
{
    /// <summary>
    /// A discrete step inside a DDF function (e.g. rawstep caption, colorwheel color, gobo).
    /// </summary>
    public class DdfStep
    {
        public string Caption { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Optional value, e.g. "#ff0000" for colorwheel steps or an icon name for gobos.
        /// </summary>
        public string Value { get; set; } = string.Empty;

        public int MinDmx { get; set; }

        public int MaxDmx { get; set; }
    }
}
