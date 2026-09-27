using System.Security.Cryptography;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using DesktopGuides.Core.Paths;

namespace DesktopGuides.Infrastructure.Import;

public sealed class StaticHtmlDependencyScanner
{
    private readonly StaticHtmlScanLimits limits;

    public StaticHtmlDependencyScanner(StaticHtmlScanLimits? limits = null)
    {
        this.limits = limits ?? new StaticHtmlScanLimits();
        if (this.limits.MaxEntryBytes < 1 ||
            this.limits.MaxAssetBytes < 1 ||
            this.limits.MaxTotalBytes < 1 ||
            this.limits.MaxAssets < 1 ||
            this.limits.MaxReferences < 1 ||
            this.limits.MaxCssDepth < 1 ||
            this.limits.MaxCssRules < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limits), "HTML scan limits must be positive.");
        }
    }

    public Task<StaticHtmlManifest> ScanAsync(
        string entryRelativePath,
        IStaticHtmlAssetSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ManagedRelativePath.Parse(entryRelativePath);
        if (!IsExtension(entryRelativePath, ".html", ".htm"))
        {
            throw new InvalidDataException("HTML entry must end in .html or .htm.");
        }
        return new ScanSession(limits, source, cancellationToken)
            .ScanAsync(entryRelativePath);
    }

    private static bool IsExtension(string path, params string[] extensions) =>
        extensions.Contains(
            Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private sealed class ScanSession(
        StaticHtmlScanLimits limits,
        IStaticHtmlAssetSource source,
        CancellationToken cancellationToken)
    {
        private readonly Dictionary<string, StaticAsset> assets =
            new(StringComparer.Ordinal);
        private readonly List<StaticAssetReference> references = [];
        private readonly HtmlParser htmlParser = new();
        private readonly CssParser cssParser = new();
        private long totalBytes;
        private int cssRuleCount;

        public async Task<StaticHtmlManifest> ScanAsync(string entryPath)
        {
            byte[]? entry = await ReadAssetAsync(
                entryPath, StaticAssetKind.EntryHtml);
            if (entry is null)
            {
                throw new FileNotFoundException("HTML entry was not found.");
            }
            using MemoryStream entryStream = new(entry, writable: false);
            IDocument document = htmlParser.ParseDocument(entryStream);
            foreach (IElement element in document.QuerySelectorAll("*"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = element.LocalName;
                if (name == "img")
                {
                    await ReadHtmlReferenceAsync(
                        entryPath, element.GetAttribute("src"),
                        StaticAssetKind.Image, 0);
                    foreach (string target in ParseSrcSet(
                        element.GetAttribute("srcset")))
                    {
                        await ReadHtmlReferenceAsync(
                            entryPath, target, StaticAssetKind.Image, 0);
                    }
                }
                else if (name == "source")
                {
                    foreach (string target in ParseSrcSet(
                        element.GetAttribute("srcset")))
                    {
                        await ReadHtmlReferenceAsync(
                            entryPath, target, StaticAssetKind.Image, 0);
                    }
                    if (element.HasAttribute("src"))
                    {
                        AddUnsupportedReference(
                            entryPath, element.GetAttribute("src")!);
                    }
                }
                else if (name == "link" &&
                    HasRel(element.GetAttribute("rel"), "stylesheet"))
                {
                    await ReadHtmlReferenceAsync(
                        entryPath, element.GetAttribute("href"),
                        StaticAssetKind.StyleSheet, 1);
                }
                else if ((name is "link" or "base") &&
                    element.GetAttribute("href") is string unsupportedHref)
                {
                    AddUnsupportedReference(entryPath, unsupportedHref);
                }
                else if (element.HasAttribute("src"))
                {
                    AddUnsupportedReference(
                        entryPath, element.GetAttribute("src")!);
                }

                if (name == "style")
                {
                    await ScanCssAsync(
                        entryPath, element.TextContent, 0);
                }
                if (element.GetAttribute("style") is string inlineStyle)
                {
                    await ScanCssUrlsAsync(
                        entryPath, inlineStyle, inlineStyle: true);
                }
            }
            return new StaticHtmlManifest(
                assets.Values.OrderBy(asset => asset.RelativePath,
                    StringComparer.Ordinal).ToArray(),
                references.ToArray(),
                totalBytes);
        }

        private static bool HasRel(string? rel, string token) =>
            rel?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Contains(token, StringComparer.OrdinalIgnoreCase) ?? false;

        private static IEnumerable<string> ParseSrcSet(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield break;
            }
            for (int index = 0; index < value.Length;)
            {
                while (index < value.Length &&
                    (char.IsWhiteSpace(value[index]) || value[index] == ','))
                {
                    index++;
                }
                int start = index;
                while (index < value.Length && !char.IsWhiteSpace(value[index]))
                {
                    index++;
                }
                string target = value[start..index].TrimEnd(',');
                if (target.Length > 0)
                {
                    yield return target;
                }
                if (index > start && value[index - 1] == ',')
                {
                    continue;
                }
                int parentheses = 0;
                while (index < value.Length)
                {
                    if (value[index] == '(') parentheses++;
                    if (value[index] == ')' && parentheses > 0) parentheses--;
                    if (value[index++] == ',' && parentheses == 0) break;
                }
            }
        }

        private async Task ScanCssAsync(
            string sourcePath,
            string css,
            int depth)
        {
            ICssStyleSheet sheet = cssParser.ParseStyleSheet(css);
            CountRules(sheet.Rules);
            foreach (ICssRule rule in sheet.Rules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (rule is ICssImportRule import)
                {
                    await ReadHtmlReferenceAsync(
                        sourcePath, import.Href,
                        StaticAssetKind.StyleSheet, depth + 1);
                }
            }
            await ScanCssUrlsAsync(sourcePath, css);
        }

        private void CountRules(IEnumerable<ICssRule> rules)
        {
            Stack<ICssRule> pending = new(rules);
            while (pending.Count > 0)
            {
                ICssRule rule = pending.Pop();
                if (++cssRuleCount > limits.MaxCssRules)
                {
                    throw Limit(
                        StaticScanLimit.CssRuleCount, "Too many CSS rules.");
                }
                if (rule is ICssGroupingRule group)
                {
                    foreach (ICssRule child in group.Rules)
                    {
                        pending.Push(child);
                    }
                }
                if (rule is ICssStyleRule style)
                {
                    foreach (ICssRule child in style.Rules)
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        private async Task ScanCssUrlsAsync(
            string sourcePath,
            string css,
            bool inlineStyle = false)
        {
            foreach (string target in CssUrlReferences.ExtractDeclarations(
                css, inlineStyle))
            {
                await ReadHtmlReferenceAsync(
                    sourcePath, target, StaticAssetKind.Image, 0);
            }
        }

        private void AddUnsupportedReference(string sourcePath, string target)
        {
            EnsureReferenceBudget();
            (string? path, StaticReferenceStatus? issue) =
                NormalizeReference(sourcePath, target);
            references.Add(new StaticAssetReference(
                sourcePath, target, path, StaticAssetKind.Other,
                issue ?? StaticReferenceStatus.Unsupported));
        }

        private async Task ReadHtmlReferenceAsync(
            string sourcePath,
            string? raw,
            StaticAssetKind kind,
            int depth)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }
            EnsureReferenceBudget();
            (string? path, StaticReferenceStatus? issue) =
                NormalizeReference(sourcePath, raw);
            if (issue is not null)
            {
                references.Add(new StaticAssetReference(
                    sourcePath, raw, path, kind, issue.Value));
                return;
            }
            if (path is null)
            {
                return; // A fragment-only reference needs no file.
            }
            bool supported = kind switch
            {
                StaticAssetKind.StyleSheet =>
                    IsExtension(path, ".css"),
                StaticAssetKind.Image =>
                    IsExtension(path, ".png", ".jpg", ".jpeg", ".gif", ".webp"),
                _ => false
            };
            if (!supported)
            {
                references.Add(new StaticAssetReference(
                    sourcePath, raw, path, kind,
                    StaticReferenceStatus.Unsupported));
                return;
            }
            if (assets.TryGetValue(path, out StaticAsset? existing))
            {
                references.Add(new StaticAssetReference(
                    sourcePath, raw, path, kind,
                    existing.Kind == kind
                        ? StaticReferenceStatus.Included
                        : StaticReferenceStatus.Unsupported));
                return;
            }
            if (kind == StaticAssetKind.StyleSheet && depth > limits.MaxCssDepth)
            {
                throw Limit(
                    StaticScanLimit.CssDepth, "CSS import depth exceeded.");
            }
            byte[]? bytes = await ReadAssetAsync(path, kind);
            if (bytes is null)
            {
                references.Add(new StaticAssetReference(
                    sourcePath, raw, path, kind,
                    StaticReferenceStatus.Missing));
                return;
            }
            references.Add(new StaticAssetReference(
                sourcePath, raw, path, kind,
                StaticReferenceStatus.Included));
            if (kind == StaticAssetKind.StyleSheet)
            {
                await ScanCssAsync(path, CssTextDecoder.Decode(bytes), depth);
            }
        }

        private void EnsureReferenceBudget()
        {
            if (references.Count >= limits.MaxReferences)
            {
                throw Limit(
                    StaticScanLimit.ReferenceCount, "Too many asset references.");
            }
        }

        private async Task<byte[]?> ReadAssetAsync(
            string path,
            StaticAssetKind kind)
        {
            await using Stream? input = await source.OpenReadAsync(
                path, cancellationToken);
            if (input is null)
            {
                return null;
            }
            if (assets.Count - 1 >= limits.MaxAssets &&
                kind != StaticAssetKind.EntryHtml)
            {
                throw Limit(
                    StaticScanLimit.AssetCount, "Too many static assets.");
            }
            long maximum = kind == StaticAssetKind.EntryHtml
                ? limits.MaxEntryBytes : limits.MaxAssetBytes;
            using MemoryStream output = new();
            byte[] buffer = new byte[64 * 1024];
            while (true)
            {
                int count = await input.ReadAsync(
                    buffer.AsMemory(), cancellationToken);
                if (count == 0) break;
                if (output.Length + count > maximum)
                {
                    throw Limit(
                        kind == StaticAssetKind.EntryHtml
                            ? StaticScanLimit.EntryBytes
                            : StaticScanLimit.AssetBytes,
                        "Static asset exceeds its file size limit.");
                }
                if (totalBytes + output.Length + count > limits.MaxTotalBytes)
                {
                    throw Limit(
                        StaticScanLimit.TotalBytes, "HTML tree is too large.");
                }
                output.Write(buffer, 0, count);
            }
            byte[] bytes = output.ToArray();
            totalBytes += bytes.Length;
            assets.Add(path, new StaticAsset(
                path, kind, bytes.LongLength,
                Convert.ToHexStringLower(SHA256.HashData(bytes))));
            return bytes;
        }

        private static (string? Path, StaticReferenceStatus? Issue)
            NormalizeReference(string sourcePath, string raw)
        {
            string target = raw.Trim();
            if (target.StartsWith('#'))
            {
                return (null, null);
            }
            if (target.StartsWith("//", StringComparison.Ordinal) ||
                target.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
            {
                return (null, StaticReferenceStatus.Remote);
            }
            if (Uri.TryCreate(target, UriKind.Absolute, out _))
            {
                return (null, StaticReferenceStatus.Unsupported);
            }
            int queryOrFragment = target.IndexOfAny(['?', '#']);
            if (queryOrFragment >= 0)
            {
                target = target[..queryOrFragment];
            }
            if (target.Length == 0)
            {
                return (null, null);
            }
            if (target.StartsWith('/') ||
                target.Contains('\\') ||
                target.Contains('%') ||
                target.Contains(':'))
            {
                return (null, StaticReferenceStatus.Unsafe);
            }
            List<string> segments = [];
            int slash = sourcePath.LastIndexOf('/');
            if (slash >= 0)
            {
                segments.AddRange(sourcePath[..slash].Split('/'));
            }
            foreach (string segment in target.Split('/'))
            {
                if (segment == ".") continue;
                if (segment is "" or "..")
                {
                    return (null, StaticReferenceStatus.Unsafe);
                }
                segments.Add(segment);
            }
            string normalized = string.Join('/', segments);
            try
            {
                return (ManagedRelativePath.Parse(normalized), null);
            }
            catch (InvalidDataException)
            {
                return (null, StaticReferenceStatus.Unsafe);
            }
        }

        private static StaticHtmlScanException Limit(
            StaticScanLimit limit,
            string message) => new(limit, message);
    }
}
