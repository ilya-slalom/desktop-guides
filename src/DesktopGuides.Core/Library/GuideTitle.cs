namespace DesktopGuides.Core.Library;

public static class GuideTitle
{
    public const int TitleLimit = 200;
    public const string Fallback = "Untitled guide";

    public static string Create(string? title) =>
        TryCreate(title, out string trimmed)
            ? trimmed
            : throw new ArgumentException(
                $"A title of 1–{TitleLimit} characters is required.", nameof(title));

    public static bool TryCreate(string? title, out string trimmed)
    {
        trimmed = title?.Trim() ?? "";
        return trimmed.Length is >= 1 and <= TitleLimit;
    }

    public static string Suggest(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (name.Length > TitleLimit)
        {
            int cut = char.IsHighSurrogate(name[TitleLimit - 1]) ? TitleLimit - 1 : TitleLimit;
            name = name[..cut].TrimEnd();
        }
        return name.Length == 0 ? Fallback : name;
    }
}
