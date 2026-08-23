namespace DmxControlUtilities.Lib.Models.Ddf
{
    /// <summary>
    /// A value range inside a DDF function (e.g. linear strobe frequency, wheel rotation speed).
    /// </summary>
    public class DdfRange
    {
        public string Type { get; set; } = string.Empty;

        public int MinDmx { get; set; }

        public int MaxDmx { get; set; }

        public double MinValue { get; set; }

        public double MaxValue { get; set; }
    }
}
