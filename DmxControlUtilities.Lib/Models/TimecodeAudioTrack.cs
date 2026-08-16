using System.Text.Json.Serialization;

namespace DmxControlUtilities.Lib.Models
{
    /// <summary>
    /// A track of a timecode show which contains the audio files which are played sequentially.
    /// The track is the time axis; ranges without an audio file are silent.
    /// </summary>
    public class TimecodeAudioTrack : ITimecodeShowTrack
    {
        private Stream? _audioFile;

        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The audio files of this track, played sequentially.
        /// </summary>
        public List<TimecodeAudioFile> AudioFiles { get; set; } = new();

        /// <summary>
        /// Display name of the first audio file. Kept for backwards compatibility.
        /// </summary>
        [JsonIgnore]
        public string AudioFileName
        {
            get => AudioFiles.FirstOrDefault()?.FileName ?? string.Empty;
            set
            {
                var first = AudioFiles.FirstOrDefault();

                if (first != null)
                    first.FileName = value;
            }
        }

        /// <summary>
        /// Content of the first audio file. Kept for backwards compatibility.
        /// Setting a stream replaces all existing files with a single file.
        /// </summary>
        [JsonIgnore]
        public Stream? AudioFile
        {
            get => AudioFiles.FirstOrDefault()?.File;
            set
            {
                _audioFile?.Dispose();
                _audioFile = value;

                AudioFiles.Clear();

                if (value != null)
                {
                    AudioFiles.Add(new TimecodeAudioFile
                    {
                        FileName = string.Empty,
                        File = value,
                        StartOffset = TimeSpan.Zero
                    });
                }
            }
        }

        public double Threshold { get; set; } = 0;

        public double Ratio { get; set; } = 1;

        public double Bandwidth { get; set; } = 2;
    }
}
