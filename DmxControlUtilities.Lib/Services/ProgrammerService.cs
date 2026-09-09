using DmxControlUtilities.Lib.Models;
using DmxControlUtilities.Lib.Models.Ddf;
using DmxControlUtilities.Lib.Services.Hal;

namespace DmxControlUtilities.Lib.Services
{
    /// <summary>
    /// Manages the DMXControl 3 Programmer: a central staging layer where logical HAL feature
    /// values (Color, Position, Dimmer, ...) are set, inspected, cleared, undone, and stored
    /// into Cues. Programmer output sits above cuelist playback and device controls.
    /// Raw DDF channels are never staged directly - edits always happen through features.
    /// </summary>
    public class ProgrammerService
    {
        private readonly DeviceService mDeviceService;
        private readonly HalService mHalService;
        private readonly HalService mIsolatedHalService;
        private readonly object mLock = new();

        // Staged programmer values: DeviceId -> (FeatureKey -> typed value)
        private Dictionary<Guid, Dictionary<string, CueFeatureValue>> mValues = new();

        // Last cleared snapshot for "Undo clear"
        private Dictionary<Guid, Dictionary<string, CueFeatureValue>>? mLastCleared;

        public event Action? Changed;

        public ProgrammerService(DeviceService pDeviceService, HalService pHalService)
        {
            mDeviceService = pDeviceService;
            mHalService = pHalService;
            mIsolatedHalService = pHalService.WithoutApply();
        }

        public bool HasValues
        {
            get
            {
                lock (mLock)
                {
                    return mValues.Count > 0 && mValues.Values.Any(v => v.Count > 0);
                }
            }
        }

        public bool CanUndoClear
        {
            get
            {
                lock (mLock)
                {
                    return mLastCleared != null && mLastCleared.Count > 0 && mLastCleared.Values.Any(v => v.Count > 0);
                }
            }
        }

        /// <summary>
        /// Number of staged feature values (for UI badges).
        /// </summary>
        public int ValueCount
        {
            get
            {
                lock (mLock)
                {
                    return mValues.Values.Sum(v => v.Count);
                }
            }
        }

        /// <summary>
        /// Returns a deep copy of all current programmer staged feature values.
        /// </summary>
        public Dictionary<Guid, Dictionary<string, CueFeatureValue>> GetValuesSnapshot()
        {
            lock (mLock)
            {
                return CloneValues(mValues);
            }
        }

        /// <summary>
        /// Returns one display entry per staged feature value for UI display.
        /// </summary>
        public IReadOnlyList<ProgrammerValueEntry> GetEntries()
        {
            lock (mLock)
            {
                var list = new List<ProgrammerValueEntry>();

                foreach (var (deviceId, values) in mValues)
                {
                    var device = mDeviceService.GetDevice(deviceId);
                    string devName = device?.Name ?? $"Device {deviceId}";

                    foreach (var (feature, val) in values)
                    {
                        list.Add(new ProgrammerValueEntry
                        {
                            DeviceId = deviceId,
                            DeviceName = devName,
                            Feature = feature,
                            FeatureName = val.Name,
                            Kind = val.Kind,
                            DisplayValue = val.ToDisplayString(),
                        });
                    }
                }

                return list;
            }
        }

        /// <summary>
        /// Returns an isolated staging Device representing the staged feature values of the given
        /// device merged over its base control values. Changes made to this device via the HAL
        /// features can be committed via <see cref="CommitStagingDevice"/>.
        /// </summary>
        public Device GetStagingDevice(Device pDevice)
        {
            lock (mLock)
            {
                var staging = new Device
                {
                    Id = pDevice.Id,
                    Name = pDevice.Name,
                    Channel = pDevice.Channel,
                    DescriptionId = pDevice.DescriptionId
                };

                // Seed with current device base values
                foreach (var (k, v) in pDevice.GetValuesSnapshot())
                {
                    staging.SetValue(k, v);
                }

                // Overlay existing programmer staged feature values if any
                if (mValues.TryGetValue(pDevice.Id, out var staged))
                {
                    foreach (var value in staged.Values)
                    {
                        value.Apply(staging, mIsolatedHalService);
                    }
                }

                return staging;
            }
        }

        /// <summary>
        /// Returns HAL features bound to an isolated staging device. Editing features modifies staging in-place.
        /// Call <see cref="CommitStagingDevice"/> to push changed values into the programmer layer.
        /// </summary>
        public IReadOnlyList<HalFeature> GetStagingFeatures(Device pStagingDevice)
        {
            return mIsolatedHalService.GetFeatures(pStagingDevice);
        }

        /// <summary>
        /// Sets a typed feature value for a device in the programmer (e.g. from a cue load or test).
        /// </summary>
        public void SetValue(Guid pDeviceId, CueFeatureValue pValue)
        {
            ArgumentNullException.ThrowIfNull(pValue);

            lock (mLock)
            {
                if (!mValues.TryGetValue(pDeviceId, out var devValues))
                {
                    devValues = new Dictionary<string, CueFeatureValue>();
                    mValues[pDeviceId] = devValues;
                }

                devValues[pValue.Feature] = pValue.Clone();
                ApplyToDeviceService();
            }

            RaiseChanged();
        }

        /// <summary>
        /// Compares the staging device's channel values against the device's base control values
        /// and stages one typed feature value per touched HAL feature.
        /// </summary>
        public void CommitStagingDevice(Device pStagingDevice, IEnumerable<string>? pTouchedKeys = null)
        {
            lock (mLock)
            {
                var baseDevice = mDeviceService.GetDevice(pStagingDevice.Id);
                if (baseDevice == null)
                    return;

                var description = mDeviceService.GetDescription(baseDevice);
                if (description == null)
                    return;

                var stagingSnapshot = pStagingDevice.GetValuesSnapshot();
                var baseSnapshot = baseDevice.GetValuesSnapshot();

                if (!mValues.TryGetValue(pStagingDevice.Id, out var devValues))
                {
                    devValues = new Dictionary<string, CueFeatureValue>();
                    mValues[pStagingDevice.Id] = devValues;
                }

                var features = mIsolatedHalService.GetFeatures(pStagingDevice);

                foreach (var feature in features)
                {
                    var keys = feature.GetKeys();

                    bool touched = pTouchedKeys != null
                        ? keys.Any(pTouchedKeys.Contains)
                        : keys.Any(k => stagingSnapshot.GetValueOrDefault(k) != baseSnapshot.GetValueOrDefault(k))
                            || (devValues.ContainsKey(feature.Feature)
                                && keys.Any(k => stagingSnapshot.GetValueOrDefault(k) != 0));

                    if (!touched)
                        continue;

                    var value = CueFeatureValue.FromStagedValues(description, feature, stagingSnapshot, baseSnapshot);

                    if (value != null)
                    {
                        devValues[feature.Feature] = value;
                    }
                }

                if (devValues.Count == 0)
                {
                    mValues.Remove(pStagingDevice.Id);
                }

                ApplyToDeviceService();
            }

            RaiseChanged();
        }

        /// <summary>
        /// Clears all values currently in the programmer and stores a snapshot in case of Undo.
        /// Programmer DMX output is released.
        /// </summary>
        public void Clear()
        {
            lock (mLock)
            {
                if (mValues.Count > 0 && mValues.Values.Any(v => v.Count > 0))
                {
                    mLastCleared = CloneValues(mValues);
                }

                mValues = new Dictionary<Guid, Dictionary<string, CueFeatureValue>>();
                ApplyToDeviceService();
            }

            RaiseChanged();
        }

        /// <summary>
        /// Restores the programmer contents from before the last <see cref="Clear"/>.
        /// </summary>
        public void UndoClear()
        {
            lock (mLock)
            {
                if (mLastCleared == null || mLastCleared.Count == 0)
                    return;

                mValues = CloneValues(mLastCleared);
                mLastCleared = null;
                ApplyToDeviceService();
            }

            RaiseChanged();
        }

        /// <summary>
        /// Deletes the staged programmer value of a single feature of a specific device.
        /// </summary>
        public void DeleteFeature(Guid pDeviceId, string pFeature)
        {
            lock (mLock)
            {
                if (mValues.TryGetValue(pDeviceId, out var devValues))
                {
                    if (devValues.Remove(pFeature))
                    {
                        if (devValues.Count == 0)
                            mValues.Remove(pDeviceId);

                        ApplyToDeviceService();
                    }
                }
            }

            RaiseChanged();
        }

        /// <summary>
        /// Deletes all staged programmer values for a specific device.
        /// </summary>
        public void DeleteDevice(Guid pDeviceId)
        {
            lock (mLock)
            {
                if (mValues.Remove(pDeviceId))
                {
                    ApplyToDeviceService();
                }
            }

            RaiseChanged();
        }

        /// <summary>
        /// Loads the feature values of an existing Cue into the Programmer, replacing whatever is
        /// currently in the Programmer. Per DMXControl 3 specification, "In Programmer bearbeiten"
        /// overwrites previous programmer contents.
        /// </summary>
        public void LoadCue(Cue pCue)
        {
            ArgumentNullException.ThrowIfNull(pCue);

            lock (mLock)
            {
                mLastCleared = null;
                mValues.Clear();

                foreach (var (devId, featureValues) in pCue.DeviceValues)
                {
                    var dev = mDeviceService.GetDevice(devId);
                    if (dev == null)
                        continue;

                    var copy = featureValues.ToDictionary(f => f.Key, f => f.Value.Clone());
                    if (copy.Count > 0)
                        mValues[devId] = copy;
                }

                ApplyToDeviceService();
            }

            RaiseChanged();
        }

        /// <summary>
        /// Creates a sparse cue value dictionary from the programmer.
        /// If <paramref name="pDeviceFilter"/> is provided, only values for the specified devices are included.
        /// Otherwise all programmer values are included.
        /// </summary>
        public Dictionary<Guid, Dictionary<string, CueFeatureValue>> CreateCueValues(IEnumerable<Guid>? pDeviceFilter = null)
        {
            lock (mLock)
            {
                HashSet<Guid>? filter = pDeviceFilter != null ? new HashSet<Guid>(pDeviceFilter) : null;
                var result = new Dictionary<Guid, Dictionary<string, CueFeatureValue>>();

                foreach (var (devId, vals) in mValues)
                {
                    if (filter != null && !filter.Contains(devId))
                        continue;

                    if (vals.Count > 0)
                    {
                        result[devId] = vals.ToDictionary(f => f.Key, f => f.Value.Clone());
                    }
                }

                return result;
            }
        }

        private void ApplyToDeviceService()
        {
            mDeviceService.SetProgrammerFeatures(mValues, mIsolatedHalService);
        }

        private void RaiseChanged()
        {
            try
            {
                Changed?.Invoke();
            }
            catch
            {
                // Subscriber safety
            }
        }

        private static Dictionary<Guid, Dictionary<string, CueFeatureValue>> CloneValues(Dictionary<Guid, Dictionary<string, CueFeatureValue>> pSource)
        {
            return pSource.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToDictionary(f => f.Key, f => f.Value.Clone())
            );
        }
    }
}
