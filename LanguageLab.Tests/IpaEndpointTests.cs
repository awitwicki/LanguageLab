using LanguageLab.Api.Endpoints;
using LanguageLab.Domain.Pronunciation;

namespace LanguageLab.Tests;

/// <summary>
/// What <c>GET /api/pronunciation/alphabet</c> answers with. No HTTP here — the endpoint
/// is a projection of a static catalog, so the DTO it hands out is the whole behaviour.
/// </summary>
public class IpaEndpointTests
{
    [Fact]
    public void The_alphabet_carries_every_section_and_symbol_of_the_catalog()
    {
        var sections = PronunciationEndpoints.Alphabet.Sections;

        Assert.Equal(IpaCatalog.Sections.Count, sections.Count);
        Assert.Equal(
            IpaCatalog.Sections.SelectMany(s => s.Entries).Select(e => e.Symbol),
            sections.SelectMany(s => s.Entries).Select(e => e.Symbol));
    }

    [Fact]
    public void A_recording_arrives_as_a_url_under_the_audio_directory()
    {
        var entry = PronunciationEndpoints.Alphabet.Sections
            .SelectMany(s => s.Entries)
            .First(e => e.SoundAudio is not null);

        Assert.StartsWith(PronunciationAudio.UrlPrefix, entry.SoundAudio);
        Assert.DoesNotContain(PronunciationAudio.UrlPrefix + PronunciationAudio.UrlPrefix, entry.SoundAudio);
    }

    [Fact]
    public void A_missing_recording_stays_null_instead_of_becoming_a_url_to_nothing()
    {
        var entries = PronunciationEndpoints.Alphabet.Sections.SelectMany(s => s.Entries).ToList();
        var catalog = IpaCatalog.All.ToDictionary(e => e.Symbol);

        // Something in the chart has no recording — a mark has no sound of its own — so
        // this is a live case, not a hypothetical one.
        Assert.Contains(entries, e => e.SoundAudio is null);

        foreach (var entry in entries)
        {
            var source = catalog[entry.Symbol];
            Assert.Equal(PronunciationAudio.Url(source.SoundAudioFile), entry.SoundAudio);
            Assert.Equal(PronunciationAudio.Url(source.WordAudioFile), entry.WordAudio);
        }
    }

    [Fact]
    public void A_sound_the_trainer_drills_carries_the_family_that_drills_it()
    {
        var th = PronunciationEndpoints.Alphabet.Sections
            .SelectMany(s => s.Entries)
            .Single(e => e.Symbol == "θ");

        Assert.Equal(IpaCatalog.FamilyKeyFor("θ"), th.FamilyKey);
        Assert.NotNull(th.FamilyKey);
    }

    [Fact]
    public void The_chart_is_built_once_and_handed_out_as_the_same_instance()
    {
        Assert.Same(PronunciationEndpoints.Alphabet, PronunciationEndpoints.Alphabet);
    }
}
