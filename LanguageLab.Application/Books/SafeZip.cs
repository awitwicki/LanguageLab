using System.IO.Compression;
using System.Text.RegularExpressions;

namespace LanguageLab.Application.Books;

/// <summary>
/// A port of web/src/books/zip.ts — change both together — with the limits a server needs: the
/// archive comes from any signed-in user. Sizes are counted from the bytes actually inflated,
/// never from an entry's declared size, which the archive's author controls. Nothing touches disk.
/// </summary>
internal static class SafeZip
{
    public const int MaxEntries = 5_000;
    public const long MaxEntryBytes = 32L * 1024 * 1024;
    public const long MaxTotalBytes = 64L * 1024 * 1024;

    /// <summary>What a zip archive made on a Mac carries besides the book.</summary>
    private static readonly Regex Junk = new(@"^__MACOSX/|(?:^|/)\.DS_Store$", RegexOptions.CultureInvariant);

    /// <summary>The local-file-header signature every zip starts with — an epub and a zipped fb2 both do.</summary>
    public static bool LooksZipped(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4b && bytes[2] == 0x03 && bytes[3] == 0x04;

    /// <summary>Every file of the archive by its full path; directories and Mac junk left out.</summary>
    public static IReadOnlyDictionary<string, byte[]> Read(byte[] bytes)
    {
        try
        {
            return ReadEntries(bytes);
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException or IOException or ArgumentException)
        {
            throw new BookFormatException(BookFormatError.Invalid, "not a readable zip archive", e);
        }
    }

    private static Dictionary<string, byte[]> ReadEntries(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);

        if (archive.Entries.Count > MaxEntries)
        {
            throw new BookFormatException(BookFormatError.Invalid, "zip entry limit exceeded");
        }

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var chunk = new byte[81_920];
        long total = 0;

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || Junk.IsMatch(entry.FullName))
            {
                continue;
            }

            using var input = entry.Open();
            using var content = new MemoryStream();
            int read;

            while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (content.Length + read > MaxEntryBytes)
                {
                    throw new BookFormatException(BookFormatError.Invalid, "zip entry size limit exceeded");
                }

                total += read;

                if (total > MaxTotalBytes)
                {
                    throw new BookFormatException(BookFormatError.Invalid, "zip total size limit exceeded");
                }

                content.Write(chunk, 0, read);
            }

            entries[entry.FullName] = content.ToArray();
        }

        return entries;
    }
}
