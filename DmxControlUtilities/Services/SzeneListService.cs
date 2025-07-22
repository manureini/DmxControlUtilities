using DmxControlUtilities.Models;
using System.Xml.Linq;

namespace DmxControlUtilities.Services
{
    public class SzeneListService
    {
        public List<SceneList> GetSceneLists(DmzContainer container)
        {
            var files = container.Files.Where(f => f.FileName.StartsWith("Config/SceneLists") && f.FileName.EndsWith(".xml"));

            var sceneLists = new List<SceneList>();

            foreach (var file in files)
            {
                file.FileStream.Seek(0, SeekOrigin.Begin);
                var xmlContent = XDocument.Load(file.FileStream);

                var elements = xmlContent.Descendants("TreeItem").Where(e => e.Attribute("Name")?.Value == "SceneLists");

                foreach (var sclist in elements)
                {
                    foreach (var element in sclist.Descendants("TreeItem").Where(e => e.Attribute("Name")?.Value == "SceneList"))
                    {
                        var name = element.Descendants("Attribute")
                        .FirstOrDefault(a => a.Attribute("Name")?.Value == "Name")
                        ?.Attribute("Value")?.Value;
                        var id = element.Descendants("Attribute")
                            .FirstOrDefault(a => a.Attribute("Name")?.Value == "ID")
                            ?.Attribute("Value")?.Value;

                        if (Guid.TryParse(id, out var parsedId) && !string.IsNullOrEmpty(name))
                        {
                            sceneLists.Add(new SceneList
                            {
                                Id = parsedId,
                                Name = name,
                                Xml = element.ToString()
                            });
                        }
                    }

                }
            }

            return sceneLists;
        }
    }
}
