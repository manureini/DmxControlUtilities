using System.Text.Json.Serialization;

namespace DmxControlUtilities.Lib.Models
{
    /// <summary>
    /// A timecode show contains tracks (audio tracks and light event tracks) which are applied while the show is played.
    /// </summary>
    public class TimecodeShow
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        [JsonConverter(typeof(TimecodeShowTrackJsonConverter))]
        public List<ITimecodeShowTrack> Tracks { get; set; } = new();

        /// <summary>
        /// All audio tracks of this show.
        /// </summary>
        [JsonIgnore]
        public IEnumerable<TimecodeAudioTrack> AudioTracks => Tracks.OfType<TimecodeAudioTrack>();

        /// <summary>
        /// All light event tracks of this show.
        /// </summary>
        [JsonIgnore]
        public IEnumerable<LightEventTrack> LightEventTracks => Tracks.OfType<LightEventTrack>();

        /// <summary>
        /// All light events of all light event tracks of this show.
        /// </summary>
        [JsonIgnore]
        public IEnumerable<LightEvent> AllEvents => LightEventTracks.SelectMany(t => t.Events);
    }
}
