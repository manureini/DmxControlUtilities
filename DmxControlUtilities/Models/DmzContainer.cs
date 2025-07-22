namespace DmxControlUtilities.Models
{
    public class DmzContainer
    {      
        public string Name { get; set; } = string.Empty;
        public List<DmzFile> Files { get; set; } = [];
    }
}
