using System.Text;

namespace DesktopGuides.Infrastructure.Online;

/// <summary>
/// S22: names compared as a reader would: without case, ™ ® ©, or
/// punctuation. A match ranks exact, then a name that starts with the
/// query, then one holding all its words; anything else doesn't match.
/// </summary>
internal static class OnlineNames
{
    public static string Normalize(string name)
    {
        StringBuilder key = new(name.Length);
        bool space = true;
        // The marks go first: compatibility normalizing turns ™ into "TM".
        string plain = name.Replace("™", "", StringComparison.Ordinal).Replace("®", "", StringComparison.Ordinal)
            .Replace("©", "", StringComparison.Ordinal);
        foreach (char c in plain.Normalize(NormalizationForm.FormKC).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                key.Append(c);
                space = false;
            }
            else if (!space)
            {
                key.Append(' ');
                space = true;
            }
        }
        return key.ToString().TrimEnd();
    }

    public static int Rank(string name, string key, string prefix, string[] words)
    {
        if (name == key) return 0;
        if (name.StartsWith(prefix, StringComparison.Ordinal)) return 1;
        string spaced = " " + name + " ";
        return words.All(word => spaced.Contains(word, StringComparison.Ordinal)) ? 2 : -1;
    }
}
