using System.Text;
using System.Text.Json;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Paths;

namespace DesktopGuides.Core.Reading;

public static class ReaderLocationCodec
{
    public const int CurrentVersion = 1;
    public const int MaxBytes = 4096;
    private const int MaxContextCharacters = 160;
    private const int MaxElementIdCharacters = 128;

    public static string Serialize(ReaderLocation location)
    {
        Validate(location);
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("format", location.Format.ToString());
            writer.WriteNumber("schemaVersion", location.SchemaVersion);
            writer.WriteString("contentSha256", location.ContentSha256);
            writer.WritePropertyName("payload");
            writer.WriteStartObject();
            switch (location.Payload)
            {
                case TextPosition text:
                    writer.WriteNumber("characterOffset", text.CharacterOffset);
                    writer.WriteString("context", text.Context);
                    break;
                case HtmlPosition html:
                    writer.WriteString("documentPath", html.DocumentPath);
                    WriteOptionalString(writer, "elementId", html.ElementId);
                    WriteOptionalString(writer, "textQuote", html.TextQuote);
                    writer.WriteNumber("textOffset", html.TextOffset);
                    writer.WriteNumber("scrollFraction", html.ScrollFraction);
                    break;
                case PdfPosition pdf:
                    writer.WriteNumber("pageIndex", pdf.PageIndex);
                    writer.WriteNumber("pageFraction", pdf.PageFraction);
                    break;
            }
            writer.WriteEndObject();
            if (location.EstimatedFraction is double estimate)
            {
                writer.WriteNumber("estimatedFraction", estimate);
            }
            else
            {
                writer.WriteNull("estimatedFraction");
            }
            writer.WriteEndObject();
        }

        if (buffer.Length > MaxBytes)
        {
            throw new InvalidDataException("Reader locator exceeds the storage limit.");
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static LocationDecodeResult Deserialize(
        string? json,
        GuideFormat expectedFormat,
        string expectedContentSha256,
        string? expectedHtmlDocument = null)
    {
        if (string.IsNullOrWhiteSpace(json) ||
            Encoding.UTF8.GetByteCount(json) > MaxBytes ||
            !ValidHash(expectedContentSha256))
        {
            return new(LocationDecodeStatus.Invalid, null);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 8
            });
            JsonElement root = document.RootElement;
            RequireObject(root);
            string formatName = RequireString(root, "format");
            if (!Enum.TryParse(formatName, ignoreCase: false, out GuideFormat format) ||
                !Enum.IsDefined(format) || format.ToString() != formatName ||
                format != expectedFormat)
            {
                return new(LocationDecodeStatus.Invalid, null);
            }

            int version = RequireInt(root, "schemaVersion");
            if (version != CurrentVersion)
            {
                return new(LocationDecodeStatus.UnsupportedVersion, null);
            }

            string hash = RequireString(root, "contentSha256");
            JsonElement payload = RequireProperty(root, "payload", JsonValueKind.Object);
            RequireObject(payload);
            ReaderPosition position = format switch
            {
                GuideFormat.Txt => new TextPosition(
                    RequireInt(payload, "characterOffset"),
                    RequireString(payload, "context")),
                GuideFormat.Html => new HtmlPosition(
                    RequireString(payload, "documentPath"),
                    OptionalString(payload, "elementId"),
                    OptionalString(payload, "textQuote"),
                    RequireInt(payload, "textOffset"),
                    RequireFraction(payload, "scrollFraction")),
                GuideFormat.Pdf => new PdfPosition(
                    RequireInt(payload, "pageIndex"),
                    RequireFraction(payload, "pageFraction")),
                _ => throw new InvalidDataException("Unknown reader format.")
            };
            ReaderLocation location = new(
                format, version, hash, position, OptionalFraction(root, "estimatedFraction"));
            Validate(location);
            if (format == GuideFormat.Html &&
                (expectedHtmlDocument is null ||
                 ManagedRelativePath.Parse(expectedHtmlDocument) !=
                 ((HtmlPosition)position).DocumentPath))
            {
                return new(LocationDecodeStatus.Invalid, null);
            }

            return new(hash == expectedContentSha256
                ? LocationDecodeStatus.Valid
                : LocationDecodeStatus.ContentChanged, location);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException
            or InvalidOperationException or FormatException)
        {
            return new(LocationDecodeStatus.Invalid, null);
        }
    }

    public static IReadOnlyList<RestoreKind> RestoreCandidates(LocationDecodeResult result)
    {
        if (result.Location is null)
        {
            return [];
        }

        bool hasContext = result.Location.Payload switch
        {
            TextPosition text => text.Context.Length > 0,
            HtmlPosition html => !string.IsNullOrEmpty(html.ElementId) ||
                                 !string.IsNullOrEmpty(html.TextQuote),
            _ => false
        };
        List<RestoreKind> order = [];
        if (result.Status == LocationDecodeStatus.Valid)
        {
            order.Add(RestoreKind.Exact);
        }
        if (hasContext)
        {
            order.Add(RestoreKind.Context);
        }
        if (result.Location.EstimatedFraction.HasValue ||
            result.Location.Payload is PdfPosition)
        {
            order.Add(RestoreKind.Approximate);
        }
        return order;
    }

    private static void Validate(ReaderLocation location)
    {
        if (location.SchemaVersion != CurrentVersion ||
            !Enum.IsDefined(location.Format) ||
            !ValidHash(location.ContentSha256) ||
            !ValidNullableFraction(location.EstimatedFraction))
        {
            throw new InvalidDataException("Reader locator header is invalid.");
        }

        bool valid = (location.Format, location.Payload) switch
        {
            (GuideFormat.Txt, TextPosition text) =>
                text.CharacterOffset >= 0 &&
                text.Context is not null &&
                text.Context.Length <= MaxContextCharacters &&
                !text.Context.Contains('\0'),
            (GuideFormat.Html, HtmlPosition html) =>
                ValidHtmlPosition(html),
            (GuideFormat.Pdf, PdfPosition pdf) =>
                pdf.PageIndex >= 0 && ValidFraction(pdf.PageFraction),
            _ => false
        };
        if (!valid)
        {
            throw new InvalidDataException("Reader locator payload is invalid.");
        }
    }

    private static bool ValidHtmlPosition(HtmlPosition html)
    {
        try
        {
            _ = ManagedRelativePath.Parse(html.DocumentPath);
        }
        catch (InvalidDataException)
        {
            return false;
        }

        return html.TextOffset >= 0 &&
               ValidFraction(html.ScrollFraction) &&
               ValidOptionalText(html.ElementId, MaxElementIdCharacters) &&
               ValidOptionalText(html.TextQuote, MaxContextCharacters);
    }

    private static bool ValidOptionalText(string? text, int limit) =>
        text is null || (text.Length <= limit && !text.Contains('\0'));

    private static bool ValidFraction(double value) =>
        double.IsFinite(value) && value >= 0 && value <= 1;

    private static bool ValidNullableFraction(double? value) =>
        !value.HasValue || ValidFraction(value.Value);

    private static bool ValidHash(string? hash) =>
        hash is { Length: 64 } &&
        hash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void RequireObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Reader locator must be an object.");
        }

        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new InvalidDataException("Reader locator contains a duplicate field.");
            }
        }
    }

    private static JsonElement RequireProperty(
        JsonElement parent, string name, JsonValueKind kind)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != kind)
        {
            throw new InvalidDataException($"Reader locator field {name} is missing or invalid.");
        }
        return value;
    }

    private static string RequireString(JsonElement parent, string name) =>
        RequireProperty(parent, name, JsonValueKind.String).GetString()!;

    private static int RequireInt(JsonElement parent, string name)
    {
        JsonElement value = RequireProperty(parent, name, JsonValueKind.Number);
        if (!value.TryGetInt32(out int number))
        {
            throw new InvalidDataException($"Reader locator field {name} is not an integer.");
        }
        return number;
    }

    private static double RequireFraction(JsonElement parent, string name)
    {
        JsonElement value = RequireProperty(parent, name, JsonValueKind.Number);
        if (!value.TryGetDouble(out double number) || !ValidFraction(number))
        {
            throw new InvalidDataException($"Reader locator field {name} is not a fraction.");
        }
        return number;
    }

    private static string? OptionalString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Reader locator field {name} is invalid.");
        }
        return value.GetString();
    }

    private static double? OptionalFraction(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out double number) || !ValidFraction(number))
        {
            throw new InvalidDataException($"Reader locator field {name} is invalid.");
        }
        return number;
    }

    private static void WriteOptionalString(
        Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }
}
