using DmxControlUtilities.Web.Background;
using DmxControlUtilities.Web.Components;
using DmxControlUtilities.Web.Options;
using DmxControlUtilities.Web.Services;
using MComponents;

namespace DmxControlUtilities.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // Configure Umbra connection options
            builder.Services.Configure<UmbraConnectionOptions>(
                builder.Configuration.GetSection(UmbraConnectionOptions.SectionName));

            builder.Services.AddSingleton<DiscoveryService>();
            builder.Services.AddSingleton<DmxControlInstanceService>();
            builder.Services.AddSingleton<FixtureService>();

            builder.Services.AddHostedService<DiscoverBackgroundService>();

            builder.Services.AddMComponents(o =>
            {

                o.RegisterStringLocalizer = true;
                o.RegisterResourceLocalizer = true;
                o.RegisterNavigation = true;

            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
