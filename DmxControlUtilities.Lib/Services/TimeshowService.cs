using DmxControlUtilities.Lib.Models;
using System.Text;
using System.Xml.Linq;

namespace DmxControlUtilities.Lib.Services
{
    public class TimeshowService
    {
        protected readonly SzeneListService _szeneListService;

        // XML Element Names
        private const string XmlElementTreeItem = "TreeItem";
        private const string XmlElementAttribute = "Attribute";
        private const string XmlAttributeName = "Name";
        private const string XmlAttributeId = "ID";
        private const string XmlAttributeValue = "Value";

        // XML Element Names - Specific
        private const string XmlElementTimecodeShow = "TimecodeShow";
        private const string XmlElementNode = "Node";
        private const string XmlElementSoundFile = "SoundFile";
        private const string XmlElementScenelistIds = "ScenelistIDs";
        private const string XmlElementScenelist = "Scenelist";
        private const string XmlElementPreset = "Preset";
        private const string XmlElementPresets = "Presets";
        private const string XmlElementDeviceGroup = "DeviceGroup";
        private const string XmlElementDeviceGroups = "DeviceGroups";
        private const string XmlElementColorlist = "Colorlist";
        private const string XmlElementItemLists = "ItemLists";
        private const string XmlElementResources = "Resources";
        private const string XmlElementTimecodeShows = "TimecodeShows";
        private const string XmlElementSceneLists = "SceneLists";

        // XML File Paths
        private const string ConfigPathTimecodeShows = "Config/TimecodeShows";
        private const string ConfigPathTimecodeShowsXml = "Config/TimecodeShows.xml";
        private const string ConfigPathProjectExplorer = "Config/ProjectExplorer.xml";
        private const string ConfigPathPresets = "Config/Presets";
        private const string ConfigPathDeviceGroups = "Config/DeviceGroups";
        private const string ConfigPathItemList = "Config/ItemList";
        private const string ConfigPathProjectResourceMetadata = "Config/ProjectResourceMetadata.xml";
        private const string ConfigPathSceneLists = "Config/SceneLists";
        private const string ConfigPath = "Config/";

        public TimeshowService(SzeneListService szeneListService)
        {
            _szeneListService = szeneListService ?? throw new ArgumentNullException(nameof(szeneListService));
        }

        public List<TimeshowMeta> GetTimeshows(DmzContainer container)
        {
            if (container is null) throw new ArgumentNullException(nameof(container));

            var files = container.Files
                .Where(f => f.FileName.StartsWith(ConfigPathTimecodeShows, StringComparison.OrdinalIgnoreCase)
                            && !f.FileName.Contains($"/{ConfigPathTimecodeShows.Substring(7)}/", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var timeshows = new List<TimeshowMeta>();

            foreach (var file in files)
            {
                var xml = LoadXDocument(file);
                if (xml is null) continue;

                var timecodeShowElements = xml.Descendants(XmlElementTreeItem)
                    .Where(e => string.Equals((string?)e.Attribute(XmlAttributeName), XmlElementTimecodeShow, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var element in timecodeShowElements)
                {
                    var name = GetAttributeValue(element, XmlAttributeName);
                    var id = GetAttributeValue(element, XmlAttributeId);
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

        public Timeshow ExtractTimeshow(DmzContainer container, Guid timeshowId)
        {
            if (container is null) throw new ArgumentNullException(nameof(container));

            var timecodeXmlFile = container.Files
                .FirstOrDefault(f => f.FileName.Contains($"{ConfigPathTimecodeShows}/", StringComparison.OrdinalIgnoreCase) &&
                                     f.FileName.Contains(timeshowId.ToString(), StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Timecode show file for ID {timeshowId} not found.");

            var xmlContent = GetXmlString(timecodeXmlFile);

            var projectExplorerFile = container.Files
                .FirstOrDefault(f => f.FileName.Contains(ConfigPathProjectExplorer, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("ProjectExplorer.xml not found in container.");

            var projectExplorerXml = LoadXDocument(projectExplorerFile)
                ?? throw new InvalidOperationException("Failed to load ProjectExplorer.xml.");

            var xmlElem = FindNodeByIdAttribute(projectExplorerXml, timeshowId.ToString())
                ?? throw new InvalidOperationException($"ProjectExplorer entry for timeshow ID {timeshowId} not found.");

            var projectExplorerXmlContent = xmlElem.ToString();

            var timecodeXml = LoadXDocument(timecodeXmlFile) ?? throw new InvalidOperationException("Failed to load timeshow XML.");


            var treeItem = timecodeXml.Descendants("TreeItem").First(t => t.Attribute("Name").Value == "TimecodeShow");

            var item = treeItem.Descendants("Attribute").Where(ti => string.Equals((string?)ti.Attribute(XmlAttributeName), "Name", StringComparison.OrdinalIgnoreCase)).First();
            var tsName = item.Attribute("Value").Value;

            var item2 = treeItem.Descendants("Attribute").Where(ti => string.Equals((string?)ti.Attribute(XmlAttributeName), "Number", StringComparison.OrdinalIgnoreCase)).First();
            var tsNumber = item2.Attribute("Value").Value;

            var ret = new Timeshow
            {
                Id = timeshowId,
                Name = tsName,
                Number = tsNumber,
                Xml = xmlContent,
                XmlFileName = timecodeXmlFile.FileName,
                ProjectExplorerXml = projectExplorerXmlContent,
            };

            // collect sound files
            CollectSoundFiles(container, timecodeXml, ret);

            var allSceneLists = _szeneListService.GetSceneLists(container) ?? new List<SceneList>();

            // Populate scene lists for this timeshow
            PopulateSceneLists(timecodeXml, allSceneLists, ret);

            ret.Presets = GetPresets(container);
            ret.DeviceGroup = GetDeviceGroups(container);
            ret.ItemLists = GetItemListEntries(container);

            return ret;
        }

        public List<Preset> GetPresets(DmzContainer container)
        {
            if (container is null) throw new ArgumentNullException(nameof(container));

            var files = container.Files
                .Where(f => f.FileName.StartsWith(ConfigPathPresets, StringComparison.OrdinalIgnoreCase)
                            && !f.FileName.Contains($"/{ConfigPathPresets.Substring(7)}/", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var presets = new List<Preset>();

            foreach (var file in files)
            {
                var xml = LoadXDocument(file);
                if (xml is null) continue;

                var presetElements = xml.Descendants(XmlElementTreeItem)
                    .Where(e => string.Equals((string?)e.Attribute(XmlAttributeName), XmlElementPreset, StringComparison.OrdinalIgnoreCase));

                foreach (var element in presetElements)
                {
                    var id = GetAttributeValue(element, XmlAttributeId);
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

        public List<DeviceGroup> GetDeviceGroups(DmzContainer container)
        {
            var deviceGroups = new List<DeviceGroup>();

            var files = container.Files
             .Where(f => f.FileName.StartsWith(ConfigPathDeviceGroups, StringComparison.OrdinalIgnoreCase))
             .ToList();

            foreach (var file in files)
            {
                var xml = LoadXDocument(file);
                if (xml is null) continue;

                var presetElements = xml.Descendants(XmlElementTreeItem)
                    .Where(e => string.Equals((string?)e.Attribute(XmlAttributeName), XmlElementDeviceGroup, StringComparison.OrdinalIgnoreCase));

                foreach (var element in presetElements)
                {
                    var id = GetAttributeValue(element, XmlAttributeId);

                    if (Guid.TryParse(id, out var parsedId))
                    {
                        deviceGroups.Add(new DeviceGroup
                        {
                            Id = parsedId,
                            Xml = element.ToString()
                        });
                    }
                }
            }

            return deviceGroups;
        }

        public List<ItemListEntry> GetItemListEntries(DmzContainer container)
        {
            var itemLists = new List<ItemListEntry>();

            var files = container.Files
             .Where(f => f.FileName.StartsWith(ConfigPathItemList, StringComparison.OrdinalIgnoreCase))
             .ToList();

            foreach (var file in files)
            {
                var xml = LoadXDocument(file);
                if (xml is null) continue;

                var presetElements = xml.Descendants(XmlElementTreeItem)
                    .Where(e => string.Equals((string?)e.Attribute(XmlAttributeName), XmlElementColorlist, StringComparison.OrdinalIgnoreCase));

                foreach (var element in presetElements)
                {
                    var id = GetAttributeValue(element, XmlAttributeId);

                    if (Guid.TryParse(id, out var parsedId))
                    {
                        itemLists.Add(new ItemListEntry
                        {
                            Id = parsedId,
                            Xml = element.ToString()
                        });
                    }
                }
            }

            return itemLists;
        }

        public DmzContainer AddTimeshow(DmzContainer container, Timeshow timeshow)
        {
            if (container is null) throw new ArgumentNullException(nameof(container));
            if (timeshow is null) throw new ArgumentNullException(nameof(timeshow));

            if (container.Files.Any(f => f.FileName.Contains($"{ConfigPathTimecodeShows}/", StringComparison.OrdinalIgnoreCase)
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
            UpdateDeviceGroups(container, timeshow);
            UpdateItemLists(container, timeshow);

            if (timeshow.Files?.Any() == true)
            {
                container.Files.AddRange(timeshow.Files);
            }

            return container;
        }

        /// <summary>
        /// Configuration for updating configuration collection elements (Presets, DeviceGroups, ItemLists)
        /// </summary>
        private sealed record UpdateConfiguration(
            string filePathPrefix,
            string parentElementName,
            string childElementName,
            string errorMessageSuffix
        );

        /// <summary>
        /// Generic method to update configuration collection elements with deduplication logic
        /// </summary>
        private static void UpdateConfigurationElements<T>(
            DmzContainer container,
            IEnumerable<T> items,
            Func<T, Guid> idSelector,
            Func<T, string> xmlSelector,
            UpdateConfiguration config,
            Action<XElement, int>? additionalAction = null
        )
        {
            var configFiles = container.Files
                .Where(f => f.FileName.StartsWith(config.filePathPrefix, StringComparison.OrdinalIgnoreCase) && f.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.FileName)
                .ToList();

            if (!configFiles.Any()) return;

            var existingIds = new List<string>();

            // Collect all existing IDs from all config files
            foreach (var file in configFiles)
            {
                var xml = LoadXDocument(file);
                if (xml is null) continue;

                var parentElement = xml.Descendants(XmlElementTreeItem)
                    .FirstOrDefault(ti => string.Equals((string?)ti.Attribute(XmlAttributeName), config.parentElementName, StringComparison.OrdinalIgnoreCase));

                if (parentElement is null) continue;

                var ids = ExtractIdsFromXmlElements(parentElement, config.childElementName);
                existingIds.AddRange(ids);
            }

            var lastfile = configFiles.Last();
            var lastXml = LoadXDocument(lastfile) ?? throw new InvalidOperationException($"Failed to load last {config.errorMessageSuffix} file.");

            var parentEl = lastXml.Descendants(XmlElementTreeItem)
                .FirstOrDefault(ti => string.Equals((string?)ti.Attribute(XmlAttributeName), config.parentElementName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"{config.parentElementName} element not found in last {config.errorMessageSuffix} file.");

            int index = 0;
            foreach (var item in items)
            {
                var idString = idSelector(item).ToString();
                if (existingIds.Contains(idString, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var newElement = XElement.Parse(xmlSelector(item));
                var indexAttr = newElement.Elements(XmlElementAttribute).FirstOrDefault(x => string.Equals((string?)x.Attribute(XmlAttributeName), "ZZ_SAVE_INDEX", StringComparison.OrdinalIgnoreCase));
                indexAttr?.SetAttributeValue(XmlAttributeValue, existingIds.Count + index);

                additionalAction?.Invoke(newElement, existingIds.Count + index);

                parentEl.Add(newElement);
                existingIds.Add(idString);
                index++;
            }

            SaveXmlToFileStream(lastXml, lastfile);
        }

        private static void UpdateResourceMetadata(DmzContainer container, Timeshow timeshow)
        {
            var projectExplorerFile = container.Files.FirstOrDefault(f => f.FileName.Contains(ConfigPathProjectResourceMetadata, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("ProjectResourceMetadata.xml not found.");

            var resourceMetadataXml = LoadXDocument(projectExplorerFile) ?? throw new InvalidOperationException("Failed to load ProjectResourceMetadata.xml.");

            var projectResourcesElement = resourceMetadataXml.Descendants(XmlElementTreeItem).FirstOrDefault(ti => string.Equals((string?)ti.Attribute(XmlAttributeName), XmlElementResources, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Resources element not found in ProjectResourceMetadata.xml.");

            var newTreeItem = GetResourcesElement(timeshow.XmlFileName.Replace(ConfigPath, string.Empty), false);
            projectResourcesElement.Add(newTreeItem);

            foreach (var file in timeshow.Files ?? Enumerable.Empty<DmzFile>())
            {
                var filename = file.FileName.Replace(ConfigPath, string.Empty).Replace("/", "\\");
                var existing = projectResourcesElement.Descendants(XmlElementTreeItem).FirstOrDefault(t => string.Equals(t.Attribute(XmlAttributeName)?.Value, filename, StringComparison.OrdinalIgnoreCase));
                existing?.Remove();

                var resourceElement = GetResourcesElement(file.FileName, true);
                projectResourcesElement.Add(resourceElement);
            }

            SaveXmlToFileStream(resourceMetadataXml, projectExplorerFile);
        }

        private static void UpdateTimecodeShows(DmzContainer container, Timeshow timeshow)
        {
            var timecodeShowsFile = container.Files.FirstOrDefault(f => f.FileName.Contains(ConfigPathTimecodeShowsXml, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("TimecodeShows.xml not found.");

            var timecodeShowsXml = LoadXDocument(timecodeShowsFile) ?? throw new InvalidOperationException("Failed to load TimecodeShows.xml.");

            int count = timecodeShowsXml.Descendants(XmlElementTreeItem).Count(e => string.Equals((string?)e.Attribute(XmlAttributeName), XmlElementTimecodeShow, StringComparison.OrdinalIgnoreCase));

            var timecodeShowsElement = timecodeShowsXml.Descendants(XmlElementTreeItem)
                .FirstOrDefault(ti => string.Equals((string?)ti.Attribute(XmlAttributeName), XmlElementTimecodeShows, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("TimecodeShows element not found.");

            var timecodeShowElement = new XElement(XmlElementTreeItem,
                new XAttribute(XmlAttributeName, XmlElementTimecodeShow),
                new XElement(XmlElementAttribute,
                    new XAttribute(XmlAttributeName, XmlAttributeName),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "String"),
                    new XAttribute(XmlAttributeValue, timeshow.Name)
                ),
                new XElement(XmlElementAttribute,
                    new XAttribute(XmlAttributeName, XmlAttributeId),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "String"),
                    new XAttribute(XmlAttributeValue, timeshow.Id)
                ),
                new XElement(XmlElementAttribute,
                    new XAttribute(XmlAttributeName, "Number"),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "UInt32"),
                    new XAttribute(XmlAttributeValue, count + 1)
                ),
                new XElement(XmlElementAttribute,
                    new XAttribute(XmlAttributeName, "File"),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "String"),
                    new XAttribute(XmlAttributeValue, timeshow.XmlFileName.Replace(ConfigPath, string.Empty).Replace("/", "\\"))
                ),
                new XElement(XmlElementAttribute,
                    new XAttribute(XmlAttributeName, "ZZ_SAVE_INDEX"),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "Int32"),
                    new XAttribute(XmlAttributeValue, count)
                )
            );

            timecodeShowsElement.Add(timecodeShowElement);

            SaveXmlToFileStream(timecodeShowsXml, timecodeShowsFile);
        }

        private static void UpdateSceneLists(DmzContainer container, Timeshow timeshow)
        {
            var sceneListsFiles = container.Files
                .Where(f => f.FileName.StartsWith(ConfigPathSceneLists, StringComparison.OrdinalIgnoreCase) && f.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.FileName)
                .ToList();

            if (!sceneListsFiles.Any()) return;

            var sceneIds = new List<string>();
            int lastNumber = 1;

            foreach (var file in sceneListsFiles)
            {
                var sceneListsXml = LoadXDocument(file);
                if (sceneListsXml is null) continue;

                var sceneListsElement = sceneListsXml.Descendants(XmlElementTreeItem)
                    .FirstOrDefault(ti => string.Equals((string?)ti.Attribute(XmlAttributeName), XmlElementSceneLists, StringComparison.OrdinalIgnoreCase));

                if (sceneListsElement is null) continue;

                var fsceneIds = ExtractIdsFromXmlElements(sceneListsElement, XmlElementScenelist);
                sceneIds.AddRange(fsceneIds);

                var numbers = sceneListsElement.Elements(XmlElementTreeItem)
                    .Where(ti => string.Equals((string?)ti.Attribute(XmlAttributeName), XmlElementScenelist, StringComparison.OrdinalIgnoreCase))
                    .Select(ti => (string?)ti.Elements(XmlElementAttribute).FirstOrDefault(a => string.Equals((string?)a.Attribute(XmlAttributeName), "Number", StringComparison.OrdinalIgnoreCase))?.Attribute(XmlAttributeValue))
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Select(v => int.TryParse(v, out var n) ? n : 0);

                if (numbers.Any()) lastNumber = Math.Max(lastNumber, numbers.Max());
            }

            var lastfile = sceneListsFiles.Last();
            var lastsceneListsXml = LoadXDocument(lastfile) ?? throw new InvalidOperationException("Failed to load last SceneLists file.");

            var lastsceneListsElement = lastsceneListsXml.Descendants(XmlElementTreeItem)
                .FirstOrDefault(ti => string.Equals((string?)ti.Attribute(XmlAttributeName), XmlElementSceneLists, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("SceneLists element not found in last file.");

            foreach (var sceneList in timeshow.SceneLists ?? Enumerable.Empty<SceneList>())
            {
                if (sceneIds.Contains(sceneList.Id.ToString(), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                lastNumber++;

                var newSceneListElement = XElement.Parse(sceneList.Xml);

                var numberAttr = newSceneListElement.Elements(XmlElementAttribute).FirstOrDefault(x => string.Equals((string?)x.Attribute(XmlAttributeName), "Number", StringComparison.OrdinalIgnoreCase));
                numberAttr?.SetAttributeValue(XmlAttributeValue, lastNumber);

                var indexAttr = newSceneListElement.Elements(XmlElementAttribute).FirstOrDefault(x => string.Equals((string?)x.Attribute(XmlAttributeName), "ZZ_SAVE_INDEX", StringComparison.OrdinalIgnoreCase));
                indexAttr?.SetAttributeValue(XmlAttributeValue, sceneIds.Count);

                lastsceneListsElement.Add(newSceneListElement);
                sceneIds.Add(sceneList.Id.ToString());
            }

            SaveXmlToFileStream(lastsceneListsXml, lastfile);
        }

        private static void UpdateProjectExplorer(DmzContainer container, Timeshow timeshow)
        {
            var projectExplorerFile = container.Files.FirstOrDefault(f => f.FileName.Contains(ConfigPathProjectExplorer, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("ProjectExplorer.xml not found.");

            var projectExplorerXml = LoadXDocument(projectExplorerFile) ?? throw new InvalidOperationException("Failed to load ProjectExplorer.xml.");

            var cueListsElement = projectExplorerXml.Descendants(XmlElementTreeItem)
                .Where(ti => string.Equals((string?)ti.Attribute(XmlAttributeName), "Branch", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(ti => ti.Elements(XmlElementAttribute).Any(attr =>
                    string.Equals((string?)attr.Attribute(XmlAttributeName), XmlAttributeId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string?)attr.Attribute(XmlAttributeValue), "Cuelists", StringComparison.OrdinalIgnoreCase)));

            if (cueListsElement is null) throw new InvalidOperationException("Cuelists branch not found in ProjectExplorer.xml.");

            int cueListCount = cueListsElement.Descendants(XmlElementTreeItem).Count(e => string.Equals((string?)e.Attribute(XmlAttributeName), XmlElementNode, StringComparison.OrdinalIgnoreCase));

            var tsDirectoryXmlNode = new XElement(XmlElementTreeItem,
                new XAttribute(XmlAttributeName, XmlElementNode),
                new XElement(XmlElementAttribute,
                    new XAttribute(XmlAttributeName, XmlAttributeId),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "String"),
                    new XAttribute(XmlAttributeValue, Guid.NewGuid())
                ),
                new XElement(XmlElementAttribute,
                    new XAttribute(XmlAttributeName, "Index"),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "Int32"),
                    new XAttribute(XmlAttributeValue, cueListCount)
                ),
                new XElement(XmlElementAttribute,
                    new XAttribute(XmlAttributeName, "NodeType"),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "String"),
                    new XAttribute(XmlAttributeValue, "DirectoryNode")
                ),
                new XElement(XmlElementAttribute,
                    new XAttribute(XmlAttributeName, "Name"),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "String"),
                    new XAttribute(XmlAttributeValue, "F1")
                )
            );

            int nodeCount = 0;

            foreach (var sceneList in timeshow.SceneLists ?? Enumerable.Empty<SceneList>())
            {
                var treeItem = CreateTreeItemNode(sceneList.Id.ToString(), nodeCount);
                tsDirectoryXmlNode.Add(treeItem);
                nodeCount++;
            }

            cueListsElement.Add(tsDirectoryXmlNode);

            var timeCodeShowsElement = projectExplorerXml.Descendants(XmlElementTreeItem)
                .Where(ti => string.Equals((string?)ti.Attribute(XmlAttributeName), "Branch", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(ti => ti.Elements(XmlElementAttribute).Any(attr =>
                    string.Equals((string?)attr.Attribute(XmlAttributeName), XmlAttributeId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string?)attr.Attribute(XmlAttributeValue), XmlElementTimecodeShows, StringComparison.OrdinalIgnoreCase)));

            if (timeCodeShowsElement is null) throw new InvalidOperationException("TimecodeShows branch not found in ProjectExplorer.xml.");

            int timeCodeShowsCount = timeCodeShowsElement.Elements(XmlElementTreeItem).Count(e => string.Equals((string?)e.Attribute(XmlAttributeName), XmlElementNode, StringComparison.OrdinalIgnoreCase));

            var xelem = XElement.Parse(timeshow.ProjectExplorerXml);
            var indexAttribute = xelem.Elements(XmlElementAttribute).FirstOrDefault(x => string.Equals((string?)x.Attribute(XmlAttributeName), "Index", StringComparison.OrdinalIgnoreCase));
            indexAttribute?.SetAttributeValue(XmlAttributeValue, timeCodeShowsCount);

            timeCodeShowsElement.Add(xelem);

            var filesElement = projectExplorerXml.Descendants(XmlElementTreeItem)
                .Where(ti => string.Equals((string?)ti.Attribute(XmlAttributeName), "Branch", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(ti => ti.Elements(XmlElementAttribute).Any(attr =>
                    string.Equals((string?)attr.Attribute(XmlAttributeName), XmlAttributeId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string?)attr.Attribute(XmlAttributeValue), "Files", StringComparison.OrdinalIgnoreCase)));

            if (filesElement is null) throw new InvalidOperationException("Files branch not found in ProjectExplorer.xml.");

            int fileCount = filesElement.Elements(XmlElementTreeItem).Count(e => string.Equals((string?)e.Attribute(XmlAttributeName), XmlElementNode, StringComparison.OrdinalIgnoreCase));

            foreach (var file in timeshow.Files ?? Enumerable.Empty<DmzFile>())
            {
                var treeItem = CreateTreeItemNode(Path.GetFileName(file.FileName), fileCount);
                filesElement.Add(treeItem);
                fileCount++;
            }

            SaveXmlToFileStream(projectExplorerXml, projectExplorerFile);
        }

        private static void UpdatePresets(DmzContainer container, Timeshow timeshow)
        {
            var configuration = new UpdateConfiguration(
                filePathPrefix: ConfigPathPresets,
                parentElementName: XmlElementPresets,
                childElementName: XmlElementPreset,
                errorMessageSuffix: "Presets"
            );

            var presets = timeshow.Presets ?? Enumerable.Empty<Preset>();
            UpdateConfigurationElements(container, presets, p => p.Id, p => p.Xml, configuration);
        }


        private static void UpdateDeviceGroups(DmzContainer container, Timeshow timeshow)
        {
            var configuration = new UpdateConfiguration(
                filePathPrefix: ConfigPathDeviceGroups,
                parentElementName: XmlElementDeviceGroups,
                childElementName: XmlElementDeviceGroup,
                errorMessageSuffix: "DeviceGroups"
            );

            var deviceGroups = timeshow.DeviceGroup ?? Enumerable.Empty<DeviceGroup>();
            UpdateConfigurationElements(
                container,
                deviceGroups,
                dg => dg.Id,
                dg => dg.Xml,
                configuration,
                additionalAction: (element, index) => SetAttributeValueElementFromTreeItemParameter(element, "Group Number", index + 1)
            );
        }

        private static void UpdateItemLists(DmzContainer container, Timeshow timeshow)
        {
            var configuration = new UpdateConfiguration(
                filePathPrefix: ConfigPathItemList,
                parentElementName: XmlElementItemLists,
                childElementName: XmlElementColorlist,
                errorMessageSuffix: "ItemLists"
            );

            var itemLists = timeshow.ItemLists ?? Enumerable.Empty<ItemListEntry>();
            UpdateConfigurationElements(container, itemLists, il => il.Id, il => il.Xml, configuration);
        }

        private static XElement GetResourcesElement(string pName, bool value)
        {
            return new XElement(XmlElementTreeItem,
                new XAttribute(XmlAttributeName, pName.Replace(ConfigPath, string.Empty).Replace("/", "\\")),
                new XElement(XmlElementAttribute,
                    new XAttribute(XmlAttributeName, "UserImported"),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "Boolean"),
                    new XAttribute(XmlAttributeValue, value)
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

        /// <summary>
        /// Finds a TreeItem element that contains an Attribute with specific Name and Value, using case-insensitive comparison.
        /// </summary>
        private static XElement? FindTreeItemByAttributeValue(IEnumerable<XElement> elements, string attributeName, string attributeValue)
        {
            return elements
                .FirstOrDefault(ti => ti.Elements(XmlElementAttribute)
                    .Any(attr =>
                        string.Equals((string?)attr.Attribute(XmlAttributeName), attributeName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals((string?)attr.Attribute(XmlAttributeValue), attributeValue, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// Finds a TreeItem element by Name, then searches its descendants for another TreeItem with a specific attribute Name and value.
        /// </summary>
        private static XElement? FindNodeByIdAttribute(XDocument xmlDocument, string attributeValue)
        {
            var treeItems = xmlDocument.Descendants(XmlElementTreeItem)
                .Where(ti => string.Equals((string?)ti.Attribute(XmlAttributeName), XmlElementNode, StringComparison.OrdinalIgnoreCase));

            return FindTreeItemByAttributeValue(treeItems, XmlAttributeId, attributeValue);
        }

        /// <summary>
        /// Collects sound files referenced in a timeshow and adds them to the timeshow's file collection.
        /// </summary>
        private static void CollectSoundFiles(DmzContainer container, XDocument timecodeXml, Timeshow timeshow)
        {
            var soundFiles = timecodeXml.Descendants(XmlElementTreeItem)
                .Where(x => string.Equals((string?)x.Attribute(XmlAttributeName), XmlElementSoundFile, StringComparison.OrdinalIgnoreCase))
                .Select(x => GetAttributeValue(x, "SoundFileName"))
                .Where(fn => !string.IsNullOrWhiteSpace(fn))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var fileName in soundFiles)
            {
                // try to find matching file in container
                var soundFile = container.Files.FirstOrDefault(f =>
                    f.FileName.Contains($"{ConfigPath}{fileName}", StringComparison.OrdinalIgnoreCase) ||
                    f.FileName.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

                if (soundFile != null)
                {
                    timeshow.Files.Add(soundFile);
                }
            }
        }

        /// <summary>
        /// Populates the SceneLists collection of a timeshow from XML and available scene lists.
        /// </summary>
        private static void PopulateSceneLists(XDocument timecodeXml, List<SceneList> availableSceneLists, Timeshow timeshow)
        {
            var sceneListIdSections = timecodeXml.Descendants(XmlElementTreeItem)
                .Where(x => string.Equals((string?)x.Attribute(XmlAttributeName), XmlElementScenelistIds, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var section in sceneListIdSections)
            {
                var scenelists = section.Descendants(XmlElementTreeItem)
                    .Where(x => string.Equals((string?)x.Attribute(XmlAttributeName), XmlElementScenelist, StringComparison.OrdinalIgnoreCase));

                foreach (var scenelist in scenelists)
                {
                    var sceneListIdValue = GetAttributeValue(scenelist, "SceneListID");
                    if (Guid.TryParse(sceneListIdValue, out var parsedId))
                    {
                        var sceneList = availableSceneLists.FirstOrDefault(s => s.Id == parsedId);
                        if (sceneList != null && !timeshow.SceneLists.Any(s => s.Id == sceneList.Id))
                        {
                            timeshow.SceneLists.Add(sceneList);
                        }
                    }
                }
            }
        }

        private static void SetAttributeValueElementFromTreeItemParameter(XElement element, string nameValue, int value)
        {
            var elemen = element.Descendants(XmlElementTreeItem)
                .First(ti => ti.Elements(XmlElementAttribute).Any(attr =>
                        string.Equals((string?)attr.Attribute(XmlAttributeName), XmlAttributeName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals((string?)attr.Attribute(XmlAttributeValue), nameValue, StringComparison.OrdinalIgnoreCase)));

            var valueelement = elemen.Elements(XmlElementAttribute).First(a => a.Attribute(XmlAttributeName)!.Value == XmlAttributeValue);

            var valueAttr = valueelement.Attribute(XmlAttributeValue);

            valueAttr.Value = value.ToString();
        }

        /// <summary>
        /// Extracts ID values from TreeItem elements with a specific name attribute.
        /// </summary>
        private static List<string> ExtractIdsFromXmlElements(XElement parentElement, string treeItemName)
        {
            if (parentElement is null) return new List<string>();

            return parentElement.Elements(XmlElementTreeItem)
                .Where(ti => string.Equals((string?)ti.Attribute(XmlAttributeName), treeItemName, StringComparison.OrdinalIgnoreCase))
                .Select(ti => (string?)ti.Elements(XmlElementAttribute).FirstOrDefault(a => string.Equals((string?)a.Attribute(XmlAttributeName), XmlAttributeId, StringComparison.OrdinalIgnoreCase))?.Attribute(XmlAttributeValue))
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();
        }

        /// <summary>
        /// Creates a TreeItem XElement with Name and Index attributes.
        /// </summary>
        private static XElement CreateTreeItemNode(string id, int index)
        {
            return new XElement(XmlElementTreeItem,
                new XAttribute(XmlAttributeName, XmlElementNode),
                new XElement(XmlElementAttribute,
                    new XAttribute(XmlAttributeName, XmlAttributeId),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "String"),
                    new XAttribute(XmlAttributeValue, id)
                ),
                new XElement(XmlElementAttribute,
                    new XAttribute(XmlAttributeName, "Index"),
                    new XAttribute("Type", "Primitive"),
                    new XAttribute("ValueType", "Int32"),
                    new XAttribute(XmlAttributeValue, index)
                )
            );
        }

        /// <summary>
        /// Saves an XDocument to a file stream.
        /// </summary>
        private static void SaveXmlToFileStream(XDocument xmlDocument, DmzFile file)
        {
            if (xmlDocument is null || file is null) return;

            var ms = new MemoryStream();
            xmlDocument.Save(ms);
            ms.Seek(0, SeekOrigin.Begin);
            file.FileStream = ms;
        }

    }
}
