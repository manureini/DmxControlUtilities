using DmxControlUtilities.Lib.Models;

namespace DmxControlUtilities.Lib.Services
{
    /// <summary>
    /// Represents one staged HAL feature value in the programmer for display in tables.
    /// Composite features (Color, Position) are a single entry.
    /// </summary>
    public sealed class ProgrammerValueEntry
    {
        public Guid DeviceId { get; init; }
        public string DeviceName { get; init; } = string.Empty;

        /// <summary>
        /// Feature key (DDF group key, e.g. "rgb", "dimmer", "position").
        /// </summary>
        public string Feature { get; init; } = string.Empty;

        /// <summary>
        /// Display name of the feature (e.g. "Color", "Dimmer").
        /// </summary>
        public string FeatureName { get; init; } = string.Empty;

        public CueFeatureValueKind Kind { get; init; }

        /// <summary>
        /// Human-readable value (e.g. "#ff8000", "50% / 25%", "75%").
        /// </summary>
        public string DisplayValue { get; init; } = string.Empty;
    }
}
