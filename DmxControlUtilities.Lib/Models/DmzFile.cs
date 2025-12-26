namespace DmxControlUtilities.Lib.Models
{
    public class DmzFile
    {
        public required string FileName { get; set; }
        public required Stream FileStream { get; set; }
    }
}
