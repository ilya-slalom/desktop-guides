using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Providers;

public sealed class ProviderCredentialFileTests : IDisposable
{
    private static readonly ProviderCredentials Full = new(new IgdbCredentials("abc123", "s3cret-value"), "sgdb-key-1");
    private readonly string root = Directory.CreateTempSubdirectory("dg-credentials-").FullName;
    private string FilePath => Path.Combine(root, "providers.bin");

    // Stand-in for DPAPI: reversible, and never leaves plaintext on disk.
    private static Task<byte[]> Flip(byte[] data) => Task.FromResult(data.Select(b => (byte)(b ^ 0x5A)).ToArray());

    private ProviderCredentialFile Create(Func<byte[], Task<byte[]>>? unprotect = null) =>
        new(FilePath, Flip, unprotect ?? Flip);

    [Fact]
    public async Task SavedCredentialsLoadInANewInstanceAndAreNotStoredInPlaintext()
    {
        await Create().SaveAsync(Full, CancellationToken.None);

        ProviderCredentialFile reopened = Create();
        Assert.Equal(Full, await reopened.LoadAsync(CancellationToken.None));
        Assert.False(reopened.LastReadFailed);
        Assert.DoesNotContain("s3cret-value", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(FilePath)));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public async Task MissingFileIsNotConfigured()
    {
        ProviderCredentialFile store = Create();
        Assert.Equal(ProviderCredentials.None, await store.LoadAsync(CancellationToken.None));
        Assert.False(store.LastReadFailed);
    }

    [Fact]
    public async Task BlobThatCannotBeUnprotectedIsNotConfigured()
    {
        await Create().SaveAsync(Full, CancellationToken.None);

        ProviderCredentialFile store = Create(_ => throw new System.Security.Cryptography.CryptographicException());
        Assert.Equal(ProviderCredentials.None, await store.LoadAsync(CancellationToken.None));
        Assert.False(store.LastReadFailed);
    }

    [Fact]
    public async Task LockedFileIsReportedAndReadAgainOnceReleased()
    {
        await Create().SaveAsync(Full, CancellationToken.None);
        ProviderCredentialFile store = Create();

        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(ProviderCredentials.None, await store.LoadAsync(CancellationToken.None));
            Assert.True(store.LastReadFailed);
        }

        Assert.Equal(Full, await store.LoadAsync(CancellationToken.None));
        Assert.False(store.LastReadFailed);
    }

    [Fact]
    public async Task ClearRemovesTheFile()
    {
        ProviderCredentialFile store = Create();
        await store.SaveAsync(Full, CancellationToken.None);
        await store.ClearAsync(CancellationToken.None);

        Assert.False(File.Exists(FilePath));
        Assert.Equal(ProviderCredentials.None, await Create().LoadAsync(CancellationToken.None));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
