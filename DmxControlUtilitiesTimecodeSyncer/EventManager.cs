using System;
using System.Collections.Generic;
using System.Text;

namespace DmxControlUtilitiesTimecodeSyncer
{
    public class EventManager
    {
        public List<DmxControlInstance> Instances = new();

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

        public void StopAllTimecodeShow(string name)
        {
            foreach (var instance in Instances)
            {
                _ = instance.StopTimecodeShow(name);
            }
        }
    }
}
