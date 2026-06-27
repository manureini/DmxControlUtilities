
using DmxControlUtilities.Lib.Models;
using DmxControlUtilities.Lib.Services;

var dmzFileService = new DmzFileService();
var szeneListService = new SzeneListService();

var source = dmzFileService.ReadDmzFile(File.OpenRead(@"C:\Users\Manuel\Downloads\jonas.dmz"), "source.dmz");
var dest = dmzFileService.ReadDmzFile(File.OpenRead(@"C:\Users\Manuel\Downloads\hh.dmz"), "dest.dmz");

var timeshowService = new TimeshowService(szeneListService);

var tesfy = szeneListService.GetSceneLists(source);

var ts = timeshowService.ExtractTimeshow(source, new TimeshowMeta
{
    Id = Guid.Parse("0c3219f8-4f86-4fa5-aae6-b4bef17f1426"),
    Name = "TLK"
});


var destContainer = timeshowService.AddTimeshow(dest, ts);

var ms = new MemoryStream();
dmzFileService.WriteDmzFile(destContainer, ms);


File.WriteAllBytes("H:\\Nextcloud\\DMXControl\\ESG_WSSS2526_04.dmz.zip", ms.ToArray());