using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage.Streams;

namespace DesktopGuides.Production.Providers;

internal sealed class WindowsProviderCredentialStore(string localStatePath) : IProviderCredentialStore
{
    private const int MaxProtectedBytes = 64 * 1024;
    private readonly string path = Path.Combine(localStatePath, "providers.bin");
    private readonly SemaphoreSlim gate = new(1, 1);
    private ProviderCredentials? cached;

    public async Task<ProviderCredentials> LoadAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            return cached ??= await ReadAsync(token);
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
                IBuffer protectedData = await new DataProtectionProvider("LOCAL=user").ProtectAsync(plain.AsBuffer());
                await File.WriteAllBytesAsync(temp, protectedData.ToArray(), token);
                File.Move(temp, path, overwrite: true);
                cached = ProviderCredentialBlob.Parse(plain);
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
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ProviderCredentials> ReadAsync(CancellationToken token)
    {
        try
        {
            FileInfo file = new(path);
            if (!file.Exists || file.Length is 0 or > MaxProtectedBytes) return ProviderCredentials.None;
            byte[] protectedData = await File.ReadAllBytesAsync(path, token);
            IBuffer plain = await new DataProtectionProvider().UnprotectAsync(protectedData.AsBuffer());
            byte[] bytes = plain.ToArray();
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
            // A blob this user can't unprotect or parse counts as not configured.
            return ProviderCredentials.None;
        }
    }
}
