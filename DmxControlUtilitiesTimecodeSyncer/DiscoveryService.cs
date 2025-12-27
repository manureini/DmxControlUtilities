using LumosProtobuf.Udp;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace DmxControlUtilitiesTimecodeSyncer
{
    internal class DiscoveryService
    {
        protected UmbraDiscoveryClient _discoveryClient = new();

        public HashSet<IPEndPoint> Endpoints { get; init; } = new();

        public DiscoveryService()
        {
            _discoveryClient.UmbraDiscoveryBroadcastReceived += DiscoveryClient_UmbraDiscoveryBroadcastReceived;
        }

        public void StartDiscovery()
        {
            _discoveryClient.StartDiscovery();
        }

        private void DiscoveryClient_UmbraDiscoveryBroadcastReceived(object? sender, UmbraDiscoveryBroadcastEventArgs e)
        {
            var umbraEndpoint = new IPEndPoint(IPAddress.Parse(e.Broadcast.UmbraServer.ClientInfo.Ips.First()), e.Broadcast.UmbraServer.ClientInfo.UmbraPort);

            Console.WriteLine($"Found {umbraEndpoint}");
            Endpoints.Add(umbraEndpoint);
        }
    }
}
