using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using DesktopGuides.Core.Paths;
using Microsoft.Win32.SafeHandles;

namespace DesktopGuides.Infrastructure.Import;

/// <summary>
/// Opens only files within one selected HTML directory. The opened Windows
/// handle is checked after path traversal so a swapped link cannot redirect
/// a read outside the requested path.
/// </summary>
internal sealed class RootedStaticHtmlAssetSource : IStaticHtmlAssetSource
{
    private readonly string root;
    private readonly string rootPrefix;
    private readonly string? entryAlias;
    private readonly string? sourceEntryName;

    public RootedStaticHtmlAssetSource(
        string absoluteRoot,
        string? entryAlias = null,
        string? sourceEntryName = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The HTML source boundary requires Windows.");
        }
        if (string.IsNullOrWhiteSpace(absoluteRoot) ||
            !Path.IsPathFullyQualified(absoluteRoot))
        {
            throw new ArgumentException(
                "An absolute HTML source directory is required.",
                nameof(absoluteRoot));
        }
        if ((entryAlias is null) != (sourceEntryName is null))
        {
            throw new ArgumentException(
                "The entry alias and source name must be supplied together.");
        }
        if (entryAlias is not null)
        {
            ManagedRelativePath.Parse(entryAlias);
            if (sourceEntryName != Path.GetFileName(sourceEntryName))
            {
                throw new ArgumentException(
                    "The selected HTML entry must be in the source root.",
                    nameof(sourceEntryName));
            }
            // Only a literal percent is allowed beyond managed path rules.
            ManagedRelativePath.Parse(sourceEntryName!.Replace('%', '_'));
        }

        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(absoluteRoot));
        rootPrefix = Path.EndsInDirectorySeparator(root)
            ? root : root + Path.DirectorySeparatorChar;
        this.entryAlias = entryAlias;
        this.sourceEntryName = sourceEntryName;
        if (!CheckUnlinkedPath(root) ||
            (File.GetAttributes(root) & FileAttributes.Directory) == 0)
        {
            throw new DirectoryNotFoundException(
                "HTML source directory was not found.");
        }
    }

    public ValueTask<Stream?> OpenReadAsync(
        string safeRelativePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalized = ManagedRelativePath.Parse(safeRelativePath);
        string sourceRelativePath = string.Equals(
            normalized, entryAlias, StringComparison.Ordinal)
            ? sourceEntryName! : normalized;
        string requested = Path.GetFullPath(Path.Combine(
            root, sourceRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!requested.StartsWith(
            rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw UnsafePath();
        }
        if (!CheckUnlinkedPath(requested))
        {
            return ValueTask.FromResult<Stream?>(null);
        }
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(requested);
        }
        catch (FileNotFoundException)
        {
            return ValueTask.FromResult<Stream?>(null);
        }
        catch (DirectoryNotFoundException)
        {
            return ValueTask.FromResult<Stream?>(null);
        }
        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw UnsafePath();
        }

        SafeFileHandle handle;
        try
        {
            handle = File.OpenHandle(
                requested, FileMode.Open, FileAccess.Read, FileShare.Read,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException)
        {
            return ValueTask.FromResult<Stream?>(null);
        }
        catch (DirectoryNotFoundException)
        {
            return ValueTask.FromResult<Stream?>(null);
        }

        try
        {
            if (!string.Equals(
                GetFinalPath(handle), requested,
                StringComparison.OrdinalIgnoreCase))
            {
                throw UnsafePath();
            }
            Stream stream = new FileStream(
                handle, FileAccess.Read, 64 * 1024, isAsync: true);
            return ValueTask.FromResult<Stream?>(stream);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static bool CheckUnlinkedPath(string path)
    {
        string volume = Path.GetPathRoot(path)!;
        string current = volume;
        foreach (string segment in Path.GetRelativePath(volume, path)
            .Split(Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                if ((File.GetAttributes(current) &
                    FileAttributes.ReparsePoint) != 0)
                {
                    throw UnsafePath();
                }
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
        }
        return true;
    }

    private static string GetFinalPath(SafeFileHandle handle)
    {
        uint required = GetFinalPathNameByHandle(handle, null, 0, 0);
        if (required == 0 || required > short.MaxValue)
        {
            throw FinalPathError();
        }
        StringBuilder buffer = new((int)required);
        uint written = GetFinalPathNameByHandle(
            handle, buffer, (uint)buffer.Capacity, 0);
        if (written == 0 || written >= buffer.Capacity)
        {
            throw FinalPathError();
        }
        string path = buffer.ToString();
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            path = @"\\" + path[8..];
        }
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            path = path[4..];
        }
        return Path.GetFullPath(path);
    }

    private static IOException FinalPathError() => new(
        "Opened HTML asset path could not be verified.",
        new Win32Exception(Marshal.GetLastPInvokeError()));

    private static StaticHtmlValidationException UnsafePath() => new(
        StaticHtmlValidationIssue.UnsafePath,
        "HTML asset path crosses a link or leaves its selected root.");

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        StringBuilder? path,
        uint pathLength,
        uint flags);
}
