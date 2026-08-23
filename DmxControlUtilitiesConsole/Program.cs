
using DmxControlUtilities.Lib.Models;
using DmxControlUtilities.Lib.Services;

var dmzFileService = new DmzFileService();
var szeneListService = new SzeneListService();

var source = dmzFileService.ReadDmzFile(File.OpenRead(@"C:\Users\Manuel\Downloads\jonas2.dmz"), "source.dmz");
var dest = dmzFileService.ReadDmzFile(File.OpenRead(@"C:\Users\Manuel\Downloads\hannah.dmz"), "dest.dmz");

var timeshowService = new TimeshowService(szeneListService);

var tesfy = szeneListService.GetSceneLists(source);

var ts = timeshowService.ExtractTimeshow(source, Guid.Parse("ec12e969-707c-4536-9163-c8524c71e041"));

var destContainer = timeshowService.AddTimeshow(dest, ts);

var ms = new MemoryStream();
dmzFileService.WriteDmzFile(destContainer, ms);

File.WriteAllBytes("H:\\Nextcloud\\DMXControl\\merged_test3.zip", ms.ToArray());