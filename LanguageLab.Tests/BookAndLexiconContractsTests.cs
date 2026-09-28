using LanguageLab.Application.Books;
using LanguageLab.Tests.Fakes;

namespace LanguageLab.Tests;

public class BookAndLexiconContractsTests
{
    [Fact]
    public void Lexicon_maps_a_known_form_to_its_lemma_and_drops_the_rest()
    {
        var lexicon = new FakeEnglishLexicon(new Dictionary<string, string>
        {
            ["went"] = "go",
            ["go"] = "go",
        });

        Assert.Equal("go", lexicon.LemmaOf("went"));
        Assert.Equal("go", lexicon.LemmaOf("go"));
        Assert.Null(lexicon.LemmaOf("aargh"));
    }

    [Fact]
    public void Lexicon_does_not_lowercase_for_the_caller()
    {
        var lexicon = new FakeEnglishLexicon(new Dictionary<string, string> { ["went"] = "go" });

        Assert.Null(lexicon.LemmaOf("Went"));
    }

    [Fact]
    public void Lexicon_lists_every_lemma_primary_first()
    {
        var lexicon = new FakeEnglishLexicon(new Dictionary<string, IReadOnlyList<string>>
        {
            ["found"] = new[] { "find", "found" },
            ["find"] = new[] { "find" },
        });

        Assert.Equal(new[] { "find", "found" }, lexicon.LemmasOf("found"));
        Assert.Equal("find", lexicon.LemmaOf("found"));
        Assert.Empty(lexicon.LemmasOf("aargh"));
        Assert.Empty(lexicon.LemmasOf("Found"));
    }

    [Fact]
    public void Lexicon_built_from_single_lemmas_lists_that_one_lemma()
    {
        var lexicon = new FakeEnglishLexicon(new Dictionary<string, string> { ["went"] = "go" });

        Assert.Equal(new[] { "go" }, lexicon.LemmasOf("went"));
    }

    [Fact]
    public void Book_format_exception_carries_its_error_kind()
    {
        var error = new BookFormatException(BookFormatError.Encrypted, "DRM-protected epub.");

        Assert.Equal(BookFormatError.Encrypted, error.Error);
        Assert.Equal("DRM-protected epub.", error.Message);
    }

    [Fact]
    public void Parsed_books_and_chapters_are_value_records()
    {
        IReadOnlyList<ParsedChapter> chapters = [new(0, "One", "It was a dark night.")];

        Assert.Equal(new ParsedBook("Title", null, chapters), new ParsedBook("Title", null, chapters));
        Assert.Equal(new ParsedChapter(0, "One", "It was a dark night."), chapters[0]);
    }
}
