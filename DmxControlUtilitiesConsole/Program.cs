
using DmxControlUtilities.Lib.Models;
using DmxControlUtilities.Lib.Services;

var dmzFileService = new DmzFileService();
var szeneListService = new SzeneListService();

var source = dmzFileService.ReadDmzFile(File.OpenRead(@"C:\Users\Manuel\Downloads\vianna.dmz"), "source.dmz");
var dest = dmzFileService.ReadDmzFile(File.OpenRead(@"C:\Users\Manuel\Downloads\burni.dmz"), "dest.dmz");

var timeshowService = new TimeshowService(szeneListService);

var tesfy = szeneListService.GetSceneLists(source);

var ts = timeshowService.ExtractTimeshow(source, new TimeshowMeta
{
    Id = Guid.Parse("a7a3234a-bbc4-48d2-b5ff-7dfc56f299b4"),
    Name = "F1-Formation"
});

var destContainer = timeshowService.AddTimeshow(dest, ts);

var ms = new MemoryStream();
dmzFileService.WriteDmzFile(destContainer, ms);

File.WriteAllBytes("H:\\Nextcloud\\DMXControl\\merged_test.zip", ms.ToArray());