using LanguageLab.Domain.Pronunciation;

namespace LanguageLab.Tests;

public class IpaCatalogTests
{
    private static readonly string AudioDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "web", "public", "pronunciation-audio");

    [Fact]
    public void Every_section_has_symbols()
    {
        foreach (var section in IpaCatalog.Sections)
        {
            Assert.NotEmpty(section.Entries);
        }
    }

    [Fact]
    public void No_symbol_is_listed_twice()
    {
        var duplicates = IpaCatalog.All
            .GroupBy(e => e.Symbol)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_symbol_carries_a_name_a_hint_and_a_group()
    {
        foreach (var entry in IpaCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Name), entry.Symbol);
            Assert.False(string.IsNullOrWhiteSpace(entry.Hint), entry.Symbol);
            Assert.False(string.IsNullOrWhiteSpace(entry.Group), entry.Symbol);
        }
    }

    [Fact]
    public void An_example_word_never_arrives_without_its_language_and_transcription()
    {
        foreach (var entry in IpaCatalog.All)
        {
            var hasWord = entry.ExampleWord is not null;
            Assert.Equal(hasWord, entry.ExampleLanguage is not null);
            Assert.Equal(hasWord, entry.ExampleIpa is not null);
        }
    }

    [Fact]
    public void Every_named_recording_is_a_file_on_disk()
    {
        foreach (var entry in IpaCatalog.All)
        {
            foreach (var file in new[] { entry.SoundAudioFile, entry.WordAudioFile })
            {
                if (file is null) continue;
                Assert.True(File.Exists(Path.Combine(AudioDir, file)), $"missing {file} for {entry.Symbol}");
            }
        }
    }

    [Fact]
    public void Only_the_ejective_mark_has_nothing_at_all_to_play()
    {
        var silent = IpaCatalog.All
            .Where(e => e.SoundAudioFile is null && e.WordAudioFile is null)
            .Select(e => e.Symbol)
            .ToList();

        // ʼ is written after another consonant rather than said on its own, so there is no
        // recording of it to find. Every other symbol of the chart can be heard one way or
        // the other — as the sound itself, or through its example word.
        Assert.Equal(["ʼ"], silent);
    }

    [Fact]
    public void Every_sound_english_uses_can_be_heard()
    {
        foreach (var entry in IpaCatalog.All.Where(e => e.InEnglish))
        {
            Assert.True(
                entry.SoundAudioFile is not null || entry.WordAudioFile is not null,
                $"{entry.Symbol} is an English sound with no recording of any kind");
        }
    }

    [Fact]
    public void Find_reads_a_symbol_and_its_aliases()
    {
        Assert.Equal("i", IpaCatalog.Find("i")?.Symbol);
        Assert.Equal("i", IpaCatalog.Find("iː")?.Symbol);
        Assert.Equal("ə", IpaCatalog.Find("ɚ")?.Symbol);
        Assert.Null(IpaCatalog.Find("not-a-symbol"));
    }

    [Fact]
    public void A_letter_is_never_shadowed_by_another_symbols_alias()
    {
        // ɹ lists "r" among its aliases, because that is how the trainer writes the
        // English r — but the letter r is the trill's own, and has to stay its own.
        Assert.Equal("r", IpaCatalog.Find("r")?.Symbol);
        Assert.Equal("alveolar trill", IpaCatalog.Find("r")?.Name);
    }

    [Fact]
    public void Every_sound_the_trainer_drills_can_be_looked_up_in_the_alphabet()
    {
        // Whatever a family calls its target sound, a learner who reads it on the family
        // tile has to be able to find that symbol here. Note this does not say the symbol
        // found is an English one: the trainer's "r" is the letter the trill owns, and it
        // is ɹ that claims it as an alias — which is what carries the family link.
        var targets = PronunciationCatalog.Families.SelectMany(f => f.TargetSounds).Distinct();

        foreach (var sound in targets)
        {
            Assert.NotNull(IpaCatalog.Find(sound));
        }
    }

    [Fact]
    public void Every_trainer_family_is_reachable_from_the_alphabet()
    {
        var linked = IpaCatalog.All
            .Select(e => IpaCatalog.FamilyKeyFor(e.Symbol))
            .Where(key => key is not null)
            .ToHashSet();

        foreach (var family in PronunciationCatalog.Families)
        {
            Assert.Contains(family.Key, linked);
        }
    }

    [Fact]
    public void FamilyKeyFor_sends_the_trainers_r_to_the_english_r_not_to_the_trill()
    {
        Assert.Equal("r-sound", IpaCatalog.FamilyKeyFor("ɹ"));
        Assert.Null(IpaCatalog.FamilyKeyFor("r"));
    }

    [Fact]
    public void FamilyKeyFor_is_null_for_a_sound_no_family_drills_and_for_an_unknown_one()
    {
        Assert.Null(IpaCatalog.FamilyKeyFor("ʘ"));
        Assert.Null(IpaCatalog.FamilyKeyFor("not-a-symbol"));
    }

    [Fact]
    public void An_english_marked_symbol_always_has_an_english_example()
    {
        // The "only English sounds" filter leaves a row on screen; a row whose only
        // example is foreign would then be the answer to "how do I say this in English".
        foreach (var entry in IpaCatalog.All.Where(e => e.InEnglish && e.ExampleWord is not null))
        {
            Assert.Equal("English", entry.ExampleLanguage);
        }
    }
}
