namespace DmxControlUtilities.Lib.Models
{
    /// <summary>
    /// A track of a timecode show. A show can contain multiple tracks of different types.
    /// </summary>
    public interface ITimecodeShowTrack
    {
        Guid Id { get; set; }

        string Name { get; set; }
    }
}
