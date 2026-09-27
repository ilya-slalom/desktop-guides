using System.Security.Cryptography;

namespace DesktopGuides.Infrastructure.Import;

public sealed class StaticHtmlImportValidator(
    StaticHtmlScanLimits? limits = null)
{
    private readonly StaticHtmlDependencyScanner scanner = new(limits);

    public async Task<StaticHtmlImportPreview> PreviewAsync(
        string selectedEntryPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(selectedEntryPath) ||
            !Path.IsPathFullyQualified(selectedEntryPath))
        {
            throw new ArgumentException(
                "An absolute HTML entry path is required.",
                nameof(selectedEntryPath));
        }
        string entryPath = Path.GetFullPath(selectedEntryPath);
        string root = Path.GetDirectoryName(entryPath)!;
        string entryName = Path.GetFileName(entryPath);
        RootedStaticHtmlAssetSource source = new(root);
        StaticHtmlManifest manifest = await scanner.ScanAsync(
            entryName, source, cancellationToken);
        RejectCaseCollisions(manifest);
        StaticHtmlPreviewWarning[] warnings = manifest.References
            .Where(reference => reference.Status != StaticReferenceStatus.Included)
            .Select(ToWarning)
            .ToArray();
        return new StaticHtmlImportPreview(
            root, entryName, manifest, warnings);
    }

    public async Task VerifyStagedAsync(
        StaticHtmlImportPreview preview,
        string stagedRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (string.IsNullOrWhiteSpace(stagedRoot) ||
            !Path.IsPathFullyQualified(stagedRoot))
        {
            throw new ArgumentException(
                "An absolute staged HTML directory is required.",
                nameof(stagedRoot));
        }
        RejectCaseCollisions(preview.Manifest);
        RootedStaticHtmlAssetSource source;
        RootedStaticHtmlAssetSource stage;
        try
        {
            source = new RootedStaticHtmlAssetSource(preview.SourceRoot);
        }
        catch (DirectoryNotFoundException)
        {
            throw Changed(
                StaticHtmlValidationIssue.SourceChanged,
                "HTML source directory was removed after preview.");
        }
        try
        {
            stage = new RootedStaticHtmlAssetSource(stagedRoot);
        }
        catch (DirectoryNotFoundException)
        {
            throw Changed(
                StaticHtmlValidationIssue.StageChanged,
                "Staged HTML directory is missing.");
        }

        foreach (StaticAsset asset in preview.Manifest.Assets)
        {
            await VerifyOneAsync(
                source, asset, StaticHtmlValidationIssue.SourceChanged,
                cancellationToken);
            await VerifyOneAsync(
                stage, asset, StaticHtmlValidationIssue.StageChanged,
                cancellationToken);
        }
    }

    private static async Task VerifyOneAsync(
        IStaticHtmlAssetSource source,
        StaticAsset asset,
        StaticHtmlValidationIssue changedIssue,
        CancellationToken cancellationToken)
    {
        await using Stream? input = await source.OpenReadAsync(
            asset.RelativePath, cancellationToken);
        if (input is null)
        {
            throw Changed(changedIssue, "An HTML asset is missing after preview.");
        }
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        long length = 0;
        while (true)
        {
            int readLength = (int)Math.Min(
                buffer.Length, asset.ByteCount - length + 1);
            int count = await input.ReadAsync(
                buffer.AsMemory(0, readLength), cancellationToken);
            if (count == 0) break;
            if (count > asset.ByteCount - length)
            {
                throw Changed(changedIssue, "An HTML asset grew after preview.");
            }
            length += count;
            hash.AppendData(buffer, 0, count);
        }
        if (length != asset.ByteCount ||
            !string.Equals(
                Convert.ToHexStringLower(hash.GetHashAndReset()),
                asset.Sha256, StringComparison.Ordinal))
        {
            throw Changed(changedIssue, "An HTML asset changed after preview.");
        }
    }

    private static void RejectCaseCollisions(StaticHtmlManifest manifest)
    {
        Dictionary<string, string> paths =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (StaticAsset asset in manifest.Assets)
        {
            if (paths.TryGetValue(asset.RelativePath, out string? earlier) &&
                !string.Equals(
                    earlier, asset.RelativePath, StringComparison.Ordinal))
            {
                throw Changed(
                    StaticHtmlValidationIssue.CaseCollision,
                    "HTML assets have case-colliding destination names.");
            }
            paths[asset.RelativePath] = asset.RelativePath;
        }
    }

    private static StaticHtmlPreviewWarning ToWarning(
        StaticAssetReference reference) => new(
            reference.Status,
            reference.SourceRelativePath,
            reference.RawTarget,
            reference.RelativePath,
            reference.Status switch
            {
                StaticReferenceStatus.Missing =>
                    "A local asset is missing and will not appear offline.",
                StaticReferenceStatus.Remote =>
                    "A remote asset will be blocked in the offline reader.",
                StaticReferenceStatus.Unsafe =>
                    "An unsafe asset path will be blocked.",
                StaticReferenceStatus.Unsupported =>
                    "An unsupported asset will be blocked.",
                _ => throw new ArgumentOutOfRangeException(nameof(reference))
            });

    private static StaticHtmlValidationException Changed(
        StaticHtmlValidationIssue issue,
        string message) => new(issue, message);
}
