using System.Text;
using System.Text.Json;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Html;

public sealed record HtmlCapture(int Offset, string? Quote, string? ElementId, double Fraction);

public sealed record HtmlScroll(double Y, double Height, double ViewportHeight);

public enum HtmlRestoreStep { Exact, Context, Fraction }

public sealed record HtmlRestorePlan(
    IReadOnlyList<HtmlRestoreStep> Steps, HtmlPosition? Position, bool ContentChanged)
{
    public bool NeedsFind => Steps.Contains(HtmlRestoreStep.Exact) || Steps.Contains(HtmlRestoreStep.Context);
}

public sealed record HtmlFindResult(bool ExactMatch, IReadOnlyList<int> InElement, IReadOnlyList<int> Anywhere);

public sealed record HtmlRestoreTarget(HtmlRestoreStep Step, int Offset, double Fraction);

// An HTML position is a character offset in the entry document's text walk,
// with a quote and the nearest id for changed bytes and a scroll fraction as
// the last resort. Every reply from the host scripts is untrusted page
// output and is checked here before it is used or stored.
public static class HtmlLocationRules
{
    public const string ChangedReason = "The guide changed, so this is an approximate position.";
    public const string UnavailableReason = "This reading position can't be used with this guide.";
    public const int MaxQuote = 160;
    public const int MaxElementId = 128;
    public const int MaxReplyBytes = ReaderLocationCodec.MaxBytes;
    public const int MaxOffsets = 64;
    private const int MaxDepth = 4;

    private static readonly HtmlRestorePlan Nothing = new([], null, false);

    public static HtmlCapture? ParseCapture(string? json, Uri entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using JsonDocument? document = Parse(json);
        if (document is null) return null;
        JsonElement root = document.RootElement;
        if (!HasExactly(root, "offset", "quote", "id", "fraction", "href") ||
            !Offset(root.GetProperty("offset"), out int offset) ||
            !OptionalText(root.GetProperty("quote"), MaxQuote, out string? quote) ||
            !OptionalText(root.GetProperty("id"), MaxElementId, out string? id) ||
            !Fraction(root.GetProperty("fraction"), out double fraction) ||
            root.GetProperty("href").ValueKind != JsonValueKind.String ||
            !Uri.TryCreate(root.GetProperty("href").GetString(), UriKind.Absolute, out Uri? page) ||
            !HtmlNavigationPolicy.IsEntryDocument(page, entry))
        {
            return null;
        }
        return new HtmlCapture(offset, quote, id, fraction);
    }

    public static HtmlScroll? ParseScroll(string? json)
    {
        using JsonDocument? document = Parse(json);
        if (document is null) return null;
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 3) return null;
        double[] values = new double[3];
        for (int index = 0; index < 3; index++)
        {
            JsonElement item = root[index];
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out double value) ||
                !double.IsFinite(value) || value < 0)
            {
                return null;
            }
            values[index] = value;
        }
        return new HtmlScroll(values[0], values[1], values[2]);
    }

    public static int? ParsePending(string? json)
    {
        using JsonDocument? document = Parse(json);
        return document is not null && Offset(document.RootElement, out int pending) ? pending : null;
    }

    public static HtmlFindResult? ParseFind(string? json)
    {
        using JsonDocument? document = Parse(json);
        if (document is null) return null;
        JsonElement root = document.RootElement;
        if (!HasExactly(root, "exact", "element", "all")) return null;
        JsonElement exact = root.GetProperty("exact");
        if (exact.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !Offsets(root.GetProperty("element"), out List<int> inElement) ||
            !Offsets(root.GetProperty("all"), out List<int> anywhere))
        {
            return null;
        }
        return new HtmlFindResult(exact.GetBoolean(), inElement, anywhere);
    }

    // Reflow and font rounding move values by fractions of a pixel.
    public static bool Moved(HtmlScroll? last, HtmlScroll now) =>
        last is null ||
        Math.Abs(last.Y - now.Y) > 1 ||
        Math.Abs(last.Height - now.Height) > 1 ||
        Math.Abs(last.ViewportHeight - now.ViewportHeight) > 1;

    public static ReaderLocation Capture(string contentSha256, string documentPath, HtmlCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return new ReaderLocation(
            GuideFormat.Html,
            ReaderLocationCodec.CurrentVersion,
            contentSha256.ToLowerInvariant(),
            new HtmlPosition(documentPath, capture.ElementId, capture.Quote, capture.Offset, capture.Fraction),
            capture.Fraction);
    }

    // A typed location gets the same checks as stored JSON.
    public static LocationDecodeResult Decode(ReaderLocation location, string contentSha256, string documentPath)
    {
        ArgumentNullException.ThrowIfNull(location);
        string json;
        try
        {
            json = ReaderLocationCodec.Serialize(location);
        }
        catch (InvalidDataException)
        {
            return new LocationDecodeResult(LocationDecodeStatus.Invalid, null);
        }
        return ReaderLocationCodec.Deserialize(
            json, GuideFormat.Html, contentSha256.ToLowerInvariant(), documentPath);
    }

    public static HtmlRestorePlan PlanRestore(LocationDecodeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Location?.Payload is not HtmlPosition position ||
            result.Status is not (LocationDecodeStatus.Valid or LocationDecodeStatus.ContentChanged))
        {
            return Nothing;
        }
        bool changed = result.Status == LocationDecodeStatus.ContentChanged;
        bool quoted = !string.IsNullOrEmpty(position.TextQuote);
        List<HtmlRestoreStep> steps = [];
        if (quoted && !changed) steps.Add(HtmlRestoreStep.Exact);
        if (quoted) steps.Add(HtmlRestoreStep.Context);
        steps.Add(HtmlRestoreStep.Fraction);
        return new HtmlRestorePlan(steps, position, changed);
    }

    public static string FindArgs(HtmlPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        return JsonSerializer.Serialize(new { offset = position.TextOffset, quote = position.TextQuote, id = position.ElementId });
    }

    public static HtmlRestoreTarget? Resolve(HtmlRestorePlan plan, HtmlFindResult? find)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Position is not HtmlPosition position || (plan.NeedsFind && find is null))
        {
            return null;
        }
        foreach (HtmlRestoreStep step in plan.Steps)
        {
            switch (step)
            {
                case HtmlRestoreStep.Exact when find!.ExactMatch:
                    return new HtmlRestoreTarget(step, position.TextOffset, position.ScrollFraction);
                case HtmlRestoreStep.Context:
                    int? near = Nearest(find!.InElement, position.TextOffset) ??
                                Nearest(find.Anywhere, position.TextOffset);
                    if (near is int offset)
                    {
                        return new HtmlRestoreTarget(step, offset, position.ScrollFraction);
                    }
                    break;
                case HtmlRestoreStep.Fraction:
                    return new HtmlRestoreTarget(step, 0, position.ScrollFraction);
            }
        }
        return null;
    }

    public static RestoreOutcome Outcome(HtmlRestorePlan plan, HtmlRestoreTarget? target)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (target is null || !plan.Steps.Contains(target.Step))
        {
            return new RestoreOutcome(RestoreKind.Unavailable, UnavailableReason);
        }
        return target.Step switch
        {
            HtmlRestoreStep.Exact => new RestoreOutcome(RestoreKind.Exact),
            HtmlRestoreStep.Context when !plan.ContentChanged => new RestoreOutcome(RestoreKind.Context),
            // A fraction of unchanged bytes is approximate, but "the guide
            // changed" would be false.
            _ => plan.ContentChanged
                ? new RestoreOutcome(RestoreKind.Approximate, ChangedReason)
                : new RestoreOutcome(RestoreKind.Approximate)
        };
    }

    // A tie is no match: separators repeat, and either copy would be a guess.
    private static int? Nearest(IReadOnlyList<int> offsets, int saved)
    {
        int? best = null;
        long bestDistance = long.MaxValue;
        bool tie = false;
        foreach (int offset in offsets.Distinct())
        {
            long distance = Math.Abs((long)offset - saved);
            if (distance < bestDistance)
            {
                (best, bestDistance, tie) = (offset, distance, false);
            }
            else if (distance == bestDistance)
            {
                tie = true;
            }
        }
        return tie ? null : best;
    }

    private static JsonDocument? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxReplyBytes)
        {
            return null;
        }
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasExactly(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
            {
                return false;
            }
        }
        return seen.Count == names.Length;
    }

    private static bool Offset(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value) && value >= 0;
    }

    private static bool Offsets(JsonElement element, out List<int> values)
    {
        values = [];
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > MaxOffsets) return false;
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (!Offset(item, out int value)) return false;
            values.Add(value);
        }
        return true;
    }

    private static bool OptionalText(JsonElement element, int limit, out string? value)
    {
        value = null;
        if (element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString()!;
        return value.Length <= limit && !value.Contains('\0');
    }

    private static bool Fraction(JsonElement element, out double value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value) &&
               double.IsFinite(value) && value is >= 0 and <= 1;
    }
}
