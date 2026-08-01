
using DmxControlUtilities.Lib.Models;
using DmxControlUtilities.Lib.Services;

var dmzFileService = new DmzFileService();
var szeneListService = new SzeneListService();

var source = dmzFileService.ReadDmzFile(File.OpenRead(@"C:\Users\Manuel\Downloads\burni.dmz"), "source.dmz");
var dest = dmzFileService.ReadDmzFile(File.OpenRead(@"C:\Users\Manuel\Downloads\merged6.dmz"), "dest.dmz");

var timeshowService = new TimeshowService(szeneListService);

var tesfy = szeneListService.GetSceneLists(source);

var ts = timeshowService.ExtractTimeshow(source, new TimeshowMeta
{
    Id = Guid.Parse("def5a658-bd1c-4ce6-9b82-8c8deff4315a"),
    Name = "F2-Formation"
});


var destContainer = timeshowService.AddTimeshow(dest, ts);

var ms = new MemoryStream();
dmzFileService.WriteDmzFile(destContainer, ms);


File.WriteAllBytes("H:\\Nextcloud\\DMXControl\\merged7.zip", ms.ToArray());