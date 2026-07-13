using DmxControlUtilities.Web.Background;
using DmxControlUtilities.Web.Components;
using DmxControlUtilities.Web.Models;
using DmxControlUtilities.Web.Services;
using MComponents;
using System.Net;
using System.Net.Sockets;
using System.Text;

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

            builder.Services.AddSingleton<DiscoveryService>();
            builder.Services.AddSingleton<DmxControlInstanceService>();
            builder.Services.AddSingleton<FixtureService>();
            builder.Services.AddSingleton<AudioVolumeService>();
            builder.Services.AddSingleton<TrackerLocationService>();

            builder.Services.AddHostedService<DiscoverBackgroundService>();

            builder.Services.AddMComponents(o =>
            {

                o.RegisterStringLocalizer = true;
                o.RegisterResourceLocalizer = true;
                o.RegisterNavigation = true;

            });

            var app = builder.Build();


            _ = Task.Run(() =>
            {

                const int port = 12345;

                using var udpClient = new UdpClient(port);

                Console.WriteLine($"Listening for UDP broadcasts on port {port}...");

                while (true)
                {
                    IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                    byte[] data = udpClient.Receive(ref remoteEndPoint);

                    string message = Encoding.UTF8.GetString(data);

                    Console.WriteLine($"Received from {remoteEndPoint.Address}:{remoteEndPoint.Port}: {message}");

                    // t1(0,0,0,76,0,0,0,0)

                    if (!message.Contains('(') || !message.Contains(')') || !message.Contains(',') || !message.StartsWith("t"))
                    {
                        continue;
                    }


                    string valuesPart = message.Substring(message.IndexOf('(') + 1).TrimEnd(')');

                    int[] values = valuesPart.Split(',')
                                             .Select(int.Parse)
                                             .Take(4)
                                             .ToArray();

                    var dist = new TrackerDistances()
                    {
                        Id = message[1] - '0',  
                        Anchor0 = values[0],
                        Anchor1 = values[1],
                        Anchor2 = values[2],
                        Anchor3 = values[3]
                    };

                    var trackerLocationService = app.Services.GetRequiredService<TrackerLocationService>();
                    trackerLocationService.UpdateDistance(dist);

                }

            });






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
