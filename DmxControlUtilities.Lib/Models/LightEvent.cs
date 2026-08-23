namespace DmxControlUtilities.Lib.Models
{
    /// <summary>
    /// A light event which applies function values to a device at a specific time position of the audio track.
    /// </summary>
    public class LightEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid DeviceId { get; set; }

        public TimeSpan Time { get; set; }

        /// <summary>
        /// Function values to apply, keyed by DDF function key (e.g. "rgb/red", "dimmer").
        /// </summary>
        public Dictionary<string, byte> Values { get; set; } = new();

        /// <summary>
        /// RGB color of the event based on its rgb function values (for display in the timeline).
        /// Returns neutral gray when the event has no rgb values.
        /// </summary>
        public string ColorHex
        {
            get
            {
                bool hasColor = Values.ContainsKey("rgb/red")
                    || Values.ContainsKey("rgb/green")
                    || Values.ContainsKey("rgb/blue");

                if (!hasColor)
                    return "#808080";

                byte r = Values.TryGetValue("rgb/red", out byte red) ? red : (byte)0;
                byte g = Values.TryGetValue("rgb/green", out byte green) ? green : (byte)0;
                byte b = Values.TryGetValue("rgb/blue", out byte blue) ? blue : (byte)0;

                return $"#{r:x2}{g:x2}{b:x2}";
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                var hex = value.TrimStart('#');

                if (hex.Length != 6)
                    return;

                Values["rgb/red"] = Convert.ToByte(hex.Substring(0, 2), 16);
                Values["rgb/green"] = Convert.ToByte(hex.Substring(2, 2), 16);
                Values["rgb/blue"] = Convert.ToByte(hex.Substring(4, 2), 16);
            }
        }
    }
}
