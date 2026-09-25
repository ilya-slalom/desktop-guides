namespace DesktopGuides.Core.Paths;

public static class ManagedRelativePath
{
    private static readonly HashSet<string> ReservedNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM0",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            // Windows also treats superscript digits as device-number suffixes.
            "COM¹", "COM²", "COM³",
            "LPT0",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
            "LPT¹", "LPT²", "LPT³"
        };

    public static string Parse(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.StartsWith('/') ||
            HasUnsafeCharacter(candidate))
        {
            throw new InvalidDataException("Managed content path is not a safe relative path.");
        }

        foreach (string segment in candidate.Split('/'))
        {
            string stem = segment.Split('.')[0];
            if (segment.Length == 0 || segment is "." or ".." ||
                segment.EndsWith('.') || segment.EndsWith(' ') ||
                stem.EndsWith(' ') || ReservedNames.Contains(stem))
            {
                throw new InvalidDataException("Managed content path has an unsafe segment.");
            }
        }

        return candidate;
    }

    private static bool HasUnsafeCharacter(string candidate)
    {
        for (int index = 0; index < candidate.Length; index++)
        {
            char character = candidate[index];
            if (char.IsHighSurrogate(character))
            {
                if (++index == candidate.Length || !char.IsLowSurrogate(candidate[index]))
                {
                    return true;
                }
                continue;
            }

            if (char.IsLowSurrogate(character) ||
                character is '\\' or ':' or '%' or '\0' or '<' or '>' or '"' or '|' or '?' or '*' ||
                char.IsControl(character))
            {
                return true;
            }
        }
        return false;
    }
}
