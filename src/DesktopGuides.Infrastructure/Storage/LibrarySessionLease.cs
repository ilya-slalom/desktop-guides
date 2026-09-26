namespace DesktopGuides.Infrastructure.Storage;

/// <summary>
/// Keeps repository initialization, recovery, and writes in one app process
/// until that process has drained and disposed its repository.
/// </summary>
public sealed class LibrarySessionLease : IAsyncDisposable
{
    private const int SharingViolation = 32;
    private const int LockViolation = 33;
    private readonly FileStream stream;

    private LibrarySessionLease(FileStream stream) => this.stream = stream;

    public static async Task<LibrarySessionLease> AcquireAsync(
        string dataRoot, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        string path = Path.Combine(Path.GetFullPath(dataRoot), "library.session.lock");
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return new LibrarySessionLease(new FileStream(
                    path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException error) when (OperatingSystem.IsWindows() &&
                (error.HResult & 0xffff) is SharingViolation or LockViolation)
            {
                await Task.Delay(100, token);
            }
        }
    }

    public ValueTask DisposeAsync() => stream.DisposeAsync();
}
