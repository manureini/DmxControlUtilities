



using DmxControlUtilities.Models;
using DmxControlUtilities.Services;

var dmzFileService = new DmzFileService();
var szeneListService = new SzeneListService();

var source = dmzFileService.ReadDmzFile(File.OpenRead("H:\\Nextcloud\\DMXControl\\0.0.159.dmz.zip"), "source.dmz");
var dest = dmzFileService.ReadDmzFile(File.OpenRead("H:\\Nextcloud\\DMXControl\\ESG_SS25_01.dmz.zip"), "dest.dmz");

var timeshowService = new TimeshowService(szeneListService);


var tesfy = szeneListService.GetSceneLists(source);


var ts = timeshowService.ExtractTimeshow(source, new TimeshowMeta
{
    Id = Guid.Parse("5b037e3b-7dc1-4300-8a4c-b4b167fdb412"),
    Name = "New Ts Show"
});


var destContainer = timeshowService.AddTimeshow(dest, ts);

var ms = new MemoryStream();
dmzFileService.WriteDmzFile(destContainer, ms);


File.WriteAllBytes("H:\\Nextcloud\\DMXControl\\ESG_SS25_01_new.dmz.zip", ms.ToArray());