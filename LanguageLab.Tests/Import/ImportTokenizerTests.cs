using LanguageLab.Application.Books;
using LanguageLab.Application.Import;
using LanguageLab.Application.Services;
using LanguageLab.Tests.Fakes;

namespace LanguageLab.Tests.Import;

/// <summary>Ported from web/src/fb2/tokenize.test.ts — the cases the server still owns.</summary>
public class ImportTokenizerTests
{
    [Theory]
    [InlineData("Well-known!", "well-known")]      // lowercases, strips punctuation, keeps hyphens
    [InlineData("_'silo'_", "silo")]               // strips quotes and underscores from the edges
    [InlineData("don't", "don't")]                 // keeps a straight apostrophe inside
    [InlineData("don’t", "don't")]                 // normalizes a typographic apostrophe
    public void Clean_word_matches_the_client(string raw, string cleaned)
    {
        Assert.Equal(cleaned, ImportTokenizer.CleanWord(raw));
    }

    [Fact]
    public void Splits_on_hyphens_and_cleans_each_part()
    {
        Assert.Equal(new[] { "well", "known" }, ImportTokenizer.SplitCompoundWord("well-known"));
    }

    [Fact]
    public void Drops_compound_parts_that_are_digits_or_too_short()
    {
        // "x" is below MinLength, "15" empties after the digit strip, "ray" stays.
        Assert.Equal(new[] { "ray" }, ImportTokenizer.SplitCompoundWord("x-15-ray"));
    }

    [Fact]
    public void Keeps_a_two_letter_compound_part()
    {
        // The client demanded > 2; the server admits MinLength (2) so co-op yields both parts.
        Assert.Equal(new[] { "co", "op" }, ImportTokenizer.SplitCompoundWord("co-op"));
    }

    [Theory]
    [InlineData("one", true)]
    [InlineData("fourth", true)]
    [InlineData("ninth", true)]        // the irregular ordinal
    [InlineData("tent", false)]        // "ten" + "t" is not an ordinal suffix
    [InlineData("stone", false)]
    [InlineData("eleventh", false)]    // outside one..ten, exactly as on the client
    public void Number_words_are_recognized(string word, bool isNumber)
    {
        Assert.Equal(isNumber, ImportTokenizer.IsNumberWord(word));
    }

    [Fact]
    public void The_stop_word_set_is_the_nltk_list()
    {
        Assert.Contains("the", EnglishStopWords.All);
        Assert.Contains("don't", EnglishStopWords.All);
        Assert.DoesNotContain("silo", EnglishStopWords.All);
    }

    private static FakeEnglishLexicon Lexicon(params string[] lemmas) =>
        new(lemmas.ToDictionary(lemma => lemma, lemma => lemma, StringComparer.Ordinal));

    private static TokenizedBook Tokenize(string text, FakeEnglishLexicon lexicon) =>
        ImportTokenizer.Tokenize(new[] { new ParsedChapter(0, "One", text) }, lexicon);

    [Fact]
    public void Counts_lemma_occurrences_per_chapter_in_order()
    {
        var chapters = new[]
        {
            new ParsedChapter(0, "One", "silo silo abide"),
            new ParsedChapter(1, "Two", "abide"),
        };

        var book = ImportTokenizer.Tokenize(chapters, Lexicon("silo", "abide"));

        Assert.Equal(new[] { 0, 1 }, book.Chapters.Select(c => c.Order));
        Assert.Equal(new[] { new ImportWord("silo", 2), new ImportWord("abide", 1) }, book.Chapters[0].Words);
        Assert.Equal(new[] { new ImportWord("abide", 1) }, book.Chapters[1].Words);
    }

    [Fact]
    public void Maps_a_form_to_its_primary_lemma()
    {
        var lexicon = new FakeEnglishLexicon(new Dictionary<string, string> { ["went"] = "go", ["go"] = "go" });

        var book = Tokenize("went go went", lexicon);

        Assert.Equal(new[] { new ImportWord("go", 3) }, book.Chapters[0].Words);
    }

    [Fact]
    public void Words_the_lexicon_does_not_know_are_dropped_and_counted_unknown()
    {
        var book = Tokenize("silo hogwarts", Lexicon("silo"));

        Assert.Equal(new[] { new ImportWord("silo", 1) }, book.Chapters[0].Words);
        Assert.Equal(1, book.KnownOccurrences);
        Assert.Equal(1, book.UnknownOccurrences);
    }

    [Fact]
    public void Stop_words_and_number_words_count_as_known_but_never_enter_the_dictionary()
    {
        var book = Tokenize("the silo was fourth", Lexicon("silo"));

        Assert.Equal(new[] { new ImportWord("silo", 1) }, book.Chapters[0].Words);
        Assert.Equal(4, book.KnownOccurrences);
        Assert.Equal(0, book.UnknownOccurrences);
    }

    [Fact]
    public void An_inflection_whose_lexicon_lemma_is_a_stop_word_or_number_word_stays_out_too()
    {
        // "done"/"others"/"ones" are not themselves stop or number words, so the client's own
        // isRejected (run on the already-lemmatized form) is the only thing that catches them —
        // the server must re-check after LemmaOf, not just before it.
        var lexicon = new FakeEnglishLexicon(new Dictionary<string, string>
        {
            ["done"] = "do",
            ["others"] = "other",
            ["ones"] = "one",
            ["silo"] = "silo",
        });

        var book = Tokenize("done others ones silo", lexicon);

        Assert.Equal(new[] { new ImportWord("silo", 1) }, book.Chapters[0].Words);
        Assert.Equal(4, book.KnownOccurrences);
        Assert.Equal(0, book.UnknownOccurrences);
    }

    [Fact]
    public void An_em_or_en_dash_with_no_space_still_separates_two_words()
    {
        // The client tokenized via compromise, which splits on these; plain whitespace-splitting
        // does not, and CleanWord discards the dash character itself, gluing "know" and "maybe"
        // into the single unknown token "knowmaybe".
        var book = Tokenize("know—maybe waited–soon", Lexicon("know", "maybe", "waited", "soon"));

        Assert.Equal(
            new[] { new ImportWord("know", 1), new ImportWord("maybe", 1), new ImportWord("waited", 1), new ImportWord("soon", 1) },
            book.Chapters[0].Words);
        Assert.Equal(0, book.UnknownOccurrences);
    }

    [Fact]
    public void Contractions_digits_and_single_letters_stay_out_of_the_metric()
    {
        // don't: apostrophe; x9: digit; j: below MinLength — all neutral, neither known nor unknown.
        var book = Tokenize("don't x9 j silo", Lexicon("silo"));

        Assert.Equal(1, book.KnownOccurrences);
        Assert.Equal(0, book.UnknownOccurrences);
    }

    [Fact]
    public void Splits_compounds_and_keeps_two_letter_lemmas()
    {
        var book = Tokenize("well-known co-op", Lexicon("well", "known", "co", "op"));

        Assert.Equal(
            new[] { new ImportWord("well", 1), new ImportWord("known", 1), new ImportWord("co", 1), new ImportWord("op", 1) },
            book.Chapters[0].Words);
    }

    [Fact]
    public void A_chapter_left_with_no_words_is_dropped()
    {
        var chapters = new[]
        {
            new ParsedChapter(0, "Pictures", "the of and"),
            new ParsedChapter(1, "Text", "silo"),
        };

        var book = ImportTokenizer.Tokenize(chapters, Lexicon("silo"));

        var chapter = Assert.Single(book.Chapters);
        Assert.Equal(1, chapter.Order);
    }

    // Review Focus 1: names are a large share of a novel's distinct words but a small share of
    // its occurrences — the metric must weigh occurrences.
    [Fact]
    public void Coverage_passes_an_english_text_dense_with_names()
    {
        var book = Tokenize("the silo was quiet and holston climbed to the top of hogwarts", Lexicon("silo", "quiet", "climbed", "top"));

        Assert.True(book.LooksEnglish);
    }

    // Review Focus 2: Latin-script non-English — the old charset check would wave it through.
    [Fact]
    public void Coverage_refuses_a_latin_script_non_english_text()
    {
        var book = Tokenize("les enfants jouaient dans le jardin toute la journee", Lexicon("silo"));

        Assert.False(book.LooksEnglish);
    }

    // Review Focus 3: a Cyrillic text yields no countable tokens at all.
    [Fact]
    public void A_text_with_no_ascii_words_at_all_is_not_english()
    {
        var book = Tokenize("Привіт світе як справи", Lexicon("silo"));

        Assert.Equal(0, book.KnownOccurrences + book.UnknownOccurrences);
        Assert.False(book.LooksEnglish);
    }

    [Fact]
    public void Half_known_is_the_lowest_passing_share()
    {
        var book = Tokenize("silo hogwarts", Lexicon("silo"));

        Assert.True(book.LooksEnglish);
    }
}
