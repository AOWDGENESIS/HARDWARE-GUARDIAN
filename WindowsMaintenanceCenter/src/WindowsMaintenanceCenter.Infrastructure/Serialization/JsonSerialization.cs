using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Values;
// System.Globalization has a TextInfo as well; the alias makes the intended type unambiguous.
using TextInfo = WindowsMaintenanceCenter.Core.Values.TextInfo;

namespace WindowsMaintenanceCenter.Infrastructure.Serialization;

/// <summary>
/// Central JSON configuration. Reports, snapshots, history and the audit log all use the same
/// options so that everything is machine readable and reproducible (spec section 27).
/// </summary>
public static class JsonOptions
{
    public static JsonSerializerOptions Default { get; } = Create(writeIndented: true);

    public static JsonSerializerOptions Compact { get; } = Create(writeIndented: false);

    /// <summary>
    /// The options of the JSON report: the same options plus the sanitation of
    /// <see cref="SafeText"/>, which the text and HTML report have always applied (chapter 80,
    /// SEC-14).
    ///
    /// Why only the report carries the cleaner and not the audit log or the settings: those are read
    /// back by this application and have to come out as they went in. The report is written once and
    /// read by people and by external tools - that is where a value from outside could steer what
    /// somebody sees.
    /// </summary>
    public static JsonSerializerOptions Report { get; } = CreateReportOptions();

    private static JsonSerializerOptions CreateReportOptions()
    {
        var options = Create(writeIndented: true);
        options.Converters.Insert(0, new SafeTextJsonConverter());
        return options;
    }

    private static JsonSerializerOptions Create(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new ValueOriginJsonConverter());
        options.Converters.Add(new TextInfoJsonConverter());
        options.Converters.Add(new LocalizedTextJsonConverter());
        options.Converters.Add(new MeasuredJsonConverterFactory());
        return options;
    }
}

/// <summary>
/// Writes every string value of a report through <see cref="SafeText.Sanitise"/>. A property *name* is
/// written by the writer itself and comes from this code, so it needs no cleaning; a property *value*
/// can come from a device name, a driver string or a log line and therefore does.
/// </summary>
public sealed class SafeTextJsonConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? string.Empty : reader.GetString() ?? string.Empty;

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(SafeText.Sanitise(value));
}

/// <summary>Serialises <see cref="ValueOrigin"/> including the retrieval timestamp.</summary>
public sealed class ValueOriginJsonConverter : JsonConverter<ValueOrigin>
{
    public override ValueOrigin Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return ValueOrigin.Unknown;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var source = root.TryGetProperty("source", out var sourceElement) && sourceElement.ValueKind == JsonValueKind.String
            ? Enum.TryParse<DataSource>(sourceElement.GetString(), ignoreCase: true, out var parsed) ? parsed : DataSource.Unknown
            : DataSource.Unknown;

        var quality = root.TryGetProperty("quality", out var qualityElement) && qualityElement.ValueKind == JsonValueKind.String
            ? Enum.TryParse<SensorQuality>(qualityElement.GetString(), ignoreCase: true, out var parsedQuality) ? parsedQuality : SensorQuality.Unknown
            : SensorQuality.Unknown;

        var detail = root.TryGetProperty("detail", out var detailElement) && detailElement.ValueKind == JsonValueKind.String
            ? detailElement.GetString()
            : null;

        var retrievedAt = root.TryGetProperty("retrievedAt", out var retrievedElement) && retrievedElement.ValueKind == JsonValueKind.String
            ? retrievedElement.TryGetDateTimeOffset(out var parsedDate) ? parsedDate : default
            : default;

        return new ValueOrigin
        {
            Source = source,
            Quality = quality,
            Detail = detail,
            RetrievedAt = retrievedAt,
        };
    }

    public override void Write(Utf8JsonWriter writer, ValueOrigin value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("source", value.Source.ToString());
        writer.WriteString("quality", value.Quality.ToString());
        if (!string.IsNullOrEmpty(value.Detail))
        {
            writer.WriteString("detail", value.Detail);
        }

        if (value.RetrievedAt != default)
        {
            writer.WriteString("retrievedAt", value.RetrievedAt);
        }

        writer.WriteEndObject();
    }
}

/// <summary>
/// Serialises <see cref="TextInfo"/>. An unknown value is written as <c>null</c> plus its
/// reason, so a consumer can never mistake "unknown" for an empty string (spec section 50).
/// </summary>
public sealed class TextInfoJsonConverter : JsonConverter<TextInfo>
{
    public override TextInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();
            return TextInfo.From(raw, ValueOrigin.Unknown);
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return TextInfo.Unknown();
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var value = root.TryGetProperty("value", out var valueElement) && valueElement.ValueKind == JsonValueKind.String
            ? valueElement.GetString()
            : null;

        var reason = root.TryGetProperty("unknownReason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String
            ? reasonElement.GetString()
            : null;

        var origin = root.TryGetProperty("origin", out var originElement)
            ? originElement.Deserialize<ValueOrigin>(options)
            : ValueOrigin.Unknown;

        return string.IsNullOrWhiteSpace(value)
            ? TextInfo.Unknown(origin, reason)
            : TextInfo.Known(value!, origin);
    }

    public override void Write(Utf8JsonWriter writer, TextInfo value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.IsKnown)
        {
            writer.WriteString("value", value.Value);
        }

        if (!string.IsNullOrEmpty(value.UnknownReason))
        {
            writer.WriteString("unknownReason", value.UnknownReason);
        }

        writer.WritePropertyName("origin");
        JsonSerializer.Serialize(writer, value.Origin, options);
        writer.WriteEndObject();
    }
}

/// <summary>Serialises <see cref="LocalizedText"/> as key plus arguments.</summary>
public sealed class LocalizedTextJsonConverter : JsonConverter<LocalizedText>
{
    public override LocalizedText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new LocalizedText(reader.GetString() ?? "Text_Missing");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var key = root.TryGetProperty("key", out var keyElement) ? keyElement.GetString() ?? "Text_Missing" : "Text_Missing";

        var arguments = new List<object?>();
        if (root.TryGetProperty("arguments", out var argumentsElement) && argumentsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var argument in argumentsElement.EnumerateArray())
            {
                arguments.Add(argument.ValueKind == JsonValueKind.String ? argument.GetString() : argument.ToString());
            }
        }

        return new LocalizedText(key, arguments.ToArray());
    }

    public override void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("key", value.Key);
        if (value.Arguments.Count > 0)
        {
            writer.WritePropertyName("arguments");
            writer.WriteStartArray();
            foreach (var argument in value.Arguments)
            {
                writer.WriteStringValue(Flatten(argument));
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Arguments are flattened to strings using the invariant culture. Nested localised text is
    /// written as its key, which keeps the audit log unambiguous (spec section 27).
    /// </summary>
    private static string Flatten(object? argument) => argument switch
    {
        null => string.Empty,
        LocalizedText text => text.Key,
        TextInfo info => info.Display,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => argument.ToString() ?? string.Empty,
    };
}

/// <summary>Converter factory for <see cref="Measured{T}"/>.</summary>
public sealed class MeasuredJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Measured<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(MeasuredJsonConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

public sealed class MeasuredJsonConverter<T> : JsonConverter<Measured<T>> where T : struct
{
    public override Measured<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return Measured<T>.Missing("value not present in payload");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var reason = root.TryGetProperty("unknownReason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String
            ? reasonElement.GetString()
            : null;

        var origin = root.TryGetProperty("origin", out var originElement)
            ? originElement.Deserialize<ValueOrigin>(options)
            : ValueOrigin.Unknown;

        if (root.TryGetProperty("value", out var valueElement) && valueElement.ValueKind != JsonValueKind.Null)
        {
            var value = valueElement.Deserialize<T>(options);
            return Measured<T>.Known(value, origin);
        }

        return Measured<T>.NotAvailable(reason ?? "value not present in payload", origin);
    }

    public override void Write(Utf8JsonWriter writer, Measured<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.HasValue)
        {
            writer.WritePropertyName("value");
            JsonSerializer.Serialize(writer, value.Value, options);
        }
        else if (!string.IsNullOrEmpty(value.UnknownReason))
        {
            writer.WriteString("unknownReason", value.UnknownReason);
        }

        writer.WritePropertyName("origin");
        JsonSerializer.Serialize(writer, value.Origin, options);
        writer.WriteEndObject();
    }
}
