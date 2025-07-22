namespace DmxControlUtilities.Models
{
    public class Timeshow
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;

        public string Xml { get; set; } = string.Empty;
        public string XmlFileName { get; set; } = string.Empty;

        public string ProjectExplorerXml { get; set; } = string.Empty;

        public List<DmzFile> Files { get; set; } = new List<DmzFile>();

        public List<SceneList> SceneLists { get; set; } = new List<SceneList>();
    }
}
