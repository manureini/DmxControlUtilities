using DmxControlUtilities.Lib.Models.Ddf;

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
                string redKey = DdfChannelKey.Rgb(ColorChannel.Red);
                string greenKey = DdfChannelKey.Rgb(ColorChannel.Green);
                string blueKey = DdfChannelKey.Rgb(ColorChannel.Blue);

                bool hasColor = Values.ContainsKey(redKey)
                    || Values.ContainsKey(greenKey)
                    || Values.ContainsKey(blueKey);

                if (!hasColor)
                    return "#808080";

                byte r = Values.TryGetValue(redKey, out byte red) ? red : (byte)0;
                byte g = Values.TryGetValue(greenKey, out byte green) ? green : (byte)0;
                byte b = Values.TryGetValue(blueKey, out byte blue) ? blue : (byte)0;

                return $"#{r:x2}{g:x2}{b:x2}";
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                var hex = value.TrimStart('#');

                if (hex.Length != 6)
                    return;

                Values[DdfChannelKey.Rgb(ColorChannel.Red)] = Convert.ToByte(hex.Substring(0, 2), 16);
                Values[DdfChannelKey.Rgb(ColorChannel.Green)] = Convert.ToByte(hex.Substring(2, 2), 16);
                Values[DdfChannelKey.Rgb(ColorChannel.Blue)] = Convert.ToByte(hex.Substring(4, 2), 16);
            }
        }
    }
}
