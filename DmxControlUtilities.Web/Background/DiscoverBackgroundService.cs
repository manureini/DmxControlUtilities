
using DmxControlUtilities.Web.Services;

namespace DmxControlUtilities.Web.Background
{
    public class DiscoverBackgroundService : BackgroundService
    {
        protected readonly DiscoveryService _discoveryService;
        protected readonly DmxControlInstanceService _instanceService;

        public DiscoverBackgroundService(DiscoveryService discoveryService, DmxControlInstanceService instanceService)
        {
            _discoveryService = discoveryService;
            _instanceService = instanceService;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _discoveryService.StartDiscovery();

            _ = Task.Run(async () =>
            {
                while (true)
                {
                    foreach (var endpoint in _discoveryService.Endpoints)
                    {
                        var existing = _instanceService.Instances.FirstOrDefault(i => i.IPEndPoint == endpoint);
                        if (existing != null)
                        {
                            await Update(existing);
                            continue;
                        }

                        var instance = new DmxControlInstance(endpoint);
                        await instance.Init();

                        _instanceService.RegisterInstance(instance);

                        Console.WriteLine($"Initialized instance at {endpoint}");
                    }

                    await Task.Delay(3000);
                }
            });

            return Task.CompletedTask;
        }

        protected async Task Update(DmxControlInstance existing)
        {
            if (existing.RunningTimecodeShows.Any())
                return;

            await existing.UpdateTimecodeshows();
        }
    }
}
