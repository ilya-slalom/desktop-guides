using System.Security.Cryptography;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Infrastructure.Providers;

public sealed class ProviderCredentialFile(
    string path, Func<byte[], Task<byte[]>> protect, Func<byte[], Task<byte[]>> unprotect) : IProviderCredentialStore
{
    private const int MaxProtectedBytes = 64 * 1024;
    private readonly SemaphoreSlim gate = new(1, 1);
    private ProviderCredentials? cached;

    public bool LastReadFailed { get; private set; }

    public async Task<ProviderCredentials> LoadAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (cached is { } known) return known;
            cached = await ReadAsync(token);
            LastReadFailed = cached is null;
            return cached ?? ProviderCredentials.None;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(ProviderCredentials credentials, CancellationToken token)
    {
        byte[] plain = ProviderCredentialBlob.Format(credentials);
        string temp = path + ".tmp";
        try
        {
            await gate.WaitAsync(token);
            try
            {
                byte[] protectedData = await protect(plain);
                await File.WriteAllBytesAsync(temp, protectedData, token);
                File.Move(temp, path, overwrite: true);
                cached = ProviderCredentialBlob.Parse(plain);
                LastReadFailed = false;
            }
            finally
            {
                File.Delete(temp);
                gate.Release();
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public async Task ClearAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            File.Delete(path);
            cached = ProviderCredentials.None;
            LastReadFailed = false;
        }
        finally
        {
            gate.Release();
        }
    }

    // Returns null when the file exists but can't be read right now, so the next load tries again.
    private async Task<ProviderCredentials?> ReadAsync(CancellationToken token)
    {
        byte[] protectedData;
        try
        {
            FileInfo file = new(path);
            if (!file.Exists || file.Length is 0 or > MaxProtectedBytes) return ProviderCredentials.None;
            protectedData = await File.ReadAllBytesAsync(path, token);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return ProviderCredentials.None;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Typically another process (antivirus, backup) holding the file.
            return null;
        }
        try
        {
            byte[] bytes = await unprotect(protectedData);
            try
            {
                return ProviderCredentialBlob.Parse(bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A blob this user can't unprotect or parse counts as not configured. DPAPI
            // LOCAL=user failures (other user, lost key, damaged blob) don't clear up on retry.
            return ProviderCredentials.None;
        }
    }
}
