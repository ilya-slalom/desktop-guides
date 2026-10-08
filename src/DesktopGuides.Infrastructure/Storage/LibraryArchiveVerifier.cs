using System.IO.Compression;
using System.Security.Cryptography;
using DesktopGuides.Core.Backup;

namespace DesktopGuides.Infrastructure.Storage;

/// <summary>
/// Checks a library archive exactly against its manifest: the manifest
/// first, then every listed entry in order, each with its length and SHA-256.
/// Export runs it before the rename; restore runs it before staging.
/// </summary>
internal static class LibraryArchiveVerifier
{
    private const int BufferBytes = 81920;

    public static LibraryArchiveManifest Verify(Stream zip, CancellationToken token)
    {
        using ZipArchive archive = new(zip, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count == 0 ||
            archive.Entries[0].FullName != LibraryArchiveManifest.EntryName)
        {
            throw new InvalidDataException("The archive's first entry isn't its manifest.");
        }
        LibraryArchiveManifest manifest = LibraryArchiveManifest.Parse(ReadManifest(archive.Entries[0]));
        if (archive.Entries.Count != manifest.Entries.Count + 1)
        {
            throw new InvalidDataException("The archive's entries don't match its manifest.");
        }
        byte[] buffer = new byte[BufferBytes];
        for (int index = 0; index < manifest.Entries.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            LibraryArchiveEntry expected = manifest.Entries[index];
            ZipArchiveEntry actual = archive.Entries[index + 1];
            if (actual.FullName != expected.Path || actual.Length != expected.Bytes)
            {
                throw new InvalidDataException($"The archive entry {actual.FullName} doesn't match its manifest.");
            }
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long read = 0;
            using (Stream stream = actual.Open())
            {
                int count;
                while ((count = stream.Read(buffer)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    read += count;
                    hash.AppendData(buffer, 0, count);
                }
            }
            if (read != expected.Bytes ||
                Convert.ToHexStringLower(hash.GetHashAndReset()) != expected.Sha256)
            {
                throw new InvalidDataException($"The archive entry {expected.Path} doesn't match its hash.");
            }
        }
        return manifest;
    }

    private static byte[] ReadManifest(ZipArchiveEntry entry)
    {
        if (entry.Length > LibraryArchiveManifest.MaxManifestBytes)
        {
            throw new InvalidDataException("The manifest is too large.");
        }
        using Stream stream = entry.Open();
        using MemoryStream copy = new();
        byte[] buffer = new byte[BufferBytes];
        int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            copy.Write(buffer, 0, count);
            if (copy.Length > LibraryArchiveManifest.MaxManifestBytes)
            {
                throw new InvalidDataException("The manifest is too large.");
            }
        }
        return copy.ToArray();
    }
}
