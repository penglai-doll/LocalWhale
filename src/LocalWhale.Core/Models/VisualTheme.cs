using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalWhale.Core.Models;

[JsonConverter(typeof(VisualThemeJsonConverter))]
public enum VisualTheme
{
    Original = 0,
    WhaleGirl = 1
}

public sealed class VisualThemeJsonConverter : JsonConverter<VisualTheme>
{
    public override bool HandleNull => true;

    public override VisualTheme Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        {
            return number == (int)VisualTheme.WhaleGirl
                ? VisualTheme.WhaleGirl
                : VisualTheme.Original;
        }

        if (reader.TokenType == JsonTokenType.String &&
            Enum.TryParse<VisualTheme>(reader.GetString(), ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed))
        {
            return parsed;
        }

        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            using var ignored = JsonDocument.ParseValue(ref reader);
        }

        return VisualTheme.Original;
    }

    public override void Write(
        Utf8JsonWriter writer,
        VisualTheme value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(
            value == VisualTheme.WhaleGirl
                ? nameof(VisualTheme.WhaleGirl)
                : nameof(VisualTheme.Original));
}
