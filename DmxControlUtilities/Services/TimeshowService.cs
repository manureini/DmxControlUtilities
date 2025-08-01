﻿using DmxControlUtilities.Models;
using System.Text;
using System.Xml.Linq;

namespace DmxControlUtilities.Services
{
    public class TimeshowService
    {
        protected SzeneListService _szeneListService;

        public TimeshowService(SzeneListService szeneListService)
        {
            _szeneListService = szeneListService;
        }

        public List<TimeshowMeta> GetTimeshows(DmzContainer container)
        {
            var files = container.Files.Where(f => f.FileName.StartsWith("Config/TimecodeShows") && !f.FileName.Contains("/TimecodeShows/"));
            var timeshows = new List<TimeshowMeta>();

            foreach (var file in files)
            {
                var xmlContent = XDocument.Load(file.FileStream);

                var timecodeShowElements = xmlContent.Descendants("TreeItem")
                    .Where(e => e.Attribute("Name")?.Value == "TimecodeShow");

                foreach (var element in timecodeShowElements)
                {
                    var name = element.Descendants("Attribute")
                        .First(a => a.Attribute("Name")?.Value == "Name")
                        ?.Attribute("Value")?.Value;

                    var id = element.Descendants("Attribute")
                        .First(a => a.Attribute("Name")?.Value == "ID")
                        ?.Attribute("Value")?.Value;

                    var number = element.Descendants("Attribute")
                       .First(a => a.Attribute("Name")?.Value == "Number")
                       ?.Attribute("Value")?.Value;

                    if (Guid.TryParse(id, out var parsedId) && !string.IsNullOrEmpty(name))
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
            var timecodeXmlFile = container.Files.First(f => f.FileName.Contains("Config/TimecodeShows/") && f.FileName.Contains(timeshowMeta.Id.ToString()));

            string xmlContent = GetXmlString(timecodeXmlFile);

            var projectExplorerFile = container.Files.First(f => f.FileName.Contains("Config/ProjectExplorer.xml"));

            projectExplorerFile.FileStream.Seek(0, SeekOrigin.Begin);
            var projectExplorerXml = XDocument.Load(projectExplorerFile.FileStream);

            var xmlElem = projectExplorerXml.Descendants("TreeItem")
            .Where(ti => (string?)ti.Attribute("Name") == "Node")
            .First(ti =>
                ti.Elements("Attribute").Any(attr =>
                    (string?)attr.Attribute("Name") == "ID" &&
                    (string?)attr.Attribute("Value") == timeshowMeta.Id.ToString()
                )
            );

            var projectExplorerXmlContent = xmlElem.ToString();

            var ret = new Timeshow()
            {
                Id = timeshowMeta.Id,
                Name = timeshowMeta.Name,
                Number = timeshowMeta.Number,
                Xml = xmlContent,
                XmlFileName = timecodeXmlFile.FileName,
                ProjectExplorerXml = projectExplorerXmlContent,
            };

            timecodeXmlFile.FileStream.Seek(0, SeekOrigin.Begin);

            var timecodeXml = XDocument.Load(timecodeXmlFile.FileStream);

            var soundFiles = timecodeXml.Descendants("TreeItem")
                     .Where(x => (string?)x.Attribute("Name") == "SoundFile")
                     .Select(x => new
                     {
                         ID = x.Elements("Attribute")
                               .First(a => (string?)a.Attribute("Name") == "ID")?
                               .Attribute("Value")?.Value,

                         FileName = x.Elements("Attribute")
                               .First(a => (string?)a.Attribute("Name") == "SoundFileName")?
                               .Attribute("Value")?.Value
                     });


            foreach (var file in soundFiles)
            {
                var soundFile = container.Files.First(f => f.FileName.Contains($"Config/{file.FileName}"));
                ret.Files.Add(soundFile);
            }

            var allSceneLists = _szeneListService.GetSceneLists(container);

            var sceneListIds = timecodeXml.Descendants("TreeItem")
                .Where(x => (string?)x.Attribute("Name") == "ScenelistIDs");

            foreach (var sceneListIdElement in sceneListIds)
            {
                var scenelists = sceneListIdElement.Descendants("TreeItem")
                    .Where(x => (string?)x.Attribute("Name") == "Scenelist");

                foreach (var scenelist in scenelists)
                {
                    var sceneListIdValue = scenelist.Elements("Attribute")
                        .FirstOrDefault(a => (string?)a.Attribute("Name") == "SceneListID")?
                        .Attribute("Value")?.Value;

                    if (Guid.TryParse(sceneListIdValue, out var parsedId))
                    {
                        var sceneList = allSceneLists.First(s => s.Id == parsedId);

                        if (!ret.SceneLists.Any(s => s.Id == sceneList.Id))
                        {
                            ret.SceneLists.Add(sceneList);
                        }
                    }
                }
            }

            return ret;
        }

        public DmzContainer AddTimeshow(DmzContainer container, Timeshow timeshow)
        {
            if (container.Files.Any(f => f.FileName.Contains("Config/TimecodeShows/") && f.FileName.Contains(timeshow.Id.ToString())))
            {
                throw new InvalidOperationException("A timeshow with the same ID already exists.");
            }

            var timecodeXmlFile = new DmzFile
            {
                FileName = timeshow.XmlFileName,
                FileStream = new MemoryStream(Encoding.UTF8.GetBytes(timeshow.Xml))
            };

            container.Files.Add(timecodeXmlFile);
            UpdateProjectExplorer(container, timeshow);
            UpdateSceneLists(container, timeshow);
            UpdateTimecodeShows(container, timeshow);
            UpdateResourceMetadata(container, timeshow);

            container.Files.AddRange(timeshow.Files);

            return container;
        }

        private static void UpdateResourceMetadata(DmzContainer container, Timeshow timeshow)
        {
            var projectExplorerFile = container.Files.First(f => f.FileName.Contains("Config/ProjectResourceMetadata.xml"));

            projectExplorerFile.FileStream.Seek(0, SeekOrigin.Begin);

            var resourceMetadataxml = XDocument.Load(projectExplorerFile.FileStream);

            var projectResourcesElement = resourceMetadataxml.Descendants("TreeItem").First(ti => (string?)ti.Attribute("Name") == "Resources");

            XElement newTreeItem = GetResourcesElement(timeshow.XmlFileName.Replace("Config/", string.Empty), false);
            projectResourcesElement.Add(newTreeItem);

            foreach (var file in timeshow.Files)
            {
                var filename = file.FileName.Replace("Config/", string.Empty).Replace("/", "\\");

                var existing = projectResourcesElement.Descendants("TreeItem").FirstOrDefault(t => t.Attribute("Name")?.Value == filename);

                if (existing != null)
                {
                    existing.Remove();
                }

                var resourceElement = GetResourcesElement(file.FileName, true);
                projectResourcesElement.Add(resourceElement);
            }

            var resourceMetadataxmlMs = new MemoryStream();
            resourceMetadataxml.Save(resourceMetadataxmlMs);

            resourceMetadataxmlMs.Seek(0, SeekOrigin.Begin);
            projectExplorerFile.FileStream = resourceMetadataxmlMs;
        }

        private static void UpdateTimecodeShows(DmzContainer container, Timeshow timeshow)
        {
            var timecodeShowsFile = container.Files.First(f => f.FileName.Contains("Config/TimecodeShows.xml"));
            timecodeShowsFile.FileStream.Seek(0, SeekOrigin.Begin);

            var timecodeShowsXml = XDocument.Load(timecodeShowsFile.FileStream);

            int count = timecodeShowsXml.Descendants("TreeItem").Count(e => (string?)e.Attribute("Name") == "TimecodeShow");

            var sceneListsElement = timecodeShowsXml.Descendants("TreeItem")
                .Where(ti => (string?)ti.Attribute("Name") == "TimecodeShows")
                .First();

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

            sceneListsElement.Add(timecodeShowElement);

            var sceneListXmlMs = new MemoryStream();
            timecodeShowsXml.Save(sceneListXmlMs);

            sceneListXmlMs.Seek(0, SeekOrigin.Begin);
            timecodeShowsFile.FileStream = sceneListXmlMs;
        }

        private static void UpdateSceneLists(DmzContainer container, Timeshow timeshow)
        {
            var sceneListsFiles = container.Files.Where(f => f.FileName.StartsWith("Config/SceneLists") && f.FileName.EndsWith(".xml")).OrderByDescending(s => s.FileName).ToList();

            List<string> sceneIds = new List<string>();

            foreach (var file in sceneListsFiles)
            {
                file.FileStream.Seek(0, SeekOrigin.Begin);

                var sceneListsXml = XDocument.Load(file.FileStream);

                var sceneListsElement = sceneListsXml.Descendants("TreeItem")
                    .Where(ti => (string?)ti.Attribute("Name") == "SceneLists")
                    .First();

                var fsceneIds = sceneListsElement.Elements("TreeItem").Where(ti => (string?)ti.Attribute("Name") == "SceneList").Select(ti => (string?)ti.Elements("Attribute").First(a => (string?)a.Attribute("Name") == "ID").Attribute("Value"));
                sceneIds.AddRange(fsceneIds.Where(id => !string.IsNullOrEmpty(id))!);
            }

            var lastfile = sceneListsFiles.Last();

            lastfile.FileStream.Seek(0, SeekOrigin.Begin);

            var lastsceneListsXml = XDocument.Load(lastfile.FileStream);

            var lastsceneListsElement = lastsceneListsXml.Descendants("TreeItem")
                .Where(ti => (string?)ti.Attribute("Name") == "SceneLists")
                .First();

            foreach (var sceneList in timeshow.SceneLists)
            {
                if (sceneIds.Contains(sceneList.Id.ToString()))
                {
                    Console.WriteLine($"SceneList with ID {sceneList.Id} already exists, skipping.");
                    continue; // Skip if the scene list already exists
                }

                var newSceneListElement = XElement.Parse(sceneList.Xml);

                var numberAttr = newSceneListElement.Elements("Attribute").First(x => (string?)x.Attribute("Name") == "Number");
                numberAttr.SetAttributeValue("Value", sceneIds.Count + 1);

                var indexAttr = newSceneListElement.Elements("Attribute").First(x => (string?)x.Attribute("Name") == "ZZ_SAVE_INDEX");
                indexAttr.SetAttributeValue("Value", sceneIds.Count);

                lastsceneListsElement.Add(newSceneListElement);

                sceneIds.Add(sceneList.Id.ToString());
            }

            var sceneListXmlMs = new MemoryStream();
            lastsceneListsXml.Save(sceneListXmlMs);

            sceneListXmlMs.Seek(0, SeekOrigin.Begin);
            lastfile.FileStream = sceneListXmlMs;
        }

        private static void UpdateProjectExplorer(DmzContainer container, Timeshow timeshow)
        {
            var projectExplorerFile = container.Files.First(f => f.FileName.Contains("Config/ProjectExplorer.xml"));

            projectExplorerFile.FileStream.Seek(0, SeekOrigin.Begin);

            var projectExplorerXml = XDocument.Load(projectExplorerFile.FileStream);

            var ceueListsElement = projectExplorerXml.Descendants("TreeItem")
                .Where(ti => (string?)ti.Attribute("Name") == "Branch")
                .First(ti =>
                        ti.Elements("Attribute").Any(attr =>
                            (string?)attr.Attribute("Name") == "ID" &&
                            (string?)attr.Attribute("Value") == "Cuelists"
                        )
                    );

            int cueueListCount = ceueListsElement.Descendants("TreeItem").Count(e => (string?)e.Attribute("Name") == "Node");

            foreach (var sceneList in timeshow.SceneLists)
            {
                XElement treeItem = new XElement("TreeItem",
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
                    new XAttribute("Value", cueueListCount)
                ));

                ceueListsElement.Add(treeItem);

                cueueListCount++;
            }

            var timeCodeShowsElement = projectExplorerXml.Descendants("TreeItem")
                .Where(ti => (string?)ti.Attribute("Name") == "Branch")
                .First(ti =>
                        ti.Elements("Attribute").Any(attr =>
                            (string?)attr.Attribute("Name") == "ID" &&
                            (string?)attr.Attribute("Value") == "TimecodeShows"
                        )
                    );

            int timeCodeShowsCount = timeCodeShowsElement.Elements("TreeItem").Count(e => (string?)e.Attribute("Name") == "Node");

            var xelem = XElement.Parse(timeshow.ProjectExplorerXml);

            var indexAttribute = xelem.Elements("Attribute").First(x => (string?)x.Attribute("Name") == "Index");

            indexAttribute.SetAttributeValue("Value", timeCodeShowsCount);

            timeCodeShowsElement.Add(xelem);

            var filesElement = projectExplorerXml.Descendants("TreeItem")
                .Where(ti => (string?)ti.Attribute("Name") == "Branch")
                .First(ti =>
                        ti.Elements("Attribute").Any(attr =>
                            (string?)attr.Attribute("Name") == "ID" &&
                            (string?)attr.Attribute("Value") == "Files"
                        )
                    );

            int fileCount = filesElement.Elements("TreeItem").Count(e => (string?)e.Attribute("Name") == "Node");

            foreach (var file in timeshow.Files)
            {
                XElement treeItem = new XElement("TreeItem",
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
            using var ms = new MemoryStream();
            timecodeXmlFile.FileStream.CopyTo(ms);
            ms.Seek(0, SeekOrigin.Begin);

            using StreamReader reader = new StreamReader(ms);
            var xmlContent = reader.ReadToEnd();
            return xmlContent;
        }
    }
}
