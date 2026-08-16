using System.Text.Json.Serialization;

namespace DmxControlUtilities.Lib.Models
{
    /// <summary>
    /// A single audio file within a <see cref="TimecodeAudioTrack"/>. Files are played sequentially,
    /// each starting after the previous one. Ranges without a file are silent.
    /// </summary>
    public class TimecodeAudioFile
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Display name of the imported audio file.
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Content of the audio file. The stream should be seekable so it can be read more than once.
        /// </summary>
        [JsonIgnore]
        public Stream? File { get; set; }

        /// <summary>
        /// Start position of this file on the track timeline (sum of the durations of all previous files).
        /// </summary>
        public TimeSpan StartOffset { get; set; }
    }
}
