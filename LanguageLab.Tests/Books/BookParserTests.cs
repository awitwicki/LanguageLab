using System.IO.Compression;
using System.Text;
using LanguageLab.Application.Books;
using static LanguageLab.Tests.Books.BookFixtures;

namespace LanguageLab.Tests.Books;

/// <summary>Ported from web/src/books/format.test.ts and web/src/books/toParsedBook.test.ts.</summary>
public class BookParserTests
{
    private static readonly BookParser Parser = new();

    private const string DrmEncryption = """
        <encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <EncryptedData xmlns="http://www.w3.org/2001/04/xmlenc#">
            <EncryptionMethod Algorithm="http://www.w3.org/2001/04/xmlenc#aes256-cbc"/>
            <CipherData><CipherReference URI="OEBPS/text/ch01.xhtml"/></CipherData>
          </EncryptedData>
        </encryption>
        """;

    private const string FontObfuscationOnly = """
        <encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <EncryptedData xmlns="http://www.w3.org/2001/04/xmlenc#">
            <EncryptionMethod Algorithm="http://www.idpf.org/2008/embedding"/>
            <CipherData><CipherReference URI="fonts/font1.otf"/></CipherData>
          </EncryptedData>
          <EncryptedData xmlns="http://www.w3.org/2001/04/xmlenc#">
            <EncryptionMethod Algorithm="http://ns.adobe.com/pdf/enc#RC"/>
            <CipherData><CipherReference URI="fonts/font2.otf"/></CipherData>
          </EncryptedData>
        </encryption>
        """;

    private static BookFormatError ErrorOf(byte[] file) => Assert.Throws<BookFormatException>(() => Parser.Parse(file)).Error;

    [Fact]
    public void Reads_a_plain_fb2()
    {
        var book = Parser.Parse(Utf8(ReaderBookXml));

        Assert.Equal("Death's End", book.Title);
        Assert.Equal(2, book.MaxDepth);
    }

    [Fact]
    public void Reads_an_epub()
    {
        Assert.Equal("Death's End", Parser.Parse(Epub3Bytes()).Title);
    }

    [Fact]
    public void Reads_a_zipped_fb2()
    {
        var book = Parser.Parse(Zip(("deaths-end.fb2", ReaderBookXml)));

        Assert.Equal("Death's End", book.Title);
    }

    [Fact]
    public void Decodes_a_zipped_windows_1251_fb2_from_the_entry_bytes()
    {
        var head = Encoding.ASCII.GetBytes("""<?xml version="1.0" encoding="windows-1251"?><FictionBook><body><section><title><p>""");
        var tail = Encoding.ASCII.GetBytes("</p></title><p> said the guard.</p></section></body></FictionBook>");
        // "Привіт" in windows-1251.
        byte[] inner = [.. head, 0xcf, 0xf0, 0xe8, 0xe2, 0xb3, 0xf2, .. tail];

        var book = Parser.Parse(Zip(new[] { ("guard.fb2", inner, CompressionLevel.Optimal) }));

        Assert.Equal("Привіт", Assert.Single(book.Sections).Title);
    }

    [Fact]
    public void Decodes_a_plain_windows_1251_fb2()
    {
        var head = Encoding.ASCII.GetBytes("""<?xml version="1.0" encoding="windows-1251"?><FictionBook><description><title-info><book-title>""");
        var tail = Encoding.ASCII.GetBytes("</book-title></title-info></description><body><section><p>x</p></section></body></FictionBook>");
        // "Ціль" in windows-1251.
        byte[] file = [.. head, 0xd6, 0xb3, 0xeb, 0xfc, .. tail];

        Assert.Equal("Ціль", Parser.Parse(file).Title);
    }

    [Fact]
    public void Goes_by_the_bytes_not_by_a_name()
    {
        // There is no file name in the contract at all: an epub is an epub because of its bytes.
        Assert.Equal(1, Parser.Parse(Epub3Bytes()).MaxDepth);
    }

    [Fact]
    public void Names_drm_for_what_it_is()
    {
        Assert.Equal(BookFormatError.Encrypted, ErrorOf(Epub3Bytes(("META-INF/encryption.xml", DrmEncryption))));
    }

    [Fact]
    public void Refuses_a_book_whose_encryption_xml_cannot_be_told_apart_from_real_drm()
    {
        Assert.Equal(BookFormatError.Encrypted, ErrorOf(Epub3Bytes(("META-INF/encryption.xml", "<encryption/>"))));
        Assert.Equal(BookFormatError.Encrypted, ErrorOf(Epub3Bytes(("META-INF/encryption.xml", "<encryption"))));
    }

    [Fact]
    public void Opens_a_drm_free_epub_whose_encryption_xml_only_obfuscates_embedded_fonts()
    {
        Assert.Equal("Death's End", Parser.Parse(Epub3Bytes(("META-INF/encryption.xml", FontObfuscationOnly))).Title);
    }

    [Fact]
    public void Refuses_a_zip_that_claims_to_be_an_epub_but_has_no_container()
    {
        Assert.Equal(BookFormatError.Invalid, ErrorOf(Zip(("mimetype", "application/epub+zip"))));
    }

    [Fact]
    public void Refuses_a_zip_that_is_neither_and_one_with_two_fb2_files_in_it()
    {
        Assert.Equal(BookFormatError.Invalid, ErrorOf(Zip(("notes.txt", "hello"))));
        Assert.Equal(BookFormatError.Invalid, ErrorOf(Zip(("a.fb2", ReaderBookXml), ("b.fb2", ReaderBookXml))));
    }

    [Fact]
    public void Refuses_a_corrupt_archive_and_an_empty_file()
    {
        Assert.Equal(BookFormatError.Invalid, ErrorOf([0x50, 0x4b, 0x03, 0x04, 0x00, 0x01]));
        Assert.Equal(BookFormatError.Invalid, ErrorOf([]));
    }

    [Fact]
    public void Refuses_a_file_that_is_not_a_book_at_all()
    {
        Assert.Equal(BookFormatError.Invalid, ErrorOf(Utf8("<not a book")));
    }

    [Fact]
    public void Gives_an_epub_one_flat_section_per_document()
    {
        var book = Parser.Parse(Epub3Bytes());

        Assert.Equal("Death's End", book.Title);
        Assert.Equal("Cixin Liu", book.Author);
        Assert.Equal(1, book.MaxDepth);
        Assert.Equal(new[] { "The Swordholder", "Year 62", "Chapter Three" }, book.Sections.Select(section => section.Title));
        Assert.All(book.Sections, section =>
        {
            Assert.Equal(1, section.Depth);
            Assert.Empty(section.Children);
        });
    }

    [Fact]
    public void Puts_every_paragraph_of_a_document_into_its_section_text()
    {
        Assert.Equal(
            "Compared to the beginning, fewer individuals were emerging. They still formed a stratum.",
            Parser.Parse(Epub3Bytes()).Sections[0].OwnText);
    }

    [Fact]
    public void Gives_the_import_one_chapter_per_document()
    {
        var chapters = BookChapters.Flatten(Parser.Parse(Epub3Bytes()).Sections, ChapterMode.Leaf);

        Assert.Equal(new[] { "The Swordholder", "Year 62", "Chapter Three" }, chapters.Select(chapter => chapter.Title));
        Assert.Equal(new[] { 0, 1, 2 }, chapters.Select(chapter => chapter.Order));
    }

    [Fact]
    public void Leaves_the_title_empty_and_the_author_null_when_the_package_names_neither()
    {
        var opf = Opf.Replace("<dc:title>Death's End</dc:title>", "").Replace("<dc:creator>Cixin Liu</dc:creator>", "");

        var book = Parser.Parse(Epub3Bytes(("OEBPS/content.opf", opf)));

        Assert.Equal("", book.Title);
        Assert.Null(book.Author);
    }

    [Fact]
    public void Reads_an_fb2_that_starts_with_a_byte_order_mark()
    {
        byte[] file = [0xef, 0xbb, 0xbf, .. Utf8(ReaderBookXml)];

        Assert.Equal("Death's End", Parser.Parse(file).Title);
    }

    [Fact]
    public void Reads_an_epub_whose_documents_start_with_a_byte_order_mark()
    {
        var files = Epub3Files();
        files["OEBPS/content.opf"] = [0xef, 0xbb, 0xbf, .. Utf8(Opf)];
        files["OEBPS/text/ch03.xhtml"] = [0xef, 0xbb, 0xbf, .. files["OEBPS/text/ch03.xhtml"]];

        var book = Parser.Parse(Zip(files));

        Assert.Equal("Death's End", book.Title);
        Assert.Equal("Chapter Three", book.Sections[2].Title);
    }
}
