using System.IO.Compression;
using System.Text;
using LanguageLab.Application.Books;
using static LanguageLab.Tests.Books.BookFixtures;

namespace LanguageLab.Tests.Books;

/// <summary>Ported from web/src/books/zip.test.ts, plus the limits the server adds.</summary>
public class SafeZipTests
{
    private const int MiB = 1024 * 1024;

    [Fact]
    public void Recognises_a_zip_by_its_signature_and_nothing_else()
    {
        Assert.True(SafeZip.LooksZipped(Zip(("a.txt", "a"))));
        Assert.False(SafeZip.LooksZipped(Utf8("<FictionBook/>")));
        Assert.False(SafeZip.LooksZipped([]));
    }

    [Fact]
    public void Reads_stored_and_deflated_entries_alike()
    {
        // An epub has both: its "mimetype" entry must be stored, the rest is usually deflated.
        var bytes = Zip(new[]
        {
            ("mimetype", Utf8("application/epub+zip"), CompressionLevel.NoCompression),
            ("ch01.xhtml", Utf8("<p>Hello there.</p>"), CompressionLevel.SmallestSize),
        });

        var entries = SafeZip.Read(bytes);

        Assert.Equal("application/epub+zip", Encoding.UTF8.GetString(entries["mimetype"]));
        Assert.Equal("<p>Hello there.</p>", Encoding.UTF8.GetString(entries["ch01.xhtml"]));
    }

    [Fact]
    public void Drops_directory_entries_and_the_junk_a_mac_adds()
    {
        var entries = SafeZip.Read(Zip(("book/", ""), ("book/ch01.xhtml", "x"), ("__MACOSX/._ch01.xhtml", "junk"), (".DS_Store", "junk")));

        Assert.Equal(new[] { "book/ch01.xhtml" }, entries.Keys);
    }

    [Fact]
    public void Throws_invalid_on_a_corrupt_archive()
    {
        var error = Assert.Throws<BookFormatException>(() => SafeZip.Read([0x50, 0x4b, 0x03, 0x04, 0x00, 0x01, 0x02]));

        Assert.Equal(BookFormatError.Invalid, error.Error);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void Reads_a_book_sized_archive_whole()
    {
        var paragraph = string.Concat(Enumerable.Repeat("The silo was quiet and the stairs were long. ", 600));
        var files = Enumerable.Range(0, 300).Select(i => ($"text/ch{i}.xhtml", $"<p>{i} {paragraph}</p>")).ToArray();

        var entries = SafeZip.Read(Zip(files));

        Assert.Equal(300, entries.Count);
        Assert.Contains("299 The silo was quiet", Encoding.UTF8.GetString(entries["text/ch299.xhtml"]));
    }

    [Fact]
    public void Refuses_an_entry_that_inflates_past_32_mb()
    {
        var bomb = Zip(new[] { ("book.fb2", new byte[40 * MiB], CompressionLevel.SmallestSize) });

        var error = Assert.Throws<BookFormatException>(() => SafeZip.Read(bomb));

        Assert.Equal(BookFormatError.Invalid, error.Error);
        Assert.Equal("zip entry size limit exceeded", error.Message);
    }

    [Fact]
    public void Refuses_an_archive_that_inflates_past_64_mb_in_total()
    {
        var files = Enumerable.Range(0, 3).Select(i => ($"part{i}.xhtml", new byte[30 * MiB], CompressionLevel.SmallestSize));

        var error = Assert.Throws<BookFormatException>(() => SafeZip.Read(Zip(files)));

        Assert.Equal("zip total size limit exceeded", error.Message);
    }

    [Fact]
    public void Refuses_an_archive_with_more_than_5000_entries()
    {
        var files = Enumerable.Range(0, SafeZip.MaxEntries + 1).Select(i => ($"e{i}", "x")).ToArray();

        var error = Assert.Throws<BookFormatException>(() => SafeZip.Read(Zip(files)));

        Assert.Equal("zip entry limit exceeded", error.Message);
    }

    [Fact]
    public void An_entry_whose_header_understates_its_size_is_never_inflated_past_that_size()
    {
        var bytes = Zip(new[] { ("book.fb2", new byte[40 * MiB], CompressionLevel.SmallestSize) });
        UnderstateUncompressedSize(bytes, 1_000);

        // .NET stops inflating at the declared size, so the lie cannot smuggle 40 MB past the counter.
        var entries = SafeZip.Read(bytes);

        Assert.True(entries["book.fb2"].Length <= 1_000);
    }

    /// <summary>Rewrites the uncompressed-size field of every local header and central-directory record.</summary>
    private static void UnderstateUncompressedSize(byte[] zip, uint size)
    {
        for (var i = 0; i + 4 <= zip.Length; i++)
        {
            if (zip[i] == 0x50 && zip[i + 1] == 0x4b && zip[i + 2] == 0x03 && zip[i + 3] == 0x04)
            {
                BitConverter.GetBytes(size).CopyTo(zip, i + 22);
            }
            else if (zip[i] == 0x50 && zip[i + 1] == 0x4b && zip[i + 2] == 0x01 && zip[i + 3] == 0x02)
            {
                BitConverter.GetBytes(size).CopyTo(zip, i + 24);
            }
        }
    }
}
