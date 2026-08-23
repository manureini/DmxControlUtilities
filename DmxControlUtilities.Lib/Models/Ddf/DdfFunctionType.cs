namespace DmxControlUtilities.Lib.Models.Ddf
{
    /// <summary>
    /// The type of a device function, derived from the DDF element it originates from.
    /// Custom/unknown elements are mapped to <see cref="Raw"/>.
    /// </summary>
    public enum DdfFunctionType
    {
        // Color mixing
        Rgb,
        Cmy,
        Hsv,

        // Intensity / beam
        Dimmer,
        Shutter,
        Strobe,
        Switch,

        // Position
        Position,

        // Color selection
        Colorwheel,
        Colortemp,

        // Gobo
        Gobowheel,

        // Optics
        Focus,
        Frost,
        Iris,
        Zoom,
        Prism,

        // Wheels / rotation
        Rotation,
        Index,

        // Matrix / radix
        Matrix,
        Radix,

        // Free / raw functions
        Raw,
        Rawstep,

        // Constant value
        Const,

        // Stage effects
        Fog,
        Fan,

        // Catch-all for any other known simple function
        Other,
    }
}
