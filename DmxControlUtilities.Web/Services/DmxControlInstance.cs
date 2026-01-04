using DmxControlUtilities.Web.Models;
using Grpc.Core;
using Grpc.Net.Client;
using log4net;
using LumosProtobuf;
using LumosProtobuf.ConnectionClient;
using LumosProtobuf.Timecode;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using UmbraClient;
using static UmbraClient.TimecodeClient;
using static UmbraClient.ProjectClient;

namespace DmxControlUtilities.Web.Services
{
    public class DmxControlInstance
    {
        public IPEndPoint IPEndPoint { get; init; }

        public string SessionId { get; set; }
        public string UserContextId { get; set; }

        public bool IsInitialized { get; set; }
        public string LoadedProjectName { get; set; }

        public List<TimecodeDescriptor> TimecodeShows { get; set; } = new();

        public List<string> RunningTimecodeShows { get; set; } = new();

        protected TimecodeClientClient _timecodeClientClient;
        protected ProjectClientClient _projectClientClient;


        protected Metadata _connectionClientDataHostMetadata;

        public DmxControlInstanceService EventManager { get; set; }

        public DmxControlInstance(IPEndPoint endpoint)
        {
            IPEndPoint = endpoint;
        }

        public async Task Init()
        {
            if (IsInitialized)
                return;

            string baseAddress = "http://" + IPEndPoint;

            var channel = GrpcChannel.ForAddress(baseAddress);

            var client = new UmbraConnectionClient();
            client.Logger = LogManager.GetLogger("UmbraConnectionClient");

            var connectionClientData = new ConnectionClientData()
            {
                Clientname = "DMXControl Utilities Timecode Syncer",
                ClientProgramInfo = new ClientProgramInfo()
                {
                    ClientInfo = new ClientInfo()
                    {
                        Clientname = "DMXControl Utilities Timecode Syncer",
                        Hostname = "127.0.0.1",
                        Runtimeid = Guid.NewGuid().ToString(),
                        Type = EClientType.ExternalTool
                    }
                },
                UmbraChannel = channel,
            };

            client.DataProvider = connectionClientData;

            var response = await client.connectAsync(false);

            SessionId = client.SessionID;

            connectionClientData.HostMetadata = new Metadata()
            {
                { "SessionID", client.SessionID },
            };

            _connectionClientDataHostMetadata = connectionClientData.HostMetadata;

            client.OpenConnectionsAfterConnect(CancellationToken.None);

            Console.WriteLine("Connected to Umbra Server at " + IPEndPoint.ToString());

            var userClient = new UserClient.UserClientClient(channel);

            var getUserContextCall = userClient.BindAsync(new LumosProtobuf.User.UserContextRequest()
            {
                Username = "Tool",
                PasswordHash = "123",

            }, connectionClientData.HostMetadata);

            var userContextResponse = await getUserContextCall.ResponseAsync;

            UserContextId = userContextResponse.UserContextId;

            IsInitialized = true;

            var request = new GetMultipleRequest
            {
                UserContextId = UserContextId
            };

            _projectClientClient = new ProjectClientClient(channel);

            //     var state = await _projectClientClient.GetProjectsAsync(request);

            _timecodeClientClient = new TimecodeClientClient(channel);

            await UpdateTimecodeshows();

            /*
            _ = Task.Run(async () =>
            {
                var receiveCall = _timecodeClientClient.ReceiveTimecodeStateChanges(new GetRequest
                {
                    RequestId = Guid.NewGuid().ToString(),
                }, connectionClientData.HostMetadata);

                await foreach (var timecodeState in receiveCall.ResponseStream.ReadAllAsync())
                {
                    Console.WriteLine("Received timecode state change:");
                    Console.WriteLine(timecodeState.ToString());

                    var name = TimecodeShows.FirstOrDefault(t => t.Id == timecodeState.Id)?.Name;

                    if (name != null)
                    {
                        if (timecodeState.State == 8 && timecodeState.TimeElapsed == 0)
                        {
                            RunningTimecodeShows.Add(name);
                            EventManager.StartAllTimecodeShow(name);
                        }
                        else if (timecodeState.State == 66)
                        {
                            RunningTimecodeShows.Clear();
                            EventManager.StopAllTimecodeShows();
                        }
                    }
                }
            });
            */
        }

        public async Task UpdateTimecodeshows()
        {
            var request = new GetMultipleRequest
            {
                UserContextId = UserContextId
            };

            var result = await _timecodeClientClient.GetTimecodesAsync(request, _connectionClientDataHostMetadata);

            TimecodeShows = result.Timecodes.ToList();
        }

        public async Task StartTimecodeShow(string name)
        {
            var ts = TimecodeShows.FirstOrDefault(t => t.Name == name);

            if (ts == null)
                return;

            if (RunningTimecodeShows.Contains(name))
                return;

            RunningTimecodeShows.Add(name);

            var response2 = await _timecodeClientClient.TimecodeActionAsync(new LumosProtobuf.Timecode.TimecodeActionRequest
            {
                Action = ETimecodeAction.Play,
                TimecodeId = ts.Id
            }, _connectionClientDataHostMetadata);
        }

        public async Task StopAllTimecodeShows()
        {
            foreach (var ts in TimecodeShows)
            {
                var response2 = await _timecodeClientClient.TimecodeActionAsync(new LumosProtobuf.Timecode.TimecodeActionRequest
                {
                    Action = ETimecodeAction.Stop,
                    TimecodeId = ts.Id
                }, _connectionClientDataHostMetadata);
            }

            RunningTimecodeShows.Clear();
        }

        public async Task StopTimecodeShow(string name)
        {
            var ts = TimecodeShows.FirstOrDefault(t => t.Name == name);

            if (ts == null)
                return;

            RunningTimecodeShows.Remove(name);

            var response2 = await _timecodeClientClient.TimecodeActionAsync(new LumosProtobuf.Timecode.TimecodeActionRequest
            {
                Action = ETimecodeAction.Stop,
                TimecodeId = ts.Id
            }, _connectionClientDataHostMetadata);
        }
    }
}
