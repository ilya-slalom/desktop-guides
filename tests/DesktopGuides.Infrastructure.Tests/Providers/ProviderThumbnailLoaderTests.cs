using System.Net;
using DesktopGuides.Infrastructure.Providers;
using DesktopGuides.Infrastructure.Tests.Artwork;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Providers;

public sealed class ProviderThumbnailLoaderTests
{
    private const string Cover = "https://images.igdb.com/igdb/image/upload/t_cover_small/co1.jpg";

    private static HttpResponseMessage Image(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    [Fact]
    public async Task ReturnsValidatedImageBytes()
    {
        byte[] png = TestImages.Png(90, 120);
        using ProviderHttp http = new(FakeHandler.Returning(Image(png)));
        Assert.Equal(png, await new ProviderThumbnailLoader(http).LoadAsync(Cover, default));
    }

    [Fact]
    public async Task NonImageBytesReturnNull()
    {
        using ProviderHttp http = new(FakeHandler.Returning(Image("<html>not an image</html>"u8.ToArray())));
        Assert.Null(await new ProviderThumbnailLoader(http).LoadAsync(Cover, default));
    }

    [Theory]
    [InlineData("https://evil.example/co1.jpg")]
    [InlineData("http://images.igdb.com/igdb/image/upload/t_cover_small/co1.jpg")]
    [InlineData("not a url")]
    public async Task DisallowedAddressReturnsNullWithoutSending(string url)
    {
        FakeHandler handler = FakeHandler.Returning(Image(TestImages.Png(1, 1)));
        using ProviderHttp http = new(handler);
        Assert.Null(await new ProviderThumbnailLoader(http).LoadAsync(url, default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using ProviderHttp http = new(new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage();
        }));
        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ProviderThumbnailLoader(http).LoadAsync(Cover, cancel.Token));
    }
}
