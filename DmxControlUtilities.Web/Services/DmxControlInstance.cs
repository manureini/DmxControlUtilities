using DmxControlUtilities.Web.Models;
using Grpc.Core;
using Grpc.Net.Client;
using log4net;
using LumosProtobuf;
using LumosProtobuf.ConnectionClient;
using LumosProtobuf.Timecode;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Text;
using UmbraClient;
using static UmbraClient.CuelistClient;
using static UmbraClient.DeviceClient;
using static UmbraClient.ParameterClient;
using static UmbraClient.PresetClient;
using static UmbraClient.ProgrammerClient;
using static UmbraClient.ProjectClient;
using static UmbraClient.TimecodeClient;

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
        public List<DeviceGroupDescriptor> DeviceGroups { get; set; } = new();
        public List<PresetModel>? PresetModels { get; set; }

        public List<string> RunningTimecodeShows { get; set; } = new();

        protected TimecodeClientClient _timecodeClientClient;
        protected ProjectClientClient _projectClientClient;
        protected ProgrammerClientClient _programmerClient;
        protected DeviceClientClient _deviceClientClient;
        protected PresetClientClient _presetClient;
        protected ParameterClientClient _parameterClient;
        protected CuelistClientClient _cuelistClient;

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
            _presetClient = new PresetClientClient(channel);
            _parameterClient = new ParameterClientClient(channel);
            _cuelistClient = new CuelistClientClient(channel);

            /*
            _ = Task.Run(async () =>
            {
                var receiveCall = _programmerClient.ReceiveProgrammerChanges(new GetRequest
                {
                    RequestId = Guid.NewGuid().ToString(),
                }, _connectionClientDataHostMetadata);

                await foreach (var change in receiveCall.ResponseStream.ReadAllAsync())
                {



                }
            });
            */


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

        public async Task UpdateGroups()
        {
            var request = new GetMultipleRequest
            {
                UserContextId = UserContextId
            };

            var result = await _deviceClientClient.GetDeviceGroupsAsync(request, _connectionClientDataHostMetadata);

            DeviceGroups = result.DeviceGroups.ToList();
        }

        public async Task<List<PresetModel>> GetPresets()
        {
            if (PresetModels != null)
                return PresetModels;

            var request = new GetMultipleRequest
            {
                UserContextId = UserContextId
            };

            var result = await _presetClient.GetPresetsAsync(request, _connectionClientDataHostMetadata);

            var models = new List<PresetModel>();

            await _programmerClient.SetProgrammerOutputAsync(new SetProgrammerOutputRequest()
            {
                Mode = EOutputMode.Hidden,
                UserContextId = UserContextId,
            }, _connectionClientDataHostMetadata);

            foreach (var preset in result.Presets)
            {
                var entry = preset.Entries.FirstOrDefault();

                if (entry == null)
                    continue;

                var resp = await _presetClient.EditPresetInProgrammerAsync(new EditPresetInProgrammerRequest()
                {
                    Blind = true,
                    PresetId = preset.PresetId,
                    RequestId = Guid.NewGuid().ToString(),
                    UserContextId = UserContextId,
                }, _connectionClientDataHostMetadata);

                var pState = await _programmerClient.GetProgrammerStateAsync(new CueStateRequest()
                {
                    UserContextId = UserContextId
                }, _connectionClientDataHostMetadata);

                var fannedValue = pState.GroupStates.FirstOrDefault()?.Fpv.FannedValues.FirstOrDefault(f => f.Color != null);

                if (fannedValue == null)
                    continue;

                var color = Color.FromArgb((int)(fannedValue.Color.VisualizationColor.R), (int)(fannedValue.Color.VisualizationColor.G), (int)(fannedValue.Color.VisualizationColor.B));

                models.Add(new PresetModel()
                {
                    Name = preset.Name,
                    HtmlColor = ColorTranslator.ToHtml(color),
                    Color = color
                });
            }

            await _programmerClient.DeleteProgrammerValueAsync(new DeleteProgrammerValueRequest()
            {
                UserContextId = UserContextId,
                Mode = DeleteProgrammerValueRequest.Types.EClearMode.Clear
            }, _connectionClientDataHostMetadata);

            await _programmerClient.SetProgrammerOutputAsync(new SetProgrammerOutputRequest()
            {
                Mode = EOutputMode.All,
                UserContextId = UserContextId,
            }, _connectionClientDataHostMetadata);

            PresetModels = models;

            return PresetModels;
        }

        public async Task ClearProgrammer()
        {
            await _programmerClient.DeleteProgrammerValueAsync(new DeleteProgrammerValueRequest()
            {
                UserContextId = UserContextId,
                Mode = DeleteProgrammerValueRequest.Types.EClearMode.Clear
            }, _connectionClientDataHostMetadata);
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
                /*
                var tempDeviceGroup = await _deviceClientClient.GetTemporaryDeviceGroupAsync(new GetTemporaryDeviceGroupRequest()
                {
                    DeviceAndGroupIDs = { device.Id },
                    RequestId = Guid.NewGuid().ToString(),
                    UserContextId = UserContextId,
                }, _connectionClientDataHostMetadata);
                */

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
            var tempDeviceGroup = await _deviceClientClient.GetTemporaryDeviceGroupAsync(new GetTemporaryDeviceGroupRequest()
            {
                DeviceAndGroupIDs = { fixtureId },
                RequestId = Guid.NewGuid().ToString(),
                UserContextId = UserContextId,
            }, _connectionClientDataHostMetadata);

            var request = new GetMultipleRequest()
            {
                RequestId = Guid.NewGuid().ToString(),
                UserContextId = UserContextId,
            };

            request.IdFilter.Add(tempDeviceGroup.Id);
                        
            var groups = await _deviceClientClient.GetDeviceGroupsAsync(request, _connectionClientDataHostMetadata);

            var firstProp = groups.DeviceGroups.First().Properties.First();

            var rest = await _deviceClientClient.GetDevicePropertyCurrentValueAsync(new DevicePropertyValueRequest()
            {
                DeviceOrGroupId = tempDeviceGroup.Id,
                PropertyId = firstProp.Id,
                Type = EValueType.CurrentPropertyvalue,
                UserContextId = UserContextId

            }, _connectionClientDataHostMetadata);

            var fannedValue = rest.PropertyValue.Fpv.FannedValues.First();

            fannedValue.Position.Pan = yaw;
            fannedValue.Position.Tilt = pitch;

            var respo = await _programmerClient.SetProgrammerValueAsync(new SetProgrammerValueRequest()
            {
                UserContextId = UserContextId,
                PropertyId = firstProp.Id,

          //      GroupHandling = EGroupHandling.ConcatGroups,
                GroupId = tempDeviceGroup.Id,
                Dpv = new DevicePropertyValue()
                {
                    Position = fannedValue.Position
                }
            }, _connectionClientDataHostMetadata);
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


        public async Task SetProgrammerColor(string groupId, Color color)
        {
            var request5 = new GetMultipleRequest()
            {
                RequestId = Guid.NewGuid().ToString(),
                UserContextId = UserContextId,
            };

            request5.IdFilter.Add(groupId);

            var groups = await _deviceClientClient.GetDeviceGroupsAsync(request5, _connectionClientDataHostMetadata);

            var firstProp = groups.DeviceGroups.First().Properties.First(c => c.Name == "Color");

            var rest = await _deviceClientClient.GetDevicePropertyCurrentValueAsync(new DevicePropertyValueRequest()
            {
                DeviceOrGroupId = groupId,
                PropertyId = firstProp.Id,
                Type = EValueType.CurrentPropertyvalue,
                UserContextId = UserContextId

            }, _connectionClientDataHostMetadata);

            var fannedValue = rest.PropertyValue.Fpv.FannedValues.First();

            var fannedPropertyValue = new FannedPropertyValue()
            {
                FannedValues = {
                    new DevicePropertyValue{
                        Color = new LumosColorData()
                        {
                            Color = new ColorData()
                            {
                                A = color.A,
                                R =color.R,
                                G =color.G,
                                B = color.B
                            },

                            ColorSet = LumosColorData.Types.EColorSet.Color,
                        }
                    }
                },
                UiValueType = 3,
                PropertyTypeAQ = "LumosLIB.Kernel.Scene.Fanning.ColorFannedValue, LumosLIB, Version=3.3.2.0, Culture=neutral, PublicKeyToken=null"
            };

            var respo = await _programmerClient.SetProgrammerValueAsync(new SetProgrammerValueRequest()
            {
                UserContextId = UserContextId,
                PropertyId = firstProp.Id,
                GroupId = groupId,
                Fpv = fannedPropertyValue
            }, _connectionClientDataHostMetadata);
        }


        public async Task SetProgrammerDimmer(string groupId, int value)
        {
            var request5 = new GetMultipleRequest()
            {
                RequestId = Guid.NewGuid().ToString(),
                UserContextId = UserContextId,
            };

            request5.IdFilter.Add(groupId);

            var groups = await _deviceClientClient.GetDeviceGroupsAsync(request5, _connectionClientDataHostMetadata);

            var firstProp = groups.DeviceGroups.First().Properties.First(c => c.Name == "Dimmer");

            var rest = await _deviceClientClient.GetDevicePropertyCurrentValueAsync(new DevicePropertyValueRequest()
            {
                DeviceOrGroupId = groupId,
                PropertyId = firstProp.Id,
                Type = EValueType.CurrentPropertyvalue,
                UserContextId = UserContextId

            }, _connectionClientDataHostMetadata);

            var respo = await _programmerClient.SetProgrammerValueAsync(new SetProgrammerValueRequest()
            {
                UserContextId = UserContextId,
                PropertyId = firstProp.Id,
                GroupId = groupId,
                Dpv = new DevicePropertyValue()
                {
                    DoubleValue = value
                }
            }, _connectionClientDataHostMetadata);
        }




    }
}
