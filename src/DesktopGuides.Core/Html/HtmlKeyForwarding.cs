using DesktopGuides.Core.Navigation;

namespace DesktopGuides.Core.Html;

/// <summary>
/// T16.1: the app keys an HTML guide's page passes back to the app. The
/// listener runs in an isolated world, so the guide's own content still has
/// no script access, and only this fixed set ever reaches the app.
/// </summary>
public static class HtmlKeyForwarding
{
    private static readonly IReadOnlyDictionary<string, ShortcutCommand> Keys =
        new Dictionary<string, ShortcutCommand>(StringComparer.Ordinal)
        {
            ["Alt+ArrowLeft"] = ShortcutCommand.Back,
            ["Control+Equal"] = ShortcutCommand.ZoomIn,
            ["Control+Shift+Equal"] = ShortcutCommand.ZoomIn,
            ["Control+NumpadAdd"] = ShortcutCommand.ZoomIn,
            ["Control+Minus"] = ShortcutCommand.ZoomOut,
            ["Control+NumpadSubtract"] = ShortcutCommand.ZoomOut,
            ["Control+Digit0"] = ShortcutCommand.ZoomReset,
            ["Control+Numpad0"] = ShortcutCommand.ZoomReset,
        };

    public static IReadOnlyCollection<string> Payloads => (IReadOnlyCollection<string>)Keys.Keys;

    public static bool TryParse(string payload, out ShortcutCommand command) =>
        Keys.TryGetValue(payload, out command);

    // Builds "Control+Shift+Alt+<code>" from the event and reports only a known key.
    public static string Script(string binding)
    {
        string list = string.Join(",", Keys.Keys.Select(key => $"'{key}'"));
        return "(() => { const keys = new Set([" + list + "]); " +
            "document.addEventListener('keydown', e => { " +
            "const k = (e.ctrlKey ? 'Control+' : '') + (e.shiftKey ? 'Shift+' : '') + " +
            "(e.altKey ? 'Alt+' : '') + e.code; " +
            "if (keys.has(k)) { e.preventDefault(); e.stopImmediatePropagation(); " +
            binding + "(k); } }, true); })();";
    }
}
