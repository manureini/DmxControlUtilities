using DmxControlUtilities;
using DmxControlUtilities.Lib.Services;
using DmxControlUtilities.Models;
using DmxControlUtilities.Services;
using MComponents;
using MComponents.Files;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddSingleton<IFileUploadService, FileUploadService>();
builder.Services.AddSingleton<DmzFileService>();
builder.Services.AddSingleton<TimeshowService>();
builder.Services.AddSingleton<SzeneListService>();

builder.Services.AddMComponents(options =>
{
    options.RegisterResourceLocalizer = true;
    options.RegisterStringLocalizer = true;
    options.RegisterNavigation = true;
    options.RegisterTimezoneService = true;
});

await builder.Build().RunAsync();
