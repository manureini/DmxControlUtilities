
using DmxControlUtilities.Lib.Models;
using DmxControlUtilities.Lib.Services;

var dmzFileService = new DmzFileService();
var szeneListService = new SzeneListService();

var source = dmzFileService.ReadDmzFile(File.OpenRead("C:\\Users\\Manuel\\Downloads\\0.0.50.dmz"), "source.dmz");
var dest = dmzFileService.ReadDmzFile(File.OpenRead("C:\\Users\\Manuel\\Downloads\\0.0.53.dmz"), "dest.dmz");

var timeshowService = new TimeshowService(szeneListService);


var tesfy = szeneListService.GetSceneLists(source);


var ts = timeshowService.ExtractTimeshow(source, new TimeshowMeta
{
    Id = Guid.Parse("d440346c-613d-4328-8cee-5c2b2bb470e2"),
    Name = "F3"
});


var destContainer = timeshowService.AddTimeshow(dest, ts);

var ms = new MemoryStream();
dmzFileService.WriteDmzFile(destContainer, ms);


File.WriteAllBytes("H:\\Nextcloud\\DMXControl\\ESG_SS25_01_new.dmz.zip", ms.ToArray());