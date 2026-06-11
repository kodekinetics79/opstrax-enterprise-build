using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zayra.Api.Infrastructure.Json;

/// <summary>
/// HTML forms naturally submit "" for untouched optional fields, but System.Text.Json
/// rejects "" for Guid?/DateTime?/DateOnly? with a 400 before validation runs.
/// These converters treat empty/whitespace strings as null so optional fields behave
/// the way every form expects.
/// </summary>
public sealed class EmptyStringNullableGuidConverter : JsonConverter<Guid?>
{
    public override Guid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var value = reader.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : Guid.Parse(value);
    }

    public override void Write(Utf8JsonWriter writer, Guid? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value);
    }
}

public sealed class EmptyStringNullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var value = reader.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value);
    }
}

public sealed class EmptyStringNullableDateOnlyConverter : JsonConverter<DateOnly?>
{
    public override DateOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var value = reader.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public override void Write(Utf8JsonWriter writer, DateOnly? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd"));
    }
}
