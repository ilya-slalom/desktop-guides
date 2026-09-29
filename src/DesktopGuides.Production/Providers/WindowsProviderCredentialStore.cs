using System.Runtime.InteropServices.WindowsRuntime;
using DesktopGuides.Infrastructure.Providers;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage.Streams;

namespace DesktopGuides.Production.Providers;

internal static class WindowsProviderCredentialStore
{
    public static ProviderCredentialFile Create(string localStatePath) =>
        new(Path.Combine(localStatePath, "providers.bin"), ProtectAsync, UnprotectAsync);

    private static async Task<byte[]> ProtectAsync(byte[] plain)
    {
        IBuffer data = await new DataProtectionProvider("LOCAL=user").ProtectAsync(plain.AsBuffer());
        return data.ToArray();
    }

    private static async Task<byte[]> UnprotectAsync(byte[] protectedData)
    {
        IBuffer data = await new DataProtectionProvider().UnprotectAsync(protectedData.AsBuffer());
        return data.ToArray();
    }
}
