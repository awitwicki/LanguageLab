using System.Text;
using System.Text.RegularExpressions;
using LanguageLab.Application.Lexicon;
using LanguageLab.Domain.Lexicon;
using LanguageLab.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace LanguageLab.Tests;

/// <summary>Against the real, embedded english-lexicon.txt.</summary>
public class EnglishLexiconTests
{
    private static readonly EnglishLexicon Lexicon = new();
    private static readonly Regex Line = new("^[a-z]+( [a-z]+)*$", RegexOptions.CultureInvariant);

    [Fact]
    public void An_irregular_past_maps_to_its_verb_only()
    {
        Assert.Equal(new[] { "go" }, Lexicon.LemmasOf("went"));
        Assert.Equal("go", Lexicon.LemmaOf("went"));
    }

    [Fact]
    public void A_form_that_is_also_a_lemma_comes_after_the_lemma_it_inflects()
    {
        Assert.Equal(new[] { "find", "found" }, Lexicon.LemmasOf("found"));
        Assert.Equal("find", Lexicon.LemmaOf("found"));
    }

    [Fact]
    public void An_override_wins_over_the_computed_order()
    {
        Assert.Equal(new[] { "lay", "lie" }, Lexicon.LemmasOf("lay"));
    }

    [Fact]
    public void An_override_fixes_a_common_word_wrongly_ordered_behind_a_rare_source_word()
    {
        // AGID's rule-generated comparative/superlative forms of obscure adjectives coincide with
        // far more common, unrelated words ("numb" -> "number", "inter" -> "interest"): the
        // inflection-wins rule alone would leave "number"/"interest" behind "numb"/"inter".
        Assert.Equal("number", Lexicon.LemmaOf("number"));
        Assert.Equal("interest", Lexicon.LemmaOf("interest"));
        Assert.Equal("morning", Lexicon.LemmaOf("morning"));
        Assert.Equal("customer", Lexicon.LemmaOf("customer"));
        Assert.Equal("priest", Lexicon.LemmaOf("priest"));
    }

    [Fact]
    public void A_lemma_maps_to_itself()
    {
        Assert.Equal(new[] { "go" }, Lexicon.LemmasOf("go"));
    }

    [Fact]
    public void A_regular_plural_does_not_list_itself_as_a_lemma()
    {
        Assert.Equal(new[] { "house" }, Lexicon.LemmasOf("houses"));
    }

    [Fact]
    public void A_word_that_is_not_english_has_no_lemmas()
    {
        Assert.Empty(Lexicon.LemmasOf("aargh"));
        Assert.Null(Lexicon.LemmaOf("aargh"));
    }

    [Fact]
    public void The_lexicon_does_not_lowercase_for_the_caller()
    {
        Assert.Empty(Lexicon.LemmasOf("Went"));
        Assert.Null(Lexicon.LemmaOf("Went"));
    }

    [Fact]
    public void First_use_from_many_threads_at_once_sees_one_complete_table()
    {
        var fresh = new EnglishLexicon();
        var primaries = new string?[64];

        Parallel.For(0, primaries.Length, i => primaries[i] = fresh.LemmaOf("went"));

        Assert.All(primaries, primary => Assert.Equal("go", primary));
    }

    [Fact]
    public void Every_line_is_well_formed_sorted_unique_and_closed()
    {
        var text = ReadEmbedded();
        Assert.EndsWith("\n", text);
        Assert.DoesNotContain("\n\n", text);
        Assert.DoesNotContain("\r", text);
        var lines = text[..^1].Split('\n');
        Assert.True(lines.Length > 50_000, $"Only {lines.Length} lines — is the build truncated?");

        var forms = new HashSet<string>(StringComparer.Ordinal);
        string? previous = null;
        foreach (var line in lines)
        {
            Assert.True(Line.IsMatch(line), $"Malformed line: '{line}'");
            var form = line.Split(' ')[0];
            Assert.True(previous is null || string.CompareOrdinal(previous, form) < 0,
                $"'{form}' is out of order or repeated after '{previous}'");
            forms.Add(form);
            previous = form;
        }

        foreach (var line in lines)
        {
            var words = line.Split(' ');
            if (words.Length == 1)
                continue;

            var lemmas = words[1..];
            Assert.True(lemmas.Distinct(StringComparer.Ordinal).Count() == lemmas.Length, $"Repeated lemma: '{line}'");
            Assert.False(lemmas.Length == 1 && lemmas[0] == words[0],
                $"A lemma of itself only is written as one word: '{line}'");
            foreach (var lemma in lemmas)
                Assert.True(forms.Contains(lemma), $"'{lemma}' in '{line}' has no line of its own");
        }
    }

    [Theory]
    [InlineData("english-lexicon.txt")]
    [InlineData("LICENSE-SCOWL.txt")]
    [InlineData("LICENSE-AGID.txt")]
    public void The_server_and_spa_copies_are_identical(string name)
    {
        var root = RepoRoot();
        var server = File.ReadAllBytes(Path.Combine(root, "LanguageLab.Domain", "Lexicon", name));
        var spa = File.ReadAllBytes(Path.Combine(root, "web", "public", "lexicon", name));

        Assert.True(server.AsSpan().SequenceEqual(spa),
            $"{name} differs between LanguageLab.Domain/Lexicon and web/public/lexicon — rerun scripts/build_lexicon.py.");
    }

    private static string ReadEmbedded()
    {
        using var stream = typeof(EnglishLexicon).Assembly.GetManifestResourceStream(EnglishLexicon.ResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LanguageLab.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException($"No LanguageLab.sln above {AppContext.BaseDirectory}.");
    }
}

public class LexiconRegistrationTests
{
    [Fact]
    public void AddEnglishLexicon_registers_one_shared_lexicon()
    {
        var services = new ServiceCollection().AddEnglishLexicon().AddEnglishLexicon();
        using var provider = services.BuildServiceProvider();

        var lexicon = provider.GetRequiredService<IEnglishLexicon>();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IEnglishLexicon));
        Assert.IsType<EnglishLexicon>(lexicon);
        Assert.Same(lexicon, provider.GetRequiredService<IEnglishLexicon>());
    }

    [Fact]
    public void AddEnglishLexicon_keeps_a_lexicon_registered_before_it()
    {
        var fake = new FakeEnglishLexicon(new Dictionary<string, string>());
        using var provider = new ServiceCollection()
            .AddSingleton<IEnglishLexicon>(fake)
            .AddEnglishLexicon()
            .BuildServiceProvider();

        Assert.Same(fake, provider.GetRequiredService<IEnglishLexicon>());
    }
}
