using System.Text.Json;
using System.Text.Json.Serialization;

namespace DmxControlUtilities.Lib.Models
{
    /// <summary>
    /// Serializes timecode show tracks with a type discriminator so the concrete track types can be restored.
    /// </summary>
    public class TimecodeShowTrackJsonConverter : JsonConverter<ITimecodeShowTrack>
    {
        private const string TypeProperty = "$type";

        public override ITimecodeShowTrack? Read(ref Utf8JsonReader pReader, Type pTypeToConvert, JsonSerializerOptions pOptions)
        {
            using var doc = JsonDocument.ParseValue(ref pReader);
            var root = doc.RootElement;

            string? typeName = null;

            if (root.TryGetProperty(TypeProperty, out var typeElement))
            {
                typeName = typeElement.GetString();
            }

            Type targetType = typeName switch
            {
                nameof(TimecodeAudioTrack) => typeof(TimecodeAudioTrack),
                nameof(LightEventTrack) => typeof(LightEventTrack),
                _ => root.EnumerateObject().Any(p => string.Equals(p.Name, nameof(TimecodeAudioTrack.AudioFileName), StringComparison.OrdinalIgnoreCase))
                    ? typeof(TimecodeAudioTrack)
                    : typeof(LightEventTrack)
            };

            return (ITimecodeShowTrack?)root.Deserialize(targetType, pOptions);
        }

        public override void Write(Utf8JsonWriter pWriter, ITimecodeShowTrack pValue, JsonSerializerOptions pOptions)
        {
            pWriter.WriteStartObject();
            pWriter.WriteString(TypeProperty, pValue.GetType().Name);

            using var doc = JsonSerializer.SerializeToDocument(pValue, pValue.GetType(), pOptions);

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                property.WriteTo(pWriter);
            }

            pWriter.WriteEndObject();
        }
    }
}
