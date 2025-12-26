namespace DmxControlUtilities.Lib.Models
{
    public class SceneList
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Xml { get; set; } = string.Empty;
    }
}
