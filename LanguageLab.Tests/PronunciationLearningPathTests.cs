using LanguageLab.Domain.Pronunciation;

namespace LanguageLab.Tests;

public class PronunciationLearningPathTests
{
    [Fact]
    public void An_empty_user_has_only_the_first_family_available()
    {
        var path = PronunciationLearningPath.Evaluate(new Dictionary<string, PronunciationState>());

        Assert.Equal(PronunciationCatalog.Families.Count, path.Count);
        Assert.Equal(FamilyStatus.Available, path[0].Status);

        for (var i = 1; i < path.Count; i++)
        {
            Assert.Equal(FamilyStatus.Locked, path[i].Status);
        }
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
    public void The_next_family_unlocks_once_the_previous_one_is_done()
    {
        if (PronunciationCatalog.Families.Count < 2)
        {
            return;
        }

        var firstFamily = PronunciationCatalog.Families[0];
        var words = PronunciationCatalog.WordsOf(firstFamily.Key);
        var masteredCount = (int)Math.Ceiling(words.Count * 0.8);
        var states = words.Take(masteredCount).ToDictionary(w => w.Word, _ => PronunciationState.Mastered);

        var path = PronunciationLearningPath.Evaluate(states);

        Assert.Equal(FamilyStatus.Available, path[1].Status);
    }

    [Fact]
    public void A_family_with_any_started_word_stays_available_even_if_the_previous_one_regresses()
    {
        if (PronunciationCatalog.Families.Count < 2)
        {
            return;
        }

        var secondFamily = PronunciationCatalog.Families[1];
        var startedWord = PronunciationCatalog.WordsOf(secondFamily.Key)[0];

        var states = new Dictionary<string, PronunciationState> { [startedWord.Word] = PronunciationState.Learning };
        var path = PronunciationLearningPath.Evaluate(states);

        Assert.Equal(FamilyStatus.Available, path[1].Status);
    }
}
