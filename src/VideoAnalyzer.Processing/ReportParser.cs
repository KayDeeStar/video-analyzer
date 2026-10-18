using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoAnalyzer.Shared;

namespace VideoAnalyzer.Processing;

/// <summary>The model's answer could not be turned into a report; Step Functions retries once.</summary>
public sealed class ReportFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Parses the model's JSON answer leniently: tolerates markdown fences, times written as "0:04" or "4s",
/// lists where a single value was expected (and vice versa), and missing fields.
/// </summary>
public static class ReportParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters =
        {
            new LenientString(), new LenientSeconds(), new LenientNullableSeconds(), new LenientStringList(),
            new LenientSections(),
        },
    };

    public static AiReport Parse(string modelText)
    {
        var start = modelText.IndexOf('{');
        var end = modelText.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new ReportFormatException("The model did not return a JSON report.");

        AiReport? report;
        try
        {
            report = JsonSerializer.Deserialize<AiReport>(modelText[start..(end + 1)], Options);
        }
        catch (JsonException ex)
        {
            throw new ReportFormatException($"The model returned malformed JSON: {ex.Message}", ex);
        }

        return Normalize(report ?? throw new ReportFormatException("The model returned an empty report."));
    }

    /// <summary>Replaces missing lists and labels so the UI never has to null-check them.</summary>
    private static AiReport Normalize(AiReport r) => r with
    {
        Summary = r.Summary ?? "",
        Narrative = (r.Narrative ?? []).Where(s => !string.IsNullOrWhiteSpace(s.Text)).ToList(),
        VisibleText = (r.VisibleText ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
        People = (r.People ?? []).Where(p => p is not null)
            .Select((p, i) => p with { Label = string.IsNullOrWhiteSpace(p.Label) ? $"Person {i + 1}" : p.Label })
            .ToList(),
        Vehicles = (r.Vehicles ?? []).Where(v => v is not null).ToList(),
        Timeline = Clean(r.Timeline),
        NotableEvents = Clean(r.NotableEvents),
    };

    private static List<TimelineEvent> Clean(IReadOnlyList<TimelineEvent>? events) =>
        (events ?? []).Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Description))
            .OrderBy(e => e.Seconds)
            .ToList();

    /// <summary>"4", "4.5", "4s", "0:04", "1:02:03" → seconds.</summary>
    public static double? ParseSeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var s = text.Trim().TrimEnd('s', 'S').Trim();
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return seconds;

        var parts = s.Split(':');
        if (parts.Length is 2 or 3 && parts.All(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            return parts.Aggregate(0d, (total, p) => total * 60 + double.Parse(p, CultureInfo.InvariantCulture));
        return null;
    }

    private sealed class LenientString : JsonConverter<string?>
    {
        public override bool HandleNull => true;

        public override string? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.String:
                    var s = reader.GetString();
                    return string.IsNullOrWhiteSpace(s) || s.Trim().Equals("null", StringComparison.OrdinalIgnoreCase) ? null : s.Trim();
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                    return JsonDocument.ParseValue(ref reader).RootElement.GetRawText();
                case JsonTokenType.StartArray:
                    var items = new List<string>();
                    using (var doc = JsonDocument.ParseValue(ref reader))
                        items.AddRange(doc.RootElement.EnumerateArray().Select(Flatten).Where(x => x.Length > 0));
                    return items.Count == 0 ? null : string.Join(", ", items);
                default:
                    using (var doc = JsonDocument.ParseValue(ref reader))
                        return Flatten(doc.RootElement);
            }
        }

        private static string Flatten(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.String => e.GetString()?.Trim() ?? "",
            JsonValueKind.Null => "",
            JsonValueKind.Object => string.Join(", ", e.EnumerateObject().Select(p => $"{p.Name}: {Flatten(p.Value)}")),
            JsonValueKind.Array => string.Join(", ", e.EnumerateArray().Select(Flatten)),
            _ => e.GetRawText(),
        };

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }

    private sealed class LenientStringList : JsonConverter<IReadOnlyList<string>>
    {
        public override IReadOnlyList<string>? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return [];
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            IEnumerable<JsonElement> items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : [root];
            return items
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText())
                .Select(s => s.Trim())
                .Where(s => s.Length > 0 && !s.Equals("null", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.ToArray(), options);
    }

    /// <summary>
    /// Accepts the requested [{ "title", "text" }] list, but also a single string, a { "Title": "text" } object,
    /// or items that use "heading"/"content" instead.
    /// </summary>
    private sealed class LenientSections : JsonConverter<IReadOnlyList<ReportSection>>
    {
        public override IReadOnlyList<ReportSection>? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            return root.ValueKind switch
            {
                JsonValueKind.String => [new ReportSection("Analysis", root.GetString()?.Trim() ?? "")],
                JsonValueKind.Object => root.EnumerateObject()
                    .Select(p => new ReportSection(p.Name, Text(p.Value)))
                    .ToList(),
                JsonValueKind.Array => root.EnumerateArray().Select(Section).OfType<ReportSection>().ToList(),
                _ => [],
            };
        }

        private static ReportSection? Section(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.String)
                return new ReportSection("Analysis", e.GetString()?.Trim() ?? "");
            if (e.ValueKind != JsonValueKind.Object)
                return null;
            var title = First(e, "title", "heading", "name") ?? "Analysis";
            var text = First(e, "text", "content", "body", "description") ?? "";
            return new ReportSection(title, text);
        }

        private static string? First(JsonElement e, params string[] names)
        {
            foreach (var p in e.EnumerateObject())
                if (names.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                    return Text(p.Value);
            return null;
        }

        private static string Text(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.String => e.GetString()?.Trim() ?? "",
            JsonValueKind.Array => string.Join("\n", e.EnumerateArray().Select(Text)),
            _ => e.GetRawText(),
        };

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<ReportSection> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.ToArray(), options);
    }

    private sealed class LenientNullableSeconds : JsonConverter<double?>
    {
        public override bool HandleNull => true;

        public override double? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetDouble(),
                JsonTokenType.String => ParseSeconds(reader.GetString()),
                _ => SkipAndReturnNull(ref reader),
            };

        private static double? SkipAndReturnNull(ref Utf8JsonReader reader)
        {
            reader.Skip();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
        {
            if (value is { } v) writer.WriteNumberValue(v);
            else writer.WriteNullValue();
        }
    }

    private sealed class LenientSeconds : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    return reader.GetDouble();
                case JsonTokenType.String:
                    return ParseSeconds(reader.GetString()) ?? 0;
                default:
                    reader.Skip();
                    return 0;
            }
        }

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value);
    }
}
