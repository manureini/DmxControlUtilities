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
using static UmbraClient.ProgrammerClient;
using static UmbraClient.DeviceClient;

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
        protected ProgrammerClientClient _programmerClient;
        protected DeviceClientClient _deviceClientClient;

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

            }, _connectionClientDataHostMetadata);

            var userContextResponse = await getUserContextCall.ResponseAsync;

            UserContextId = userContextResponse.UserContextId;

            IsInitialized = true;

            var request = new GetMultipleRequest
            {
                UserContextId = UserContextId
            };

            _projectClientClient = new ProjectClientClient(channel);

            var state = await _projectClientClient.GetProjectsAsync(request, _connectionClientDataHostMetadata);

            _timecodeClientClient = new TimecodeClientClient(channel);
            _programmerClient = new ProgrammerClientClient(channel);
            _deviceClientClient = new DeviceClientClient(channel);


            _ = Task.Run(async () =>
            {




            });



            /*
            _ = Task.Run(async () =>
            {
                var receiveCall = _programmerClient.ReceiveProgrammerChanges(new GetRequest
                {
                    RequestId = Guid.NewGuid().ToString(),
                }, _connectionClientDataHostMetadata);

                await foreach (var change in receiveCall.ResponseStream.ReadAllAsync())
                {


                    var channel2 = GrpcChannel.ForAddress(baseAddress);

                    var deviceClient = new DeviceClientClient(channel2);

                    var devices = await deviceClient.GetDevicesAsync(new GetMultipleRequest()
                    {
                        RequestId = Guid.NewGuid().ToString(),
                        UserContextId = UserContextId,
                    }, _connectionClientDataHostMetadata);

                    var firstDevice = devices.Devices.First();

                    var tempDeviceGroup = await deviceClient.GetTemporaryDeviceGroupAsync(new GetTemporaryDeviceGroupRequest()
                    {
                        DeviceAndGroupIDs = { firstDevice.Id },
                        RequestId = Guid.NewGuid().ToString(),
                        UserContextId = UserContextId,
                    }, _connectionClientDataHostMetadata);

                    var request = new GetMultipleRequest()
                    {
                        RequestId = Guid.NewGuid().ToString(),
                        UserContextId = UserContextId,
                    };

                    request.IdFilter.Add(tempDeviceGroup.Id);

                    var groups = await deviceClient.GetDeviceGroupsAsync(request, _connectionClientDataHostMetadata);

                    var firstProp = groups.DeviceGroups.First().Properties.First();

                    var rest = await deviceClient.GetDevicePropertyCurrentValueAsync(new DevicePropertyValueRequest()
                    {
                        DeviceOrGroupId = tempDeviceGroup.Id,
                        PropertyId = firstProp.Id,
                        Type = EValueType.CurrentPropertyvalue,
                        UserContextId = UserContextId

                    }, _connectionClientDataHostMetadata);


                    var fannedValue = rest.PropertyValue.Fpv.FannedValues.First();






                }
            });

            */

            /*
            var state = await _programmerClient.GetProgrammerValue(new DevicePropertyValueRequest()
            {

            })
            */

            // await UpdateTimecodeshows();




            /*
            _ = Task.Run(async () =>
            {
                var receiveCall = _timecodeClientClient.ReceiveTimecodeStateChanges(new GetRequest
                {
                    RequestId = Guid.NewGuid().ToString(),
                }, _connectionClientDataHostMetadata);

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

        public async Task<List<Fixture>> GetDevices()
        {
            var devices = await _deviceClientClient.GetDevicesAsync(new GetMultipleRequest()
            {
                RequestId = Guid.NewGuid().ToString(),
                UserContextId = UserContextId,
            }, _connectionClientDataHostMetadata);

            List<Fixture> fixtures = new List<Fixture>();

            foreach (var device in devices.Devices)
            {
                var tempDeviceGroup = await _deviceClientClient.GetTemporaryDeviceGroupAsync(new GetTemporaryDeviceGroupRequest()
                {
                    DeviceAndGroupIDs = { device.Id },
                    RequestId = Guid.NewGuid().ToString(),
                    UserContextId = UserContextId,
                }, _connectionClientDataHostMetadata);

                var fixture = new Fixture()
                {
                    Id = device.Id,
                    Name = device.Name,
                };

                fixtures.Add(fixture);
            }

            return fixtures;
        }


        public async Task UpdateFixture(string fixtureId, float yaw, float pitch)
        {


           
 
            var request = new GetMultipleRequest()
            {
                RequestId = Guid.NewGuid().ToString(),
                UserContextId = UserContextId,
            };

            request.IdFilter.Add(fixtureId);

            var groups = await _deviceClientClient.GetDeviceGroupsAsync(request, _connectionClientDataHostMetadata);



            var firstProp = groups.DeviceGroups.First().Properties.First();

            var rest = await _deviceClientClient.GetDevicePropertyCurrentValueAsync(new DevicePropertyValueRequest()
            {
                DeviceOrGroupId = fixtureId,
                PropertyId = firstProp.Id,
                Type = EValueType.CurrentPropertyvalue,
                UserContextId = UserContextId

            }, _connectionClientDataHostMetadata);


            var fannedValue = rest.PropertyValue.Fpv.FannedValues.First();


            fannedValue.Position.Pan = yaw;
            fannedValue.Position.Tilt = 90 - MapAngleToMinus90To90(pitch);


            var respo = await _programmerClient.SetProgrammerValueAsync(new SetProgrammerValueRequest()
            {
                UserContextId = UserContextId,
                PropertyId = firstProp.Id,

                GroupHandling = EGroupHandling.ConcatGroups,
                GroupId = fixtureId,
                Dpv = new DevicePropertyValue()
                {
                    Position = fannedValue.Position
                }


            }, _connectionClientDataHostMetadata);



            /*
             * 
             * 




            var state = await _programmerClient.GetProgrammerValueAsync(new DevicePropertyValueRequest()
            {
                DeviceOrGroupId = tempDeviceGroup.Id,
                PropertyId = firstProp.Id,
                Type = EValueType.Programmer,
            }, _connectionClientDataHostMetadata);




            var rest2 = await _deviceClientClient.GetDevicePropertyCurrentValueAsync(new DevicePropertyValueRequest()
            {
                DeviceOrGroupId = tempDeviceGroup.Id,
                PropertyId = firstProp.Id,
                Type = EValueType.CurrentPropertyvalue,
                UserContextId = UserContextId

            }, _connectionClientDataHostMetadata);


            var fannedValue2 = rest2.PropertyValue.Fpv.FannedValues.First();
            */


        }

        double MapAngleToMinus90To90(double angle)
        {
            // 1. Normalisieren auf 0–360
            angle %= 360;
            if (angle < 0)
                angle += 360;

            // 2. In -180 .. 180 bringen
            if (angle > 180)
                angle -= 360;

            // 3. Spiegeln auf -90 .. 90
            if (angle > 90)
                angle = 180 - angle;
            else if (angle < -90)
                angle = -180 - angle;

            return angle;
        }





    }
}
