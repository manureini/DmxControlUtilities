namespace DmxControlUtilities.Lib.Models
{
    /// <summary>
    /// A track of a timecode show which contains the light events which are applied while the show is played.
    /// </summary>
    public class LightEventTrack : ITimecodeShowTrack
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        public List<LightEvent> Events { get; set; } = new();
    }
}
