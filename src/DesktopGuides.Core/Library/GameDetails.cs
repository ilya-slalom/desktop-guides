namespace DesktopGuides.Core.Library;

public sealed record GameDetails(string Title, string? Platform, string? Notes)
{
    public const int TitleLimit = 160;
    public const int PlatformLimit = 80;
    public const int NotesLimit = 2000;

    public static GameDetails Create(string title, string? platform, string? notes) =>
        new(
            RequiredText(title, TitleLimit, nameof(title)),
            OptionalText(platform, PlatformLimit, nameof(platform)),
            OptionalText(notes, NotesLimit, nameof(notes)));

    private static string RequiredText(string? value, int limit, string name)
    {
        string trimmed = value?.Trim() ?? "";
        if (trimmed.Length is < 1 || trimmed.Length > limit)
        {
            throw new ArgumentException(
                $"A {name} of 1–{limit} characters is required.", name);
        }
        return trimmed;
    }

    private static string? OptionalText(string? value, int limit, string name)
    {
        string? trimmed = value?.Trim();
        if (trimmed?.Length > limit)
        {
            throw new ArgumentException($"{name} exceeds {limit} characters.", name);
        }
        return trimmed is { Length: 0 } ? null : trimmed;
    }
}
