namespace DmxControlUtilities.Models
{
    public class Preset
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Xml { get; set; } = string.Empty;
    }
}
