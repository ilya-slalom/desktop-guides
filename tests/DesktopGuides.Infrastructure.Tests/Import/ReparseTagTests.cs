using DesktopGuides.Infrastructure.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class ReparseTagTests
{
    [Theory]
    [InlineData(0x9000001Au)] // IO_REPARSE_TAG_CLOUD
    [InlineData(0x9000101Au)] // IO_REPARSE_TAG_CLOUD_1
    [InlineData(0x9000701Au)] // IO_REPARSE_TAG_CLOUD_7, the OneDrive sync root
    [InlineData(0x9000F01Au)] // IO_REPARSE_TAG_CLOUD_F
    public void ACloudFilesPlaceholderIsAllowed(uint tag) =>
        Assert.True(ReparseTag.IsCloudPlaceholder(tag));

    [Theory]
    [InlineData(0xA000000Cu)] // IO_REPARSE_TAG_SYMLINK
    [InlineData(0xA0000003u)] // IO_REPARSE_TAG_MOUNT_POINT (junction)
    [InlineData(0x80000013u)] // IO_REPARSE_TAG_DEDUP
    [InlineData(0x8000001Bu)] // IO_REPARSE_TAG_APPEXECLINK
    [InlineData(0x9000101Bu)] // a cloud-like bit pattern with another low word
    [InlineData(0u)]
    public void AnyOtherTagIsRejected(uint tag) =>
        Assert.False(ReparseTag.IsCloudPlaceholder(tag));
}
