using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DmxControlUtilities.Lib.Services.Hal;

public enum FeatureType
{
    // Color
    Color,

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


