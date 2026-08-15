namespace DmxControlUtilities.Lib.Models
{
    /// <summary>
    /// A light event which applies a color to a device at a specific time position of the audio track.
    /// </summary>
    public class LightEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid DeviceId { get; set; }

        public TimeSpan Time { get; set; }

        public byte R { get; set; }

        public byte G { get; set; }

        public byte B { get; set; }

        public string ColorHex
        {
            get => $"#{R:x2}{G:x2}{B:x2}";
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                var hex = value.TrimStart('#');

                if (hex.Length != 6)
                    return;

                R = Convert.ToByte(hex.Substring(0, 2), 16);
                G = Convert.ToByte(hex.Substring(2, 2), 16);
                B = Convert.ToByte(hex.Substring(4, 2), 16);
            }
        }
    }
}
