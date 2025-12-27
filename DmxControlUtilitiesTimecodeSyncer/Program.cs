using DmxControlUtilitiesTimecodeSyncer;
using Grpc.Core;       // generated messages (package LumosProtobuf)
using Grpc.Net.Client;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository;
using log4net.Repository.Hierarchy;
using LumosProtobuf;
using LumosProtobuf.ConnectionClient;
using LumosProtobuf.Udp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UmbraClient;
using UmbraKernel;



static void ConfigureLogging()
{
    // Get the repository for the current assembly
    ILoggerRepository repository =
        LogManager.GetRepository(typeof(Program).Assembly);

    // Create layout
    var layout = new PatternLayout
    {
        ConversionPattern = "%date %-5level %logger - %message%newline"
    };
    layout.ActivateOptions();

    // Create console appender
    var consoleAppender = new ConsoleAppender
    {
        Layout = layout,
        Threshold = Level.All
    };
    consoleAppender.ActivateOptions();

    var repo = (Hierarchy)LogManager.GetRepository();
    repo.Root.Level = Level.All;
    repo.Root.AddAppender(consoleAppender);
    repo.Configured = true;


    // Mark repository as configured
    repository.Configured = true;
}

ConfigureLogging();



var services = new ServiceCollection();

services.AddSingleton<DiscoveryService>();

using var serviceProvider = services.BuildServiceProvider();

var discoveryService = serviceProvider.GetRequiredService<DiscoveryService>();

discoveryService.StartDiscovery();

var eventManager = new EventManager();

_ = Task.Run(async () =>
{
    while (true)
    {
        foreach (var endpoint in discoveryService.Endpoints)
        {
            if (eventManager.Instances.Exists(i => i.IPEndPoint == endpoint))
            {
                continue;
            }

            var instance = new DmxControlInstance(endpoint);
            await instance.Init();

            eventManager.RegisterInstance(instance);

            Console.WriteLine($"Initialized instance at {endpoint}");
        }

        await Task.Delay(5000);
    }
});


_ = Task.Run(async () =>
{



    /*


    var timecodeClient = new TimecodeClient.TimecodeClientClient(channel);

    // Build request. Populate filters or paging if available on GetMultipleRequest.
    var request = new GetMultipleRequest
    {
        UserContextId = userContextId
    };



    _ = Task.Run(async () =>
      {
          var receiveCall = timecodeClient.ReceiveTimecodeStateChanges(new GetRequest
          {
              RequestId = Guid.NewGuid().ToString(),
          }, connectionClientData.HostMetadata);

          await foreach (var timecodeState in receiveCall.ResponseStream.ReadAllAsync())
          {
              Console.WriteLine("Received timecode state change:");
              Console.WriteLine(timecodeState.ToString());
          }
      });

    var timecodesResponse = await timecodeClient.GetTimecodesAsync(request, connectionClientData.HostMetadata);
    var first = timecodesResponse.Timecodes.FirstOrDefault();

    var response2 = await timecodeClient.TimecodeActionAsync(new LumosProtobuf.Timecode.TimecodeActionRequest
    {
        Action = LumosProtobuf.Timecode.ETimecodeAction.Stop,
        TimecodeId = first.Id,
    }, connectionClientData.HostMetadata);


    Console.WriteLine("done");
    */




});



Console.ReadLine();
