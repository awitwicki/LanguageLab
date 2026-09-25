using LanguageLab.Domain.Pronunciation;

namespace LanguageLab.Tests;

public class PronunciationLearningPathTests
{
    [Fact]
    public void An_empty_user_has_every_family_available()
    {
        var path = PronunciationLearningPath.Evaluate(new Dictionary<string, PronunciationState>());

        Assert.Equal(PronunciationCatalog.Families.Count, path.Count);
        Assert.All(path, f => Assert.Equal(FamilyStatus.Available, f.Status));
    }

    [Fact]
    public void A_family_is_done_once_at_least_80_percent_of_its_words_are_mastered()
    {
        var firstFamily = PronunciationCatalog.Families[0];
        var words = PronunciationCatalog.WordsOf(firstFamily.Key);
        var masteredCount = (int)Math.Ceiling(words.Count * 0.8);

        var states = words.Take(masteredCount).ToDictionary(w => w.Word, _ => PronunciationState.Mastered);
        var path = PronunciationLearningPath.Evaluate(states);

        Assert.Equal(FamilyStatus.Done, path[0].Status);
    }

    [Fact]
    public void Below_the_threshold_a_family_stays_available()
    {
        var firstFamily = PronunciationCatalog.Families[0];
        var words = PronunciationCatalog.WordsOf(firstFamily.Key);
        var masteredCount = (int)Math.Ceiling(words.Count * 0.8) - 1;

        var states = words.Take(masteredCount).ToDictionary(w => w.Word, _ => PronunciationState.Mastered);
        var path = PronunciationLearningPath.Evaluate(states);

        Assert.Equal(FamilyStatus.Available, path[0].Status);
        Assert.Equal(masteredCount, path[0].Mastered);
    }
}
