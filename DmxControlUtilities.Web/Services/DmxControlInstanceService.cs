using System;
using System.Collections.Generic;
using System.Text;

namespace DmxControlUtilities.Web.Services
{
    public class DmxControlInstanceService
    {
        public List<DmxControlInstance> Instances = new();

        protected DiscoveryService _discoveryService;

        public DmxControlInstanceService(DiscoveryService discoveryService)
        {
            _discoveryService = discoveryService;
        }


        public void RegisterInstance(DmxControlInstance instance)
        {
            instance.EventManager = this;
            Instances.Add(instance);
        }

        public void StartAllTimecodeShow(string name)
        {
            foreach (var instance in Instances)
            {
                _ = instance.StartTimecodeShow(name);
            }
        }

        public void StopAllTimecodeShows()
        {
            foreach (var instance in Instances)
            {
                _ = instance.StopAllTimecodeShows();
            }
        }

        public void RemoveInstance(DmxControlInstance instance)
        {
            Instances.Remove(instance);
            _discoveryService.Endpoints.Remove(instance.IPEndPoint);
        }
    }
}
