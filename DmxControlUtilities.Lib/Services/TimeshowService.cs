using DmxControlUtilities.Lib.Models;
using System.Text;
using System.Xml.Linq;

namespace DmxControlUtilities.Lib.Services
{
    public class TimeshowService
    {
        protected readonly SzeneListService _szeneListService;

        public TimeshowService(SzeneListService szeneListService)
        {
            _szeneListService = szeneListService ?? throw new ArgumentNullException(nameof(szeneListService));
        }

        public List<TimeshowMeta> GetTimeshows(DmzContainer container)
        {
            if (container is null) throw new ArgumentNullException(nameof(container));

            var files = container.Files
                .Where(f => f.FileName.StartsWith("Config/TimecodeShows", StringComparison.OrdinalIgnoreCase)
                            && !f.FileName.Contains("/TimecodeShows/", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var timeshows = new List<TimeshowMeta>();

            foreach (var file in files)
            {
                var xml = LoadXDocument(file);
                if (xml is null) continue;

                var timecodeShowElements = xml.Descendants("TreeItem")
                    .Where(e => string.Equals((string?)e.Attribute("Name"), "TimecodeShow", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var element in timecodeShowElements)
                {
                    var name = GetAttributeValue(element, "Name");
                    var id = GetAttributeValue(element, "ID");
                    var number = GetAttributeValue(element, "Number");

                    if (Guid.TryParse(id, out var parsedId) && !string.IsNullOrWhiteSpace(name))
                    {
                        timeshows.Add(new TimeshowMeta
                        {
                            Id = parsedId,
                            Name = name,
                            Number = number ?? string.Empty
                        });
                    }
                }
            }

            return timeshows;
        }

        public Timeshow ExtractTimeshow(DmzContainer container, TimeshowMeta timeshowMeta)
        {
            if (container is null) throw new ArgumentNullException(nameof(container));
            if (timeshowMeta is null) throw new ArgumentNullException(nameof(timeshowMeta));

            var timecodeXmlFile = container.Files
                .FirstOrDefault(f => f.FileName.Contains("Config/TimecodeShows/", StringComparison.OrdinalIgnoreCase) &&
                                     f.FileName.Contains(timeshowMeta.Id.ToString(), StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Timecode show file for ID {timeshowMeta.Id} not found.");

            var xmlContent = GetXmlString(timecodeXmlFile);

            var projectExplorerFile = container.Files
                .FirstOrDefault(f => f.FileName.Contains("Config/ProjectExplorer.xml", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("ProjectExplorer.xml not found in container.");

            var projectExplorerXml = LoadXDocument(projectExplorerFile)
                ?? throw new InvalidOperationException("Failed to load ProjectExplorer.xml.");

            var xmlElem = projectExplorerXml.Descendants("TreeItem")
                .Where(ti => string.Equals((string?)ti.Attribute("Name"), "Node", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(ti => ti.Elements("Attribute")
                    .Any(attr =>
                        string.Equals((string?)attr.Attribute("Name"), "ID", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals((string?)attr.Attribute("Value"), timeshowMeta.Id.ToString(), StringComparison.OrdinalIgnoreCase)));

            if (xmlElem is null)
            {
                throw new InvalidOperationException($"ProjectExplorer entry for timeshow ID {timeshowMeta.Id} not found.");
            }

            var projectExplorerXmlContent = xmlElem.ToString();

            var ret = new Timeshow
            {
                Id = timeshowMeta.Id,
                Name = timeshowMeta.Name,
                Number = timeshowMeta.Number,
                Xml = xmlContent,
                XmlFileName = timecodeXmlFile.FileName,
                ProjectExplorerXml = projectExplorerXmlContent,
            };

            var timecodeXml = LoadXDocument(timecodeXmlFile)
                ?? throw new InvalidOperationException("Failed to load timeshow XML.");

            // collect sound files
            var soundFiles = timecodeXml.Descendants("TreeItem")
                .Where(x => string.Equals((string?)x.Attribute("Name"), "SoundFile", StringComparison.OrdinalIgnoreCase))
                .Select(x => GetAttributeValue(x, "SoundFileName"))
                .Where(fn => !string.IsNullOrWhiteSpace(fn))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var fileName in soundFiles)
            {
                // try to find matching file in container
                var soundFile = container.Files.FirstOrDefault(f =>
                    f.FileName.Contains($"Config/{fileName}", StringComparison.OrdinalIgnoreCase) ||
                    f.FileName.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

                if (soundFile != null)
                {
                    ret.Files.Add(soundFile);
                }
            }

            var allSceneLists = _szeneListService.GetSceneLists(container) ?? new List<SceneList>();

            var sceneListIdSections = timecodeXml.Descendants("TreeItem")
                .Where(x => string.Equals((string?)x.Attribute("Name"), "ScenelistIDs", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var section in sceneListIdSections)
            {
                var scenelists = section.Descendants("TreeItem")
                    .Where(x => string.Equals((string?)x.Attribute("Name"), "Scenelist", StringComparison.OrdinalIgnoreCase));

                foreach (var scenelist in scenelists)
                {
                    var sceneListIdValue = GetAttributeValue(scenelist, "SceneListID");
                    if (Guid.TryParse(sceneListIdValue, out var parsedId))
                    {
                        var sceneList = allSceneLists.FirstOrDefault(s => s.Id == parsedId);
                        if (sceneList != null && !ret.SceneLists.Any(s => s.Id == sceneList.Id))
                        {
                            ret.SceneLists.Add(sceneList);
                        }
                    }
                }
            }

            ret.Presets = GetPresets(container);

            return ret;
        }

        public List<Preset> GetPresets(DmzContainer container)
        {
            if (container is null) throw new ArgumentNullException(nameof(container));

            var files = container.Files
                .Where(f => f.FileName.StartsWith("Config/Presets", StringComparison.OrdinalIgnoreCase)
                            && !f.FileName.Contains("/Presets/", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var presets = new List<Preset>();

            foreach (var file in files)
            {
                var xml = LoadXDocument(file);
                if (xml is null) continue;

                var presetElements = xml.Descendants("TreeItem")
                    .Where(e => string.Equals((string?)e.Attribute("Name"), "Preset", StringComparison.OrdinalIgnoreCase));

                foreach (var element in presetElements)
                {
                    var id = GetAttributeValue(element, "ID");
                    if (Guid.TryParse(id, out var parsedId))
                    {
                        presets.Add(new Preset
                        {
                            Id = parsedId,
                            Xml = element.ToString()
                        });
                    }
                }
            }

            return presets;
        }

        public DmzContainer AddTimeshow(DmzContainer container, Timeshow timeshow)
        {
            if (container is null) throw new ArgumentNullException(nameof(container));
            if (timeshow is null) throw new ArgumentNullException(nameof(timeshow));

            if (container.Files.Any(f => f.FileName.Contains("Config/TimecodeShows/", StringComparison.OrdinalIgnoreCase)
                                        && f.FileName.Contains(timeshow.Id.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("A timeshow with the same ID already exists.");
            }

            var timecodeXmlFile = new DmzFile
            {
                FileName = timeshow.XmlFileName,
                FileStream = new MemoryStream(Encoding.UTF8.GetBytes(timeshow.Xml ?? string.Empty))
            };

            container.Files.Add(timecodeXmlFile);

            UpdateProjectExplorer(container, timeshow);
            UpdateSceneLists(container, timeshow);
            UpdateTimecodeShows(container, timeshow);
            UpdateResourceMetadata(container, timeshow);
            UpdatePresets(container, timeshow);

            if (timeshow.Files?.Any() == true)
            {
                container.Files.AddRange(timeshow.Files);
            }

            return container;
        }

        private static void UpdateResourceMetadata(DmzContainer container, Timeshow timeshow)
        {
            var projectExplorerFile = container.Files.FirstOrDefault(f => f.FileName.Contains("Config/ProjectResourceMetadata.xml", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("ProjectResourceMetadata.xml not found.");

            var resourceMetadataXml = LoadXDocument(projectExplorerFile) ?? throw new InvalidOperationException("Failed to load ProjectResourceMetadata.xml.");

            var projectResourcesElement = resourceMetadataXml.Descendants("TreeItem").FirstOrDefault(ti => string.Equals((string?)ti.Attribute("Name"), "Resources", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Resources element not found in ProjectResourceMetadata.xml.");

            var newTreeItem = GetResourcesElement(timeshow.XmlFileName.Replace("Config/", string.Empty), false);
            projectResourcesElement.Add(newTreeItem);

            foreach (var file in timeshow.Files ?? Enumerable.Empty<DmzFile>())
            {
                var filename = file.FileName.Replace("Config/", string.Empty).Replace("/", "\\");
                var existing = projectResourcesElement.Descendants("TreeItem").FirstOrDefault(t => string.Equals(t.Attribute("Name")?.Value, filename, StringComparison.OrdinalIgnoreCase));
                existing?.Remove();

                var resourceElement = GetResourcesElement(file.FileName, true);
                projectResourcesElement.Add(resourceElement);
            }

            var ms = new MemoryStream();
            resourceMetadataXml.Save(ms);
            ms.Seek(0, SeekOrigin.Begin);
            projectExplorerFile.FileStream = ms;
        }

        private static void UpdateTimecodeShows(DmzContainer container, Timeshow timeshow)
        {
            var timecodeShowsFile = container.Files.FirstOrDefault(f => f.FileName.Contains("Config/TimecodeShows.xml", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("TimecodeShows.xml not found.");

            var timecodeShowsXml = LoadXDocument(timecodeShowsFile) ?? throw new InvalidOperationException("Failed to load TimecodeShows.xml.");

            int count = timecodeShowsXml.Descendants("TreeItem").Count(e => string.Equals((string?)e.Attribute("Name"), "TimecodeShow", StringComparison.OrdinalIgnoreCase));

            var timecodeShowsElement = timecodeShowsXml.Descendants("TreeItem")
                .FirstOrDefault(ti => string.Equals((string?)ti.Attribute("Name"), "TimecodeShows", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("TimecodeShows element not found.");

            var timecodeShowElement = new XElement("TreeItem",
                new XAttribute("Name", "TimecodeShow"),
                new XElement("Attribute",
                    new XAttribute("Name", "Name"),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "String"),
                    new XAttribute("Value", timeshow.Name)
                ),
                new XElement("Attribute",
                    new XAttribute("Name", "ID"),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "String"),
                    new XAttribute("Value", timeshow.Id)
                ),
                new XElement("Attribute",
                    new XAttribute("Name", "Number"),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "UInt32"),
                    new XAttribute("Value", count + 1)
                ),
                new XElement("Attribute",
                    new XAttribute("Name", "File"),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "String"),
                    new XAttribute("Value", timeshow.XmlFileName.Replace("Config/", string.Empty).Replace("/", "\\"))
                ),
                new XElement("Attribute",
                    new XAttribute("Name", "ZZ_SAVE_INDEX"),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "Int32"),
                    new XAttribute("Value", count)
                )
            );

            timecodeShowsElement.Add(timecodeShowElement);

            var ms = new MemoryStream();
            timecodeShowsXml.Save(ms);
            ms.Seek(0, SeekOrigin.Begin);
            timecodeShowsFile.FileStream = ms;
        }

        private static void UpdateSceneLists(DmzContainer container, Timeshow timeshow)
        {
            var sceneListsFiles = container.Files
                .Where(f => f.FileName.StartsWith("Config/SceneLists", StringComparison.OrdinalIgnoreCase) && f.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.FileName)
                .ToList();

            if (!sceneListsFiles.Any()) return;

            var sceneIds = new List<string>();
            int lastNumber = 1;

            foreach (var file in sceneListsFiles)
            {
                var sceneListsXml = LoadXDocument(file);
                if (sceneListsXml is null) continue;

                var sceneListsElement = sceneListsXml.Descendants("TreeItem")
                    .FirstOrDefault(ti => string.Equals((string?)ti.Attribute("Name"), "SceneLists", StringComparison.OrdinalIgnoreCase));

                if (sceneListsElement is null) continue;

                var fsceneIds = sceneListsElement.Elements("TreeItem")
                    .Where(ti => string.Equals((string?)ti.Attribute("Name"), "SceneList", StringComparison.OrdinalIgnoreCase))
                    .Select(ti => (string?)ti.Elements("Attribute").FirstOrDefault(a => string.Equals((string?)a.Attribute("Name"), "ID", StringComparison.OrdinalIgnoreCase))?.Attribute("Value"))
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();

                sceneIds.AddRange(fsceneIds);

                var numbers = sceneListsElement.Elements("TreeItem")
                    .Where(ti => string.Equals((string?)ti.Attribute("Name"), "SceneList", StringComparison.OrdinalIgnoreCase))
                    .Select(ti => (string?)ti.Elements("Attribute").FirstOrDefault(a => string.Equals((string?)a.Attribute("Name"), "Number", StringComparison.OrdinalIgnoreCase))?.Attribute("Value"))
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Select(v => int.TryParse(v, out var n) ? n : 0);

                if (numbers.Any()) lastNumber = Math.Max(lastNumber, numbers.Max());
            }

            var lastfile = sceneListsFiles.Last();
            var lastsceneListsXml = LoadXDocument(lastfile) ?? throw new InvalidOperationException("Failed to load last SceneLists file.");

            var lastsceneListsElement = lastsceneListsXml.Descendants("TreeItem")
                .FirstOrDefault(ti => string.Equals((string?)ti.Attribute("Name"), "SceneLists", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("SceneLists element not found in last file.");

            foreach (var sceneList in timeshow.SceneLists ?? Enumerable.Empty<SceneList>())
            {
                if (sceneIds.Contains(sceneList.Id.ToString(), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                lastNumber++;

                var newSceneListElement = XElement.Parse(sceneList.Xml);

                var numberAttr = newSceneListElement.Elements("Attribute").FirstOrDefault(x => string.Equals((string?)x.Attribute("Name"), "Number", StringComparison.OrdinalIgnoreCase));
                numberAttr?.SetAttributeValue("Value", lastNumber);

                var indexAttr = newSceneListElement.Elements("Attribute").FirstOrDefault(x => string.Equals((string?)x.Attribute("Name"), "ZZ_SAVE_INDEX", StringComparison.OrdinalIgnoreCase));
                indexAttr?.SetAttributeValue("Value", sceneIds.Count);

                lastsceneListsElement.Add(newSceneListElement);
                sceneIds.Add(sceneList.Id.ToString());
            }

            var ms = new MemoryStream();
            lastsceneListsXml.Save(ms);
            ms.Seek(0, SeekOrigin.Begin);
            lastfile.FileStream = ms;
        }

        private static void UpdateProjectExplorer(DmzContainer container, Timeshow timeshow)
        {
            var projectExplorerFile = container.Files.FirstOrDefault(f => f.FileName.Contains("Config/ProjectExplorer.xml", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("ProjectExplorer.xml not found.");

            var projectExplorerXml = LoadXDocument(projectExplorerFile) ?? throw new InvalidOperationException("Failed to load ProjectExplorer.xml.");

            var cueListsElement = projectExplorerXml.Descendants("TreeItem")
                .Where(ti => string.Equals((string?)ti.Attribute("Name"), "Branch", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(ti => ti.Elements("Attribute").Any(attr =>
                    string.Equals((string?)attr.Attribute("Name"), "ID", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string?)attr.Attribute("Value"), "Cuelists", StringComparison.OrdinalIgnoreCase)));

            if (cueListsElement is null) throw new InvalidOperationException("Cuelists branch not found in ProjectExplorer.xml.");

            int cueListCount = cueListsElement.Descendants("TreeItem").Count(e => string.Equals((string?)e.Attribute("Name"), "Node", StringComparison.OrdinalIgnoreCase));

            foreach (var sceneList in timeshow.SceneLists ?? Enumerable.Empty<SceneList>())
            {
                var treeItem = new XElement("TreeItem",
                    new XAttribute("Name", "Node"),
                    new XElement("Attribute",
                        new XAttribute("Name", "ID"),
                        new XAttribute("Type", "Primitive"),
                        new XAttribute("ValueType", "String"),
                        new XAttribute("Value", sceneList.Id)
                    ),
                    new XElement("Attribute",
                        new XAttribute("Name", "Index"),
                        new XAttribute("Type", "Primitive"),
                        new XAttribute("ValueType", "Int32"),
                        new XAttribute("Value", cueListCount)
                    ));

                cueListsElement.Add(treeItem);
                cueListCount++;
            }

            var timeCodeShowsElement = projectExplorerXml.Descendants("TreeItem")
                .Where(ti => string.Equals((string?)ti.Attribute("Name"), "Branch", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(ti => ti.Elements("Attribute").Any(attr =>
                    string.Equals((string?)attr.Attribute("Name"), "ID", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string?)attr.Attribute("Value"), "TimecodeShows", StringComparison.OrdinalIgnoreCase)));

            if (timeCodeShowsElement is null) throw new InvalidOperationException("TimecodeShows branch not found in ProjectExplorer.xml.");

            int timeCodeShowsCount = timeCodeShowsElement.Elements("TreeItem").Count(e => string.Equals((string?)e.Attribute("Name"), "Node", StringComparison.OrdinalIgnoreCase));

            var xelem = XElement.Parse(timeshow.ProjectExplorerXml);
            var indexAttribute = xelem.Elements("Attribute").FirstOrDefault(x => string.Equals((string?)x.Attribute("Name"), "Index", StringComparison.OrdinalIgnoreCase));
            indexAttribute?.SetAttributeValue("Value", timeCodeShowsCount);

            timeCodeShowsElement.Add(xelem);

            var filesElement = projectExplorerXml.Descendants("TreeItem")
                .Where(ti => string.Equals((string?)ti.Attribute("Name"), "Branch", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(ti => ti.Elements("Attribute").Any(attr =>
                    string.Equals((string?)attr.Attribute("Name"), "ID", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string?)attr.Attribute("Value"), "Files", StringComparison.OrdinalIgnoreCase)));

            if (filesElement is null) throw new InvalidOperationException("Files branch not found in ProjectExplorer.xml.");

            int fileCount = filesElement.Elements("TreeItem").Count(e => string.Equals((string?)e.Attribute("Name"), "Node", StringComparison.OrdinalIgnoreCase));

            foreach (var file in timeshow.Files ?? Enumerable.Empty<DmzFile>())
            {
                var treeItem = new XElement("TreeItem",
                    new XAttribute("Name", "Node"),
                    new XElement("Attribute",
                        new XAttribute("Name", "ID"),
                        new XAttribute("Type", "Primitive"),
                        new XAttribute("ValueType", "String"),
                        new XAttribute("Value", Path.GetFileName(file.FileName))
                    ),
                    new XElement("Attribute",
                        new XAttribute("Name", "Index"),
                        new XAttribute("Type", "Primitive"),
                        new XAttribute("ValueType", "Int32"),
                        new XAttribute("Value", fileCount)
                    ));

                filesElement.Add(treeItem);
                fileCount++;
            }

            var ms = new MemoryStream();
            projectExplorerXml.Save(ms);
            ms.Seek(0, SeekOrigin.Begin);
            projectExplorerFile.FileStream = ms;
        }

        private static void UpdatePresets(DmzContainer container, Timeshow timeshow)
        {
            var presetListFiles = container.Files
                .Where(f => f.FileName.StartsWith("Config/Presets", StringComparison.OrdinalIgnoreCase) && f.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.FileName)
                .ToList();

            if (!presetListFiles.Any()) return;

            var presetIds = new List<string>();

            foreach (var file in presetListFiles)
            {
                var presetListsXml = LoadXDocument(file);
                if (presetListsXml is null) continue;

                var presetsElement = presetListsXml.Descendants("TreeItem")
                    .FirstOrDefault(ti => string.Equals((string?)ti.Attribute("Name"), "Presets", StringComparison.OrdinalIgnoreCase));

                if (presetsElement is null) continue;

                var fsceneIds = presetsElement.Elements("TreeItem")
                    .Where(ti => string.Equals((string?)ti.Attribute("Name"), "Preset", StringComparison.OrdinalIgnoreCase))
                    .Select(ti => (string?)ti.Elements("Attribute").FirstOrDefault(a => string.Equals((string?)a.Attribute("Name"), "ID", StringComparison.OrdinalIgnoreCase))?.Attribute("Value"))
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();

                presetIds.AddRange(fsceneIds);
            }

            var lastfile = presetListFiles.Last();
            var lastsceneListsXml = LoadXDocument(lastfile) ?? throw new InvalidOperationException("Failed to load last Presets file.");

            var lastsceneListsElement = lastsceneListsXml.Descendants("TreeItem")
                .FirstOrDefault(ti => string.Equals((string?)ti.Attribute("Name"), "Presets", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Presets element not found in last Presets file.");

            foreach (var preset in timeshow.Presets ?? Enumerable.Empty<Preset>())
            {
                if (presetIds.Contains(preset.Id.ToString(), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var newPresetElement = XElement.Parse(preset.Xml);
                var indexAttr = newPresetElement.Elements("Attribute").FirstOrDefault(x => string.Equals((string?)x.Attribute("Name"), "ZZ_SAVE_INDEX", StringComparison.OrdinalIgnoreCase));
                indexAttr?.SetAttributeValue("Value", presetIds.Count);

                lastsceneListsElement.Add(newPresetElement);
                presetIds.Add(preset.Id.ToString());
            }

            var ms = new MemoryStream();
            lastsceneListsXml.Save(ms);
            ms.Seek(0, SeekOrigin.Begin);
            lastfile.FileStream = ms;
        }

        private static XElement GetResourcesElement(string pName, bool value)
        {
            return new XElement("TreeItem",
                new XAttribute("Name", pName.Replace("Config/", string.Empty).Replace("/", "\\")),
                new XElement("Attribute",
                    new XAttribute("Name", "UserImported"),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "Boolean"),
                    new XAttribute("Value", value)
                )
            );
        }

        private static string GetXmlString(DmzFile timecodeXmlFile)
        {
            if (timecodeXmlFile is null) throw new ArgumentNullException(nameof(timecodeXmlFile));
            timecodeXmlFile.FileStream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(timecodeXmlFile.FileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var xmlContent = reader.ReadToEnd();
            timecodeXmlFile.FileStream.Seek(0, SeekOrigin.Begin);
            return xmlContent;
        }

        private static XDocument? LoadXDocument(DmzFile file)
        {
            if (file is null) return null;
            try
            {
                file.FileStream.Seek(0, SeekOrigin.Begin);
                var doc = XDocument.Load(file.FileStream);
                file.FileStream.Seek(0, SeekOrigin.Begin);
                return doc;
            }
            catch
            {
                return null;
            }
        }

        private static string? GetAttributeValue(XElement parent, string attributeName)
        {
            if (parent is null || string.IsNullOrEmpty(attributeName)) return null;

            // Prefer direct child Attributes
            var attrElem = parent.Elements("Attribute")
                .FirstOrDefault(a => string.Equals((string?)a.Attribute("Name"), attributeName, StringComparison.OrdinalIgnoreCase));

            var value = attrElem?.Attribute("Value")?.Value;
            if (!string.IsNullOrEmpty(value)) return value;

            // fallback to descendants (some files use nested structure)
            var descAttr = parent.Descendants("Attribute")
                .FirstOrDefault(a => string.Equals((string?)a.Attribute("Name"), attributeName, StringComparison.OrdinalIgnoreCase));

            return descAttr?.Attribute("Value")?.Value;
        }
    }
}
