using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class LibraryOpenErrorsTests
{
    [Theory]
    [InlineData(26, LibraryOpenIssue.Damaged)]   // NOTADB
    [InlineData(11, LibraryOpenIssue.Damaged)]   // CORRUPT
    [InlineData(5, LibraryOpenIssue.Locked)]     // BUSY
    [InlineData(6, LibraryOpenIssue.Locked)]     // LOCKED
    [InlineData(261, LibraryOpenIssue.Locked)]   // BUSY_RECOVERY, an extended code
    [InlineData(14, LibraryOpenIssue.NoAccess)]  // CANTOPEN
    [InlineData(3, LibraryOpenIssue.NoAccess)]   // PERM
    [InlineData(8, LibraryOpenIssue.NoAccess)]   // READONLY
    [InlineData(23, LibraryOpenIssue.NoAccess)]  // AUTH
    [InlineData(13, LibraryOpenIssue.DiskFull)]  // FULL
    public void SqliteCodesMapByPrimaryCode(int code, LibraryOpenIssue issue)
    {
        SqliteException cause = new("injected", code);

        LibraryOpenException? mapped = LibraryOpenErrors.Map(cause);

        Assert.NotNull(mapped);
        Assert.Equal(issue, mapped.Issue);
        Assert.Same(cause, mapped.InnerException);
        Assert.Null(mapped.RecoveryCopyPath);
    }

    [Fact]
    public void AnUnlistedSqliteCodeIsNotMapped() =>
        Assert.Null(LibraryOpenErrors.Map(new SqliteException("injected", 1)));

    [Fact]
    public void InvalidDataIsDamaged() =>
        Assert.Equal(LibraryOpenIssue.Damaged,
            LibraryOpenErrors.Map(new InvalidDataException("schema is incomplete"))?.Issue);

    [Fact]
    public void UnauthorizedAccessIsNoAccess() =>
        Assert.Equal(LibraryOpenIssue.NoAccess,
            LibraryOpenErrors.Map(new UnauthorizedAccessException())?.Issue);

    [Theory]
    [InlineData(unchecked((int)0x80070070))] // ERROR_DISK_FULL
    [InlineData(unchecked((int)0x80070027))] // ERROR_HANDLE_DISK_FULL
    public void DiskFullIOExceptionsAreDiskFull(int hresult) =>
        Assert.Equal(LibraryOpenIssue.DiskFull,
            LibraryOpenErrors.Map(new IOException("full", hresult))?.Issue);

    [Fact]
    public void OtherIOExceptionsAreNotMapped() =>
        Assert.Null(LibraryOpenErrors.Map(new IOException("sharing", unchecked((int)0x80070020))));

    [Fact]
    public void UnrelatedExceptionsAreNotMapped() =>
        Assert.Null(LibraryOpenErrors.Map(new InvalidOperationException()));

    [Fact]
    public void AnAlreadyMappedErrorIsNotWrappedAgain() =>
        Assert.Null(LibraryOpenErrors.Map(new LibraryOpenException(LibraryOpenIssue.Missing)));
}
