using Grpc.Core;
using LumosProtobuf;
using LumosProtobuf.ConnectionClient;
using System;
using System.Collections.Generic;
using System.Text;

namespace DmxControlUtilities.Web.Models
{
    public class ConnectionClientData : IConnectionClientData
    {
        public ChannelBase UmbraChannel { get; set; }

        public Metadata HostMetadata {get; set;}

        public ClientProgramInfo ClientProgramInfo {get; set;}

        public string Clientname {get; set;}

        public string NetworkID {get; set;}

        public ChannelBase CreateChannel(string host, int port)
        {
            throw new NotImplementedException();
        }
    }
}
