using DmxControlUtilities.Lib.Models.Ddf;
using System.Globalization;
using System.Xml.Linq;

namespace DmxControlUtilities.Lib.Services
{
    /// <summary>
    /// Parses DMXControl 3 device definition files (DDF, XML) into <see cref="DeviceDescription"/> objects.
    /// The parser is tolerant: element and attribute names are matched case-insensitively
    /// and unknown function elements are still exposed as generic channels.
    /// </summary>
    public static class DdfParser
    {
        // Maps all supported color channel names and aliases (XML abbreviations) to a canonical name.
        private static readonly Dictionary<string, string> mColorAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["red"] = "red", ["r"] = "red",
            ["green"] = "green", ["g"] = "green",
            ["blue"] = "blue", ["b"] = "blue",
            ["white"] = "white", ["w"] = "white",
            ["warmwhite"] = "warmwhite", ["ww"] = "warmwhite",
            ["naturalwhite"] = "naturalwhite", ["nw"] = "naturalwhite",
            ["coldwhite"] = "coldwhite", ["cw"] = "coldwhite",
            ["amber"] = "amber", ["a"] = "amber", ["do"] = "amber",
            ["uv"] = "uv",
            ["indigo"] = "indigo", ["i"] = "indigo", ["cb"] = "indigo",
            ["cyan"] = "cyan", ["c"] = "cyan",
            ["lime"] = "lime", ["l"] = "lime", ["lg"] = "lime",
            ["mint"] = "mint", ["mi"] = "mint",
            ["redorange"] = "redorange", ["ro"] = "redorange",
        };

        // Maps CMY channel names and aliases to a canonical name.
        private static readonly Dictionary<string, string> mCmyAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["cyan"] = "cyan", ["c"] = "cyan",
            ["magenta"] = "magenta", ["m"] = "magenta",
            ["yellow"] = "yellow", ["y"] = "yellow",
        };

        // Maps HSV channel names and aliases to a canonical name.
        private static readonly Dictionary<string, string> mHsvAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["hue"] = "hue", ["h"] = "hue",
            ["saturation"] = "saturation", ["s"] = "saturation",
            ["value"] = "value", ["v"] = "value",
        };

        // Simple functions which only consist of a dmxchannel attribute (plus optional ranges/steps).
        private static readonly HashSet<string> mSimpleFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "dimmer", "strobe", "strobo", "raw", "rawstep", "fog", "fan", "iris", "focus", "zoom",
            "ptspeed", "prism", "shutter", "colorwheel", "gobowheel", "frost", "rainbow",
            "effect", "macro", "speed", "sound", "reset", "colortemp", "switch", "const",
            "rotation", "index", "goboindex", "goborotation", "goboshake", "color", "beam",
            "effectwheel", "blade", "video", "audio", "keystone", "shutterblade", "pantilt",
            "intensity", "level", "power", "temperature", "humidity", "pressure"
        };

        // Container functions whose children are channels with dmxchannel attributes.
        private static readonly HashSet<string> mContainerFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "blades", "rgbw", "rgbwa", "rgbwauv", "cmw", "colour"
        };

        public static DeviceDescription Parse(string pFilePath)
        {
            var doc = XDocument.Load(pFilePath);
            return Parse(doc, pFilePath);
        }

        public static DeviceDescription Parse(XDocument pDoc, string pFilePath = "")
        {
            var root = pDoc.Root ?? throw new InvalidDataException("DDF has no root element.");

            var description = new DeviceDescription
            {
                FilePath = pFilePath,
                Id = Path.GetFileNameWithoutExtension(pFilePath),
                Image = GetAttribute(root, "image") ?? string.Empty,
            };

            var information = GetElement(root, "information");

            if (information != null)
            {
                description.Model = GetElementValue(information, "model");
                description.Vendor = GetElementValue(information, "vendor");
                description.Mode = GetElementValue(information, "mode");
                description.Author = GetElementValue(information, "author");
            }

            if (string.IsNullOrWhiteSpace(description.Model))
                description.Model = description.Id;

            var functions = GetElement(root, "functions");

            if (functions != null)
            {
                foreach (var element in functions.Elements())
                {
                    ParseFunction(element, description);
                }
            }

            description.Functions = description.Functions.OrderBy(f => f.DmxChannel).ThenBy(f => f.Key).ToList();

            int maxChannel = description.Functions.Count > 0 ? description.Functions.Max(f => f.DmxChannel) + 1 : 0;
            int declaredCount = GetIntAttribute(root, "dmxaddresscount") ?? 0;

            description.ChannelCount = Math.Max(declaredCount, maxChannel);

            return description;
        }

        private static void ParseFunction(XElement pElement, DeviceDescription pDescription)
        {
            string name = pElement.Name.LocalName;

            if (name.Equals("rgb", StringComparison.OrdinalIgnoreCase))
            {
                ParseRgb(pElement, pDescription);
            }
            else if (name.Equals("position", StringComparison.OrdinalIgnoreCase))
            {
                ParsePosition(pElement, pDescription);
            }
            else if (name.Equals("matrix", StringComparison.OrdinalIgnoreCase))
            {
                ParseMatrix(pElement, pDescription);
            }
            else if (name.Equals("radix", StringComparison.OrdinalIgnoreCase))
            {
                ParseRadix(pElement, pDescription);
            }
            else if (name.Equals("cmy", StringComparison.OrdinalIgnoreCase))
            {
                ParseAliasedContainer(pElement, pDescription, mCmyAliases);
            }
            else if (name.Equals("hsv", StringComparison.OrdinalIgnoreCase))
            {
                ParseAliasedContainer(pElement, pDescription, mHsvAliases);
            }
            else if (mContainerFunctions.Contains(name))
            {
                ParseChannelContainer(pElement, pDescription);
            }
            else if (mSimpleFunctions.Contains(name))
            {
                int? dmxChannel = GetIntAttribute(pElement, "dmxchannel");

                if (dmxChannel == null)
                    return;

                string displayName = GetAttribute(pElement, "name") ?? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
                string key = name.Equals("rawstep", StringComparison.OrdinalIgnoreCase) || name.Equals("raw", StringComparison.OrdinalIgnoreCase)
                    ? $"{name.ToLowerInvariant()}/{displayName}"
                    : name.ToLowerInvariant();

                AddFunction(pDescription, key, displayName, dmxChannel.Value, GetByteAttribute(pElement, "defaultval"), pElement);
            }
            else
            {
                // Unknown function: keep it as a generic channel so nothing is lost.
                int? dmxChannel = GetIntAttribute(pElement, "dmxchannel");

                if (dmxChannel == null)
                    return;

                string displayName = GetAttribute(pElement, "name") ?? name;

                AddFunction(pDescription, $"other/{name.ToLowerInvariant()}/{dmxChannel}", displayName, dmxChannel.Value,
                    GetByteAttribute(pElement, "defaultval"), pElement);
            }
        }

        private static void ParseRgb(XElement pRgb, DeviceDescription pDescription)
        {
            // Multiple <rgb> groups (e.g. matrix pixels, rings) must not share keys.
            int groupIndex = pDescription.Functions.Count(f => f.Key.StartsWith("rgb/", StringComparison.Ordinal));
            string prefix = groupIndex == 0 ? "rgb" : $"rgb{groupIndex}";

            foreach (var channel in pRgb.Elements())
            {
                if (!mColorAliases.TryGetValue(channel.Name.LocalName, out string? colorName))
                    continue;

                int? dmxChannel = GetIntAttribute(channel, "dmxchannel");

                if (dmxChannel == null)
                    continue;

                AddFunction(pDescription, $"{prefix}/{colorName}", CultureInfo.InvariantCulture.TextInfo.ToTitleCase(colorName),
                    dmxChannel.Value, GetByteAttribute(channel, "defaultval"), channel, FeatureType.Rgb);
            }
        }

        /// <summary>
        /// Parses a container (cmy, hsv) whose children use aliased channel names.
        /// </summary>
        private static void ParseAliasedContainer(XElement pContainer, DeviceDescription pDescription, IReadOnlyDictionary<string, string> pAliases)
        {
            string containerName = pContainer.Name.LocalName.ToLowerInvariant();
            var type = GetFunctionType(containerName);

            foreach (var channel in pContainer.Elements())
            {
                if (!pAliases.TryGetValue(channel.Name.LocalName, out string? channelName))
                    continue;

                int? dmxChannel = GetIntAttribute(channel, "dmxchannel");

                if (dmxChannel == null)
                    continue;

                AddFunction(pDescription, $"{containerName}/{channelName}", CultureInfo.InvariantCulture.TextInfo.ToTitleCase(channelName),
                    dmxChannel.Value, GetByteAttribute(channel, "defaultval"), channel, type);
            }
        }

        /// <summary>
        /// Parses a radial matrix (radix) of rings with segments. Pixels are auto-patched
        /// starting at the dmxchannel, from the inner ring outwards.
        /// </summary>
        private static void ParseRadix(XElement pRadix, DeviceDescription pDescription)
        {
            int startChannel = GetIntAttribute(pRadix, "dmxchannel") ?? 0;
            int whiteOffset = GetIntAttribute(pRadix, "whiteoffset") ?? -1;
            int amberOffset = GetIntAttribute(pRadix, "amberoffset") ?? -1;

            // Build color order from offsets (red/green/blue always present, others by ascending offset).
            var colors = new List<(string Name, int Offset)> { ("red", 0), ("green", 1), ("blue", 2) };

            if (whiteOffset >= 0)
                colors.Add(("white", whiteOffset));

            if (amberOffset >= 0)
                colors.Add(("amber", amberOffset));

            var ordered = colors.OrderBy(c => c.Offset).Select(c => c.Name).ToArray();

            int channel = startChannel;
            int pixelIndex = 0;

            foreach (var ring in pRadix.Elements().Where(e => e.Name.LocalName.Equals("ring", StringComparison.OrdinalIgnoreCase)))
            {
                int segments = GetIntAttribute(ring, "segments") ?? 0;

                for (int segment = 0; segment < segments; segment++)
                {
                    for (int color = 0; color < ordered.Length; color++)
                    {
                        AddFunction(pDescription, $"radix/{pixelIndex}/{ordered[color]}", $"Pixel {pixelIndex + 1} {ordered[color]}",
                            channel, 0, pRadix, FeatureType.Radix);

                        channel++;
                    }

                    pixelIndex++;
                }
            }
        }

        private static void ParsePosition(XElement pPosition, DeviceDescription pDescription)
        {
            foreach (var axis in pPosition.Elements())
            {
                string axisName = axis.Name.LocalName.ToLowerInvariant();

                if (axisName != "pan" && axisName != "tilt")
                    continue;

                int? dmxChannel = GetIntAttribute(axis, "dmxchannel");

                if (dmxChannel != null)
                {
                    // Fine/ultra/ultrafine channels are added by AddFunction.
                    AddFunction(pDescription, $"position/{axisName}", CultureInfo.InvariantCulture.TextInfo.ToTitleCase(axisName),
                        dmxChannel.Value, GetByteAttribute(axis, "defaultval"), axis);
                }
            }
        }

        /// <summary>
        /// Parses a container function (hsv, cmy, blades, ...) whose children are channels
        /// with dmxchannel attributes. Key: container/childname.
        /// </summary>
        private static void ParseChannelContainer(XElement pContainer, DeviceDescription pDescription)
        {
            string containerName = pContainer.Name.LocalName.ToLowerInvariant();
            var type = GetFunctionType(containerName);

            foreach (var channel in pContainer.Elements())
            {
                int? dmxChannel = GetIntAttribute(channel, "dmxchannel");

                if (dmxChannel == null)
                    continue;

                string channelName = channel.Name.LocalName;
                string displayName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(channelName.ToLowerInvariant());

                AddFunction(pDescription, $"{containerName}/{channelName.ToLowerInvariant()}", displayName,
                    dmxChannel.Value, GetByteAttribute(channel, "defaultval"), channel, type);
            }
        }

        /// <summary>
        /// Parses a matrix function. Two forms exist:
        /// inline (rows/columns attributes, monochrome or whiteoffset) and pixel children.
        /// </summary>
        private static void ParseMatrix(XElement pMatrix, DeviceDescription pDescription)
        {
            if (pMatrix.Elements().Any())
            {
                // Pixel children form: each pixel exposes color channels.
                int index = 0;

                foreach (var pixel in pMatrix.Elements())
                {
                    foreach (var channel in pixel.Elements())
                    {
                        int? dmxChannel = GetIntAttribute(channel, "dmxchannel");

                        if (dmxChannel == null)
                            continue;

                        AddFunction(pDescription, $"matrix/{index}/{channel.Name.LocalName.ToLowerInvariant()}",
                            $"Pixel {index + 1} {channel.Name.LocalName}", dmxChannel.Value, GetByteAttribute(channel, "defaultval"), channel, FeatureType.Matrix);
                    }

                    index++;
                }

                return;
            }

            // Inline form: rows x columns pixels starting at dmxchannel.
            int startChannel = GetIntAttribute(pMatrix, "dmxchannel") ?? 0;
            int rows = GetIntAttribute(pMatrix, "rows") ?? 0;
            int columns = GetIntAttribute(pMatrix, "columns") ?? 0;

            // monochrome="true" or the misspelled variant "monocrome".
            bool monochrome = (GetAttribute(pMatrix, "monochrome") ?? GetAttribute(pMatrix, "monocrome") ?? "false")
                .Equals("true", StringComparison.OrdinalIgnoreCase);

            int pixels = rows * columns;

            if (monochrome)
            {
                for (int pixel = 0; pixel < pixels; pixel++)
                {
                    AddFunction(pDescription, $"matrix/{pixel}/intensity", $"Pixel {pixel + 1}",
                        startChannel + pixel, 0, pMatrix, FeatureType.Matrix);
                }

                return;
            }

            // RGB always present; additional colors via their offsets ordered ascending.
            var colors = new List<(string Name, int Offset)> { ("red", 0), ("green", 1), ("blue", 2) };

            foreach (var colorName in new[] { "white", "amber", "uv", "coldwhite", "warmwhite" })
            {
                int? offset = GetIntAttribute(pMatrix, colorName + "offset");

                if (offset != null)
                    colors.Add((colorName, offset.Value));
            }

            var ordered = colors.OrderBy(c => c.Offset).Select(c => c.Name).ToArray();
            int channelsPerPixel = ordered.Length;

            for (int pixel = 0; pixel < pixels; pixel++)
            {
                for (int color = 0; color < channelsPerPixel; color++)
                {
                    AddFunction(pDescription, $"matrix/{pixel}/{ordered[color]}", $"Pixel {pixel + 1} {ordered[color]}",
                        startChannel + pixel * channelsPerPixel + color, 0, pMatrix, FeatureType.Matrix);
                }
            }
        }

        private static void AddFunction(DeviceDescription pDescription, string pKey, string pName, int pDmxChannel, byte pDefault, XElement pElement)
        {
            AddFunction(pDescription, pKey, pName, pDmxChannel, pDefault, pElement, GetFunctionType(pElement.Name.LocalName));
        }

        private static void AddFunction(DeviceDescription pDescription, string pKey, string pName, int pDmxChannel, byte pDefault, XElement pElement, FeatureType pType)
        {
            var function = new DdfFunction
            {
                Key = pKey,
                Name = pName,
                FunctionType = pType,
                DmxChannel = pDmxChannel,
                DefaultValue = pDefault,
            };

            foreach (var stepElement in pElement.Elements().Where(e => e.Name.LocalName.Equals("step", StringComparison.OrdinalIgnoreCase)))
            {
                function.Steps.Add(new DdfStep
                {
                    Caption = GetAttribute(stepElement, "caption") ?? string.Empty,
                    Type = GetAttribute(stepElement, "type") ?? string.Empty,
                    Value = GetAttribute(stepElement, "val") ?? string.Empty,
                    MinDmx = GetIntAttribute(stepElement, "mindmx") ?? 0,
                    MaxDmx = GetIntAttribute(stepElement, "maxdmx") ?? 255,
                });
            }

            foreach (var rangeElement in pElement.Elements().Where(e => e.Name.LocalName.Equals("range", StringComparison.OrdinalIgnoreCase)))
            {
                function.Ranges.Add(ParseRange(rangeElement));
            }

            // Nested range containers, e.g. <wheelrotation><range .../></wheelrotation>.
            foreach (var container in pElement.Elements().Where(e => e.Name.LocalName.Equals("wheelrotation", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var rangeElement in container.Elements().Where(e => e.Name.LocalName.Equals("range", StringComparison.OrdinalIgnoreCase)))
                {
                    function.Ranges.Add(ParseRange(rangeElement));
                }
            }

            pDescription.Functions.Add(function);

            // Higher resolution channels: 16/24/32-bit via finedmxchannel, ultradmxchannel, ultrafinedmxchannel.
            AddResolutionChannel(pDescription, pElement, pKey, pName, pType, "finedmxchannel", "fine");
            AddResolutionChannel(pDescription, pElement, pKey, pName, pType, "ultradmxchannel", "ultra");
            AddResolutionChannel(pDescription, pElement, pKey, pName, pType, "ultrafinedmxchannel", "ultrafine");
        }

        private static void AddResolutionChannel(DeviceDescription pDescription, XElement pElement, string pKey, string pName, FeatureType pType, string pAttribute, string pSuffix)
        {
            int? channel = GetIntAttribute(pElement, pAttribute);

            if (channel == null)
                return;

            pDescription.Functions.Add(new DdfFunction
            {
                Key = $"{pKey}/{pSuffix}",
                Name = $"{pName} ({pSuffix})",
                FunctionType = pType,
                DmxChannel = channel.Value,
                DefaultValue = 0,
            });
        }

        /// <summary>
        /// Maps a DDF element name to its function type. Custom/unknown elements map to Raw.
        /// </summary>
        private static FeatureType GetFunctionType(string pElementName)
        {
            return pElementName.ToLowerInvariant() switch
            {
                "rgb" => FeatureType.Rgb,
                "cmy" => FeatureType.Cmy,
                "hsv" => FeatureType.Hsv,
                "dimmer" => FeatureType.Dimmer,
                "shutter" => FeatureType.Shutter,
                "strobe" or "strobo" => FeatureType.Strobe,
                "switch" => FeatureType.Switch,
                "position" => FeatureType.Position,
                "colorwheel" => FeatureType.Colorwheel,
                "colortemp" => FeatureType.Colortemp,
                "gobowheel" => FeatureType.Gobowheel,
                "focus" => FeatureType.Focus,
                "frost" => FeatureType.Frost,
                "iris" => FeatureType.Iris,
                "zoom" => FeatureType.Zoom,
                "prism" => FeatureType.Prism,
                "rotation" => FeatureType.Rotation,
                "index" => FeatureType.Index,
                "matrix" => FeatureType.Matrix,
                "radix" => FeatureType.Radix,
                "raw" => FeatureType.Raw,
                "rawstep" => FeatureType.Rawstep,
                "const" => FeatureType.Const,
                "fog" => FeatureType.Fog,
                "fan" => FeatureType.Fan,
                _ => FeatureType.Raw,
            };
        }

        private static DdfRange ParseRange(XElement pElement)
        {
            return new DdfRange
            {
                Type = GetAttribute(pElement, "type") ?? string.Empty,
                MinDmx = GetIntAttribute(pElement, "mindmx") ?? 0,
                MaxDmx = GetIntAttribute(pElement, "maxdmx") ?? 255,
                MinValue = GetDoubleAttribute(pElement, "minval") ?? 0,
                MaxValue = GetDoubleAttribute(pElement, "maxval") ?? 0,
            };
        }

        private static XElement? GetElement(XElement pParent, string pName)
        {
            return pParent.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(pName, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetElementValue(XElement pParent, string pName)
        {
            return GetElement(pParent, pName)?.Value.Trim() ?? string.Empty;
        }

        private static string? GetAttribute(XElement pElement, string pName)
        {
            return pElement.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(pName, StringComparison.OrdinalIgnoreCase))?.Value;
        }

        private static int? GetIntAttribute(XElement pElement, string pName)
        {
            string? value = GetAttribute(pElement, pName);

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
                return result;

            return null;
        }

        private static double? GetDoubleAttribute(XElement pElement, string pName)
        {
            string? value = GetAttribute(pElement, pName);

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                return result;

            return null;
        }

        private static byte GetByteAttribute(XElement pElement, string pName)
        {
            int? value = GetIntAttribute(pElement, pName);

            if (value == null)
                return 0;

            return (byte)Math.Clamp(value.Value, 0, 255);
        }
    }
}
