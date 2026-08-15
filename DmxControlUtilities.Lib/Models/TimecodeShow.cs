namespace DmxControlUtilities.Lib.Models
{
    /// <summary>
    /// A timecode show contains an audio track and the light events which are applied while the track is played.
    /// </summary>
    public class TimecodeShow
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Display name of the imported audio file.
        /// </summary>
        public string AudioFileName { get; set; } = string.Empty;

        /// <summary>
        /// Content of the audio file. The stream should be seekable so it can be read more than once.
        /// </summary>
        public Stream? AudioFilePath { get; set; }

        public double Threshold { get; set; } = 0;

        public double Ratio { get; set; } = 1;

        public double Bandwidth { get; set; } = 2;

        public List<LightEvent> Events { get; set; } = new();
    }
}
