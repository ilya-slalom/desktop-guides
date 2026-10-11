namespace DesktopGuides.Core.Library;

/// <summary>A place in the shell that shows an app icon (T11.5).</summary>
public enum AppIcon
{
    NavLibrary,
    NavSettings,
    TitleBar,
    EmptyLibrary,
    EmptySearch,
    ThemeCard,
    MaterialCard,
    BackupCard,
    RestoreCard,
    ProvidersCard,
    ColorfulIconsCard
}

/// <summary>A Segoe Fluent glyph, or a colour asset file under <see cref="AppIconCatalog.AssetFolder"/>.</summary>
public sealed record AppIconChoice(string? Glyph, string? AssetFile)
{
    public bool IsAsset => AssetFile is not null;

    public static AppIconChoice FromGlyph(string glyph) => new(glyph, null);

    public static AppIconChoice FromAsset(string file) => new(null, file);
}

/// <summary>Each place's monochrome glyph and its Fluent UI System Icons colour asset.</summary>
public static class AppIconCatalog
{
    public const string AssetFolder = "Assets/Icons/Color";

    private static readonly IReadOnlyDictionary<AppIcon, (string Glyph, string Asset)> Table =
        new Dictionary<AppIcon, (string, string)>
        {
            [AppIcon.NavLibrary] = ("\uE80F", "ic_fluent_library_24_color.svg"),
            [AppIcon.NavSettings] = ("\uE713", "ic_fluent_settings_24_color.svg"),
            [AppIcon.TitleBar] = ("\uE82D", "ic_fluent_library_24_color.svg"),
            [AppIcon.EmptyLibrary] = ("\uE82D", "ic_fluent_book_open_24_color.svg"),
            [AppIcon.EmptySearch] = ("\uE721", "ic_fluent_search_sparkle_24_color.svg"),
            [AppIcon.ThemeCard] = ("\uE771", "ic_fluent_paint_brush_24_color.svg"),
            // A picture in both modes; Fluent has no colour palette.
            [AppIcon.MaterialCard] = ("\uE8B9", "ic_fluent_image_24_color.svg"),
            [AppIcon.BackupCard] = ("\uE896", "ic_fluent_document_folder_24_color.svg"),
            [AppIcon.RestoreCard] = ("\uE777", "ic_fluent_history_24_color.svg"),
            [AppIcon.ProvidersCard] = ("\uE721", "ic_fluent_globe_24_color.svg"),
            [AppIcon.ColorfulIconsCard] = ("\uE80A", "ic_fluent_apps_24_color.svg"),
        };

    // Game-page guide rows: a missing file wins over the format.
    private static readonly IReadOnlyDictionary<GuideFormat, (string Glyph, string Asset)> GuideTable =
        new Dictionary<GuideFormat, (string, string)>
        {
            [GuideFormat.Txt] = ("\uE8A5", "ic_fluent_document_text_24_color.svg"),
            [GuideFormat.Html] = ("\uE774", "ic_fluent_globe_24_color.svg"),
            // Fluent has no colour PDF or game controller; the dg_ icons are the
            // project's own, generated in Fluent's style (see SOURCE.md).
            [GuideFormat.Pdf] = ("\uEA90", "dg_document_pdf_color.png"),
        };

    private static readonly (string Glyph, string Asset) MissingGuide =
        ("\uE7BA", "ic_fluent_warning_24_color.svg");

    private static readonly (string Glyph, string Asset) GamePlaceholder =
        ("\uE7FC", "dg_game_controller_color.png");

    public static IReadOnlyList<AppIcon> All { get; } = Enum.GetValues<AppIcon>();

    public static IReadOnlyList<string> AssetFiles { get; } =
        Table.Values.Concat(GuideTable.Values).Append(MissingGuide).Append(GamePlaceholder)
            .Select(entry => entry.Asset).Distinct().Order(StringComparer.Ordinal).ToArray();

    // High contrast always wins: its system colours apply only to glyphs.
    public static AppIconChoice For(AppIcon icon, bool colorful, bool highContrast)
    {
        return Choose(Table[icon], colorful, highContrast);
    }

    public static AppIconChoice ForGuide(GuideFormat format, bool fileMissing, bool colorful, bool highContrast) =>
        Choose(fileMissing ? MissingGuide : GuideTable[format], colorful, highContrast);

    // A library row's tile before its artwork loads.
    public static AppIconChoice ForGame(bool colorful, bool highContrast) =>
        Choose(GamePlaceholder, colorful, highContrast);

    private static AppIconChoice Choose((string Glyph, string Asset) entry, bool colorful, bool highContrast) =>
        colorful && !highContrast ? AppIconChoice.FromAsset(entry.Asset) : AppIconChoice.FromGlyph(entry.Glyph);
}
