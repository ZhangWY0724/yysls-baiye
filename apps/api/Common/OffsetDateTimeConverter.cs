using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yanyun.Api.Common;

public sealed class OffsetDateTimeConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is null || !System.Text.RegularExpressions.Regex.IsMatch(text, @"T.+(Z|[+-]\d{2}:\d{2})$") ||
            !reader.TryGetDateTimeOffset(out var value)) throw new JsonException("时间必须为带时区的 ISO 8601 格式");
        return value.ToUniversalTime();
    }
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime);
}
