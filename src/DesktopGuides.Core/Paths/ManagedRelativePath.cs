namespace DesktopGuides.Core.Paths;

public static class ManagedRelativePath
{
    private static readonly HashSet<string> ReservedNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

    public static string Parse(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.StartsWith('/') ||
            candidate.Any(character =>
                character is '\\' or ':' or '%' or '\0' or '<' or '>' or '"' or '|' or '?' or '*' ||
                char.IsControl(character) || char.IsSurrogate(character)))
        {
            throw new InvalidDataException("Managed content path is not a safe relative path.");
        }

        foreach (string segment in candidate.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." ||
                segment.EndsWith('.') || segment.EndsWith(' ') ||
                ReservedNames.Contains(segment.Split('.')[0]))
            {
                throw new InvalidDataException("Managed content path has an unsafe segment.");
            }
        }

        return candidate;
    }
}
