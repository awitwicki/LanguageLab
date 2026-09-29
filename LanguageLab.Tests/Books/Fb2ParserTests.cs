using LanguageLab.Application.Books;

namespace LanguageLab.Tests.Books;

/// <summary>Ported from web/src/fb2/chapters.test.ts (parseBook and flattenChapters over an fb2).</summary>
public class Fb2ParserTests
{
    private const string Nested = """
        <?xml version="1.0" encoding="utf-8"?>
        <FictionBook>
          <description><title-info><book-title>Wool</book-title></title-info></description>
          <body>
            <section>
              <title><p>Part One</p></title>
              <section><title><p>Chapter 1</p></title><p>the children were playing</p></section>
              <section><title><p>Chapter 2</p></title><p>holston climbed</p></section>
            </section>
          </body>
          <body name="notes">
            <section><title><p>Notes</p></title><p>footnote text</p></section>
          </body>
          <binary id="cover" content-type="image/jpeg">AAAABBBBCCCC</binary>
        </FictionBook>
        """;

    private const string WithPrologue = """
        <?xml version="1.0" encoding="utf-8"?>
        <FictionBook>
          <description><title-info><book-title>Wool</book-title></title-info></description>
          <body>
            <section><title><p>Prologue</p></title><p>before the beginning</p></section>
            <section>
              <title><p>Part One</p></title>
              <section><title><p>Chapter 1</p></title><p>the children were playing</p></section>
              <section><title><p>Chapter 2</p></title><p>holston climbed</p></section>
            </section>
          </body>
        </FictionBook>
        """;

    private static IEnumerable<BookSection> AllSections(IEnumerable<BookSection> sections) =>
        sections.SelectMany(section => AllSections(section.Children).Prepend(section));

    [Fact]
    public void Takes_the_dictionary_name_from_book_title()
    {
        Assert.Equal("Wool", Fb2Parser.Parse(Nested).Title);
    }

    [Fact]
    public void Reports_the_deepest_section_level()
    {
        Assert.Equal(2, Fb2Parser.Parse(Nested).MaxDepth);
    }

    [Fact]
    public void Ignores_the_notes_body()
    {
        var sections = AllSections(Fb2Parser.Parse(Nested).Sections).ToList();

        Assert.DoesNotContain(sections, section => section.OwnText.Contains("footnote text") || section.Title == "Notes");
    }

    [Fact]
    public void Ignores_binary_payloads()
    {
        var sections = AllSections(Fb2Parser.Parse(Nested).Sections);

        Assert.DoesNotContain(sections, section => section.OwnText.Contains("AAAABBBBCCCC"));
    }

    [Fact]
    public void Treats_leaf_sections_as_chapters_by_default()
    {
        var chapters = BookChapters.Flatten(Fb2Parser.Parse(Nested).Sections, ChapterMode.Leaf);

        Assert.Equal(new[] { "Chapter 1", "Chapter 2" }, chapters.Select(chapter => chapter.Title));
        Assert.Contains("the children were playing", chapters[0].Text);
    }

    [Fact]
    public void Collapses_deeper_sections_when_a_depth_is_given()
    {
        var chapters = BookChapters.Flatten(Fb2Parser.Parse(Nested).Sections, new ChapterMode(1));

        Assert.Equal(new[] { "Part One" }, chapters.Select(chapter => chapter.Title));
        Assert.Contains("the children were playing", chapters[0].Text);
        Assert.Contains("holston climbed", chapters[0].Text);
    }

    [Fact]
    public void Keeps_untitled_sections_with_an_empty_title()
    {
        var book = Fb2Parser.Parse("<FictionBook><body><section><p>no title here</p></section></body></FictionBook>");

        var chapter = Assert.Single(BookChapters.Flatten(book.Sections, ChapterMode.Leaf));
        Assert.Equal("", chapter.Title);
    }

    [Fact]
    public void Keeps_a_childless_section_above_the_requested_depth_instead_of_dropping_it()
    {
        var chapters = BookChapters.Flatten(Fb2Parser.Parse(WithPrologue).Sections, new ChapterMode(2));

        Assert.Equal(new[] { "Prologue", "Chapter 1", "Chapter 2" }, chapters.Select(chapter => chapter.Title));
        Assert.Contains("before the beginning", chapters[0].Text);
    }

    [Fact]
    public void Reads_a_namespaced_book_with_its_author_and_poem()
    {
        var book = Fb2Parser.Parse(BookFixtures.ReaderBookXml);

        Assert.Equal("Death's End", book.Title);
        Assert.Equal("Cixin Liu", book.Author);
        Assert.Equal(2, book.MaxDepth);

        var chapters = BookChapters.Flatten(book.Sections, ChapterMode.Leaf);
        Assert.Equal(new[] { "The Swordholder", "Year 62" }, chapters.Select(chapter => chapter.Title));
        // textContent glues <v> lines that sit side by side in the source — the SPA does the same.
        Assert.Contains("The silo was quiet,the silo was cold.", chapters[1].Text);
        Assert.Contains("Most men tried to adjust.", chapters[1].Text);
    }

    [Fact]
    public void Keeps_a_section_title_on_one_line()
    {
        var book = Fb2Parser.Parse("<FictionBook><body><section><title><p>Part</p>\n  <p>One</p></title><p>x</p></section></body></FictionBook>");

        Assert.Equal("Part One", Assert.Single(book.Sections).Title);
    }

    [Fact]
    public void Leaves_the_author_null_when_the_book_names_none()
    {
        Assert.Null(Fb2Parser.Parse(Nested).Author);
    }

    [Fact]
    public void Reports_depth_zero_and_no_sections_for_a_book_with_none()
    {
        var book = Fb2Parser.Parse("<FictionBook><body><p>loose</p></body></FictionBook>");

        Assert.Empty(book.Sections);
        Assert.Equal(0, book.MaxDepth);
        Assert.Equal("", book.Title);
    }

    [Fact]
    public void Refuses_text_that_is_not_xml()
    {
        var error = Assert.Throws<BookFormatException>(() => Fb2Parser.Parse("<not a book"));

        Assert.Equal(BookFormatError.Invalid, error.Error);
        Assert.IsType<System.Xml.XmlException>(error.InnerException);
    }

    [Fact]
    public void Refuses_sections_nested_past_the_limit()
    {
        var deep = string.Concat(Enumerable.Repeat("<section>", MarkupDepth.Limit + 1)) + string.Concat(Enumerable.Repeat("</section>", MarkupDepth.Limit + 1));

        var error = Assert.Throws<BookFormatException>(() => Fb2Parser.Parse($"<FictionBook><body>{deep}</body></FictionBook>"));

        Assert.Equal(BookFormatError.Invalid, error.Error);
    }
}
