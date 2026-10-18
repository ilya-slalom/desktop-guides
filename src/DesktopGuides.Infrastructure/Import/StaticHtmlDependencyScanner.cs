using System.Security.Cryptography;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using DesktopGuides.Core.Html;
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
            this.limits.MaxCssRules < 1 ||
            this.limits.MaxPages < 1 ||
            this.limits.MaxGuideBytes < 1 ||
            this.limits.MaxGuideAssets < 1)
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
        private readonly Dictionary<string, List<string>> customPropertyImages =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> customPropertyVariables =
            new(StringComparer.Ordinal);
        private readonly HashSet<(CssDeclarationReferenceKind Kind,
            string Property, string Value)> seenVariableDefinitions = [];
        private readonly List<(string SourcePath, string Name)> variableUses = [];
        private readonly HashSet<(string SourcePath, string Name)> seenVariableUses = [];
        private readonly HtmlParser htmlParser = new();
        private readonly CssParser cssParser = new();
        private long totalBytes;
        private int cssRuleCount;
        private long cssRuleCandidates;
        private int variableTokenCount;
        // T21.2: these budgets restart for each page; totalBytes and assets
        // cover the whole guide.
        private int pageReferenceStart;
        private int pageAssetStart;
        private long pageBytes;
        private const int PrefixChars = 256 * 1024;
        private const int CommentChars = 1024;
        private const int MaxCandidates = 2000;

        private sealed record PageScan(StaticPage Page, Uri? Url, IReadOnlyList<string> Links);

        public async Task<StaticHtmlManifest> ScanAsync(string entryPath)
        {
            PageScan entry = await ScanPageAsync(entryPath, StaticAssetKind.EntryHtml)
                ?? throw new FileNotFoundException("HTML entry was not found.");
            List<StaticPage> pages = [entry.Page];
            if (source is IStaticHtmlPageDirectory directory && entry.Url is Uri entryUrl)
            {
                await AddLinkedPagesAsync(directory, entryPath, entry, entryUrl, pages);
            }
            return new StaticHtmlManifest(
                assets.Values.OrderBy(asset => asset.RelativePath,
                    StringComparer.Ordinal).ToArray(),
                references.ToArray(),
                totalBytes,
                pages);
        }

        // Scans one page and its files under fresh per-page budgets. Null
        // when the page can't be opened.
        private async Task<PageScan?> ScanPageAsync(string pagePath, StaticAssetKind kind)
        {
            pageReferenceStart = references.Count;
            pageAssetStart = assets.Count;
            pageBytes = 0;
            cssRuleCount = 0;
            cssRuleCandidates = 0;
            variableTokenCount = 0;
            customPropertyImages.Clear();
            customPropertyVariables.Clear();
            seenVariableDefinitions.Clear();
            variableUses.Clear();
            seenVariableUses.Clear();

            byte[]? bytes = await ReadAssetAsync(pagePath, kind);
            if (bytes is null) return null;
            using MemoryStream stream = new(bytes, writable: false);
            IDocument document = htmlParser.ParseDocument(stream);
            List<string> links = [];
            foreach (IElement element in document.QuerySelectorAll("*"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (element.LocalName == "a" && element.GetAttribute("href") is string href)
                {
                    links.Add(href);
                }
                await ScanElementAsync(pagePath, element);
            }
            await ScanCustomPropertyUrlsAsync();
            Uri? url = SavedPageUrl.FromComment(Prefix(bytes, CommentChars)) ?? CanonicalOf(document);
            string? title = string.IsNullOrWhiteSpace(document.Title) ? null : document.Title.Trim();
            StaticAsset asset = assets[pagePath];
            return new PageScan(
                new StaticPage(asset.RelativePath, asset.RequestRelativePath, url?.AbsoluteUri, title), url, links);
        }

        private async Task ScanElementAsync(string pagePath, IElement element)
        {
            string name = element.LocalName;
            if (name == "img")
            {
                await ReadHtmlReferenceAsync(
                    pagePath, element.GetAttribute("src"),
                    StaticAssetKind.Image, 0);
                foreach (string target in ParseSrcSet(
                    element.GetAttribute("srcset")))
                {
                    await ReadHtmlReferenceAsync(
                        pagePath, target, StaticAssetKind.Image, 0);
                }
            }
            else if (name == "source")
            {
                foreach (string target in ParseSrcSet(
                    element.GetAttribute("srcset")))
                {
                    await ReadHtmlReferenceAsync(
                        pagePath, target, StaticAssetKind.Image, 0);
                }
                if (element.HasAttribute("src"))
                {
                    AddUnsupportedReference(
                        pagePath, element.GetAttribute("src")!);
                }
            }
            else if (name == "link" &&
                HasRel(element.GetAttribute("rel"), "stylesheet"))
            {
                await ReadHtmlReferenceAsync(
                    pagePath, element.GetAttribute("href"),
                    StaticAssetKind.StyleSheet, 1);
            }
            else if ((name is "link" or "base") &&
                element.GetAttribute("href") is string unsupportedHref)
            {
                AddUnsupportedReference(pagePath, unsupportedHref);
            }
            else if (element.HasAttribute("src"))
            {
                AddUnsupportedReference(
                    pagePath, element.GetAttribute("src")!);
            }

            if (name == "style")
            {
                await ScanCssAsync(
                    pagePath, element.TextContent, 0);
            }
            if (element.GetAttribute("style") is string inlineStyle)
            {
                await ScanCssUrlsAsync(
                    pagePath, inlineStyle, inlineStyle: true);
            }
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
            CheckCssRulePreflight(css);
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

        private void CheckCssRulePreflight(string css)
        {
            // An at-rule may have both an @ token and a block. Allow two
            // structural markers per configured rule before invoking the parser.
            long maximum = 2L * limits.MaxCssRules;
            bool statementStart = true;
            for (int index = 0; index < css.Length;)
            {
                if ((index & 4095) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                if (css[index] == '/' && index + 1 < css.Length &&
                    css[index + 1] == '*')
                {
                    int end = css.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    index = end < 0 ? css.Length : end + 2;
                    continue;
                }
                if (char.IsWhiteSpace(css[index]))
                {
                    index++;
                    continue;
                }
                if (css[index] is '\'' or '"')
                {
                    char quote = css[index++];
                    while (index < css.Length)
                    {
                        if (css[index] == '\\')
                        {
                            index = Math.Min(index + 2, css.Length);
                        }
                        else if (css[index++] == quote)
                        {
                            break;
                        }
                    }
                    statementStart = false;
                    continue;
                }
                if (css[index] == '\\')
                {
                    index = Math.Min(index + 2, css.Length);
                    statementStart = false;
                    continue;
                }
                char current = css[index++];
                if (current == '{' || (current == '@' && statementStart))
                {
                    if (++cssRuleCandidates > maximum)
                    {
                        throw Limit(
                            StaticScanLimit.CssRuleCount, "Too many CSS rules.");
                    }
                }
                statementStart = current is '{' or '}' or ';';
            }
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
            foreach (CssDeclarationReference reference in
                CssUrlReferences.ExtractDeclarations(css, inlineStyle))
            {
                if (reference.Kind == CssDeclarationReferenceKind.Image)
                {
                    await ReadHtmlReferenceAsync(
                        sourcePath, reference.Value, StaticAssetKind.Image, 0);
                    continue;
                }
                if (reference.CustomProperty is string property)
                {
                    if (!seenVariableDefinitions.Add(
                        (reference.Kind, property, reference.Value)))
                    {
                        continue;
                    }
                    ReserveVariableToken();
                    Dictionary<string, List<string>> map =
                        reference.Kind == CssDeclarationReferenceKind.CustomPropertyImage
                            ? customPropertyImages : customPropertyVariables;
                    AddValue(map, property, reference.Value);
                }
                else if (reference.Kind == CssDeclarationReferenceKind.VariableUse &&
                    seenVariableUses.Add((sourcePath, reference.Value)))
                {
                    ReserveVariableToken();
                    variableUses.Add((sourcePath, reference.Value));
                }
            }
        }

        private async Task ScanCustomPropertyUrlsAsync()
        {
            foreach (IGrouping<string, (string SourcePath, string Name)> sourceUses
                in variableUses.GroupBy(use => use.SourcePath,
                    StringComparer.Ordinal))
            {
                HashSet<string> visited = new(StringComparer.Ordinal);
                Queue<string> pending = new(
                    sourceUses.Select(use => use.Name));
                while (pending.TryDequeue(out string? name))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!visited.Add(name)) continue;
                    if (customPropertyImages.TryGetValue(name,
                        out List<string>? targets))
                    {
                        foreach (string target in targets)
                        {
                            await ReadHtmlReferenceAsync(
                                sourceUses.Key, target, StaticAssetKind.Image, 0);
                        }
                    }
                    if (customPropertyVariables.TryGetValue(name,
                        out List<string>? dependencies))
                    {
                        foreach (string dependency in dependencies)
                        {
                            pending.Enqueue(dependency);
                        }
                    }
                }
            }
        }

        private void ReserveVariableToken()
        {
            if (++variableTokenCount > limits.MaxReferences)
            {
                throw Limit(
                    StaticScanLimit.ReferenceCount,
                    "Too many CSS variable references.");
            }
        }

        private static void AddValue(
            Dictionary<string, List<string>> map,
            string key,
            string value)
        {
            if (!map.TryGetValue(key, out List<string>? values))
            {
                values = [];
                map.Add(key, values);
            }
            values.Add(value);
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
                // T21.2: a stylesheet is recognised by how the page uses it.
                StaticAssetKind.StyleSheet => true,
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
            if (kind == StaticAssetKind.StyleSheet)
            {
                byte[]? sheet = await ReadBytesAsync(path, kind);
                if (sheet is null)
                {
                    references.Add(new StaticAssetReference(sourcePath, raw, path, kind, StaticReferenceStatus.Missing));
                    return;
                }
                bool named = IsExtension(path, ".css");
                string? css = named ? CssTextDecoder.Decode(sheet) : ReadableCss(sheet);
                if (css is null)
                {
                    references.Add(new StaticAssetReference(sourcePath, raw, path, kind, StaticReferenceStatus.Invalid));
                    return;
                }
                Record(path, kind, sheet);
                references.Add(new StaticAssetReference(sourcePath, raw, path, kind, StaticReferenceStatus.Included));
                await ScanCssAsync(path, css, depth);
                return;
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
        }

        private static string Prefix(byte[] bytes, int chars) =>
            System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, chars));

        private static Uri? CanonicalOf(IDocument document) => SavedPageUrl.Parse(
            document.QuerySelectorAll("link")
                .FirstOrDefault(link => HasRel(link.GetAttribute("rel"), "canonical"))
                ?.GetAttribute("href"));

        // Indexes the folder's other saved pages by their recorded addresses,
        // then follows links breadth-first from the entry, in document order.
        private async Task AddLinkedPagesAsync(
            IStaticHtmlPageDirectory directory, string entryPath, PageScan entry, Uri entryUrl, List<StaticPage> pages)
        {
            string[] names = directory.ListPages()
                .Where(name => !string.Equals(name, entryPath, StringComparison.Ordinal) && IsSafeName(name))
                .Order(StringComparer.Ordinal)
                .Take(MaxCandidates)
                .ToArray();
            // The entry claims its own address first, so a second save of it
            // never joins as another page.
            Dictionary<string, string> byKey = new(StringComparer.Ordinal)
            {
                [SavedPageUrl.Key(entryUrl)] = entryPath
            };
            foreach (string name in names)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (Uri url in await ReadPageUrlsAsync(name))
                {
                    if (SavedPageUrl.SameHost(url, entryUrl)) byKey.TryAdd(SavedPageUrl.Key(url), name);
                }
            }
            HashSet<string> byName = new(names, StringComparer.Ordinal);
            HashSet<string> added = new(StringComparer.Ordinal) { entryPath };
            Queue<(Uri Base, IReadOnlyList<string> Links)> pending = new([(entryUrl, entry.Links)]);
            while (pending.TryDequeue(out (Uri Base, IReadOnlyList<string> Links) next))
            {
                foreach (string href in next.Links)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (PageFor(href, next.Base, entryUrl, byKey, byName) is not string name || !added.Add(name))
                    {
                        continue;
                    }
                    if (pages.Count >= limits.MaxPages)
                    {
                        throw Limit(StaticScanLimit.PageCount, "The guide has too many pages.");
                    }
                    if (await ScanPageAsync(name, StaticAssetKind.Page) is not PageScan page) continue;
                    pages.Add(page.Page);
                    pending.Enqueue((page.Url ?? next.Base, page.Links));
                }
            }
        }

        // A link names a candidate by its address or, when relative, by the
        // file name it points at.
        private static string? PageFor(
            string href, Uri baseUrl, Uri entryUrl, Dictionary<string, string> byKey, HashSet<string> byName)
        {
            string target = href.Trim();
            if (!Uri.TryCreate(target, UriKind.Absolute, out _))
            {
                int cut = target.IndexOfAny(['?', '#']);
                string file = Uri.UnescapeDataString(cut >= 0 ? target[..cut] : target);
                if (file.StartsWith("./", StringComparison.Ordinal)) file = file[2..];
                if (byName.Contains(file)) return file;
            }
            if (!Uri.TryCreate(baseUrl, target, out Uri? resolved) ||
                SavedPageUrl.Parse(resolved.AbsoluteUri) is not Uri url ||
                !SavedPageUrl.SameHost(url, entryUrl))
            {
                return null;
            }
            return byKey.GetValueOrDefault(SavedPageUrl.Key(url));
        }

        // Reads only the start of a candidate; it isn't part of the guide yet.
        // A candidate that can't be read (locked, denied, reached through a
        // link) is skipped: it isn't part of the guide, so it never blocks it.
        private async Task<IReadOnlyList<Uri>> ReadPageUrlsAsync(string name)
        {
            try
            {
                return await ReadPageUrlsUncheckedAsync(name);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return [];
            }
        }

        private async Task<IReadOnlyList<Uri>> ReadPageUrlsUncheckedAsync(string name)
        {
            await using Stream? input = await source.OpenReadAsync(name, cancellationToken);
            if (input is null) return [];
            byte[] buffer = new byte[PrefixChars];
            int length = 0;
            while (length < buffer.Length)
            {
                int count = await input.ReadAsync(buffer.AsMemory(length), cancellationToken);
                if (count == 0) break;
                length += count;
            }
            string prefix = System.Text.Encoding.UTF8.GetString(buffer, 0, length);
            List<Uri> urls = [];
            if (SavedPageUrl.FromComment(prefix[..Math.Min(prefix.Length, CommentChars)]) is Uri comment) urls.Add(comment);
            if (CanonicalOf(htmlParser.ParseDocument(prefix)) is Uri canonical) urls.Add(canonical);
            return urls;
        }

        private static bool IsSafeName(string name)
        {
            try
            {
                ManagedRelativePath.Parse(name);
                return !name.Contains('/') && IsExtension(name, ".html", ".htm");
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        // T21.2: a stylesheet found by role but not named .css must be text,
        // not a saved error page or binary data.
        private static string? ReadableCss(byte[] bytes)
        {
            if (Array.IndexOf(bytes, (byte)0) >= 0) return null;
            string css;
            try
            {
                css = CssTextDecoder.Decode(bytes);
            }
            catch (InvalidDataException)
            {
                return null;
            }
            return css.TrimStart().StartsWith('<') ? null : css;
        }

        private void EnsureReferenceBudget()
        {
            if (references.Count - pageReferenceStart >= limits.MaxReferences)
            {
                throw Limit(
                    StaticScanLimit.ReferenceCount, "Too many asset references.");
            }
        }

        private async Task<byte[]?> ReadAssetAsync(
            string path,
            StaticAssetKind kind)
        {
            byte[]? bytes = await ReadBytesAsync(path, kind);
            if (bytes is not null) Record(path, kind, bytes);
            return bytes;
        }

        // Reads a file within the page's and the guide's budgets, without
        // recording it.
        private async Task<byte[]?> ReadBytesAsync(
            string path,
            StaticAssetKind kind)
        {
            await using Stream? input = await source.OpenReadAsync(
                path, cancellationToken);
            if (input is null)
            {
                return null;
            }
            bool page = kind is StaticAssetKind.EntryHtml or StaticAssetKind.Page;
            if (assets.Count - pageAssetStart - 1 >= limits.MaxAssets && !page)
            {
                throw Limit(
                    StaticScanLimit.AssetCount, "Too many static assets.");
            }
            if (assets.Count >= limits.MaxGuideAssets)
            {
                throw Limit(
                    StaticScanLimit.GuideAssetCount, "The guide has too many files.");
            }
            long maximum = page ? limits.MaxEntryBytes : limits.MaxAssetBytes;
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
                        page
                            ? StaticScanLimit.EntryBytes
                            : StaticScanLimit.AssetBytes,
                        "Static asset exceeds its file size limit.");
                }
                if (pageBytes + output.Length + count > limits.MaxTotalBytes)
                {
                    throw Limit(
                        StaticScanLimit.TotalBytes, "HTML tree is too large.");
                }
                if (totalBytes + output.Length + count > limits.MaxGuideBytes)
                {
                    throw Limit(
                        StaticScanLimit.GuideBytes, "The guide is too large.");
                }
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }

        private void Record(string path, StaticAssetKind kind, byte[] bytes)
        {
            totalBytes += bytes.Length;
            pageBytes += bytes.Length;
            string requestPath = source is IStaticHtmlRootPathMap pathMap
                ? pathMap.ToRequestPath(path) : path;
            assets.Add(path, new StaticAsset(
                path, kind, bytes.LongLength,
                Convert.ToHexStringLower(SHA256.HashData(bytes)),
                requestPath));
        }

        private (string? Path, StaticReferenceStatus? Issue)
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
                string managedSegment = segment;
                if (segments.Count == 0 &&
                    source is IStaticHtmlRootPathMap pathMap)
                {
                    if (pathMap.TryMapRootSegment(
                        segment, out string mappedSegment))
                    {
                        managedSegment = mappedSegment;
                    }
                    else if (pathMap.IsManagedRootAlias(segment))
                    {
                        return (null, StaticReferenceStatus.Unsafe);
                    }
                }
                if (managedSegment.Contains('%'))
                {
                    return (null, StaticReferenceStatus.Unsafe);
                }
                segments.Add(managedSegment);
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
