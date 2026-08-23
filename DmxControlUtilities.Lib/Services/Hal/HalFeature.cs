using DmxControlUtilities.Lib.Models;
using DmxControlUtilities.Lib.Models.Ddf;

namespace DmxControlUtilities.Lib.Services.Hal
{
    /// <summary>
    /// A logical, hardware-independent capability of a device (e.g. Color, Dimmer, Pan, Tilt),
    /// typed by <see cref="FeatureType"/>. Higher-resolution DDF channels (fine/ultra) are
    /// collapsed into the coarse feature - they are never exposed separately.
    /// <para>
    /// Values are normalized to 0..1. Setting a feature distributes the value onto the underlying
    /// DDF channels (coarse + fine bits) through the typed <see cref="HalService"/> setters, so
    /// callers never touch DDF internals (keys, DMX offsets, resolutions).
    /// </para>
    /// </summary>
    public abstract class HalFeature
    {
        internal readonly HalService mHal;
        internal readonly Device mDevice;

        /// <summary>
        /// The kind of feature, derived from the DDF element it originates from.
        /// </summary>
        public FeatureType Type { get; }

        /// <summary>
        /// Display name distinguishing multiple features of the same <see cref="Type"/>,
        /// e.g. "Red"/"Green"/"Blue" for <see cref="FeatureType.Rgb"/>,
        /// "Pan"/"Tilt" for <see cref="FeatureType.Position"/>.
        /// </summary>
        public string Name { get; }

        protected HalFeature(HalService pHal, Device pDevice, FeatureType pType, string pName)
        {
            mHal = pHal;
            mDevice = pDevice;
            Type = pType;
            Name = pName;
        }

        /// <summary>
        /// Current value normalized to 0..1 (combined coarse+fine for high-resolution features).
        /// </summary>
        public abstract double GetValue();

        /// <summary>
        /// Sets the value normalized to 0..1, distributing it onto the underlying DDF channels.
        /// </summary>
        public abstract void SetValue(double pValue);

        /// <summary>
        /// Discrete steps of this feature (e.g. colorwheel/gobowheel/rawstep). Empty for continuous features.
        /// Exposed as (MinValue 0..1, MaxValue 0..1, Caption) so callers don't need DDF types.
        /// </summary>
        public virtual IReadOnlyList<HalFeatureStep> Steps => Array.Empty<HalFeatureStep>();
    }

    /// <summary>
    /// A discrete step of a <see cref="HalFeature"/>, with the DMX range normalized to 0..1.
    /// </summary>
    public readonly record struct HalFeatureStep(double MinValue, double MaxValue, string Caption)
    {
        public bool Contains(double pValue) => pValue >= MinValue && pValue <= MaxValue;
    }
}
