using LanguageLab.Domain.Pronunciation;

namespace LanguageLab.Tests;

public class PronunciationAnswerCheckerTests
{
    [Fact]
    public void An_exact_match_scores_100_and_is_correct()
    {
        var (score, outcome) = PronunciationAnswerChecker.Check("ship", "ship");

        Assert.Equal(100, score);
        Assert.Equal(PronunciationOutcome.Correct, outcome);
    }

    [Fact]
    public void An_exact_match_is_case_and_punctuation_insensitive()
    {
        var (score, outcome) = PronunciationAnswerChecker.Check("ship", "Ship.");

        Assert.Equal(100, score);
        Assert.Equal(PronunciationOutcome.Correct, outcome);
    }

    [Fact]
    public void A_close_mishearing_can_still_score_above_threshold()
    {
        var (score, outcome) = PronunciationAnswerChecker.Check("ship", "shep");

        Assert.True(score >= PronunciationAnswerChecker.CorrectThreshold);
        Assert.Equal(PronunciationOutcome.Correct, outcome);
    }

    [Fact]
    public void A_short_word_with_one_character_wrong_is_correct_despite_a_low_score()
    {
        // The score is length-normalized, so one edit in a 3-letter word is only 67 —
        // under the threshold. A single edit on a real word is ordinary recognizer
        // noise, not a mispronunciation, so the absolute-distance rule has to carry it.
        var (score, outcome) = PronunciationAnswerChecker.Check("big", "bit");

        Assert.True(score < PronunciationAnswerChecker.CorrectThreshold);
        Assert.Equal(PronunciationOutcome.Correct, outcome);
    }

    [Fact]
    public void A_short_word_missing_a_character_is_correct()
    {
        var (_, outcome) = PronunciationAnswerChecker.Check("good", "god");

        Assert.Equal(PronunciationOutcome.Correct, outcome);
    }

    [Fact]
    public void A_one_letter_word_heard_as_another_letter_is_correct()
    {
        // Worst case of the old rule: for a 1-character target anything but an exact
        // match scored 0, so the word could only ever be passed letter-perfect.
        var (score, outcome) = PronunciationAnswerChecker.Check("a", "e");

        Assert.Equal(0, score);
        Assert.Equal(PronunciationOutcome.Correct, outcome);
    }

    [Fact]
    public void A_short_word_with_two_characters_wrong_is_still_wrong()
    {
        // The tolerance is one edit, not "short words always pass".
        var (_, outcome) = PronunciationAnswerChecker.Check("big", "top");

        Assert.Equal(PronunciationOutcome.Wrong, outcome);
    }

    [Fact]
    public void A_completely_different_word_scores_low_and_is_wrong()
    {
        var (score, outcome) = PronunciationAnswerChecker.Check("ship", "elephant");

        Assert.True(score < PronunciationAnswerChecker.CorrectThreshold);
        Assert.Equal(PronunciationOutcome.Wrong, outcome);
    }

    [Fact]
    public void An_empty_transcript_scores_zero_and_is_wrong()
    {
        var (score, outcome) = PronunciationAnswerChecker.Check("ship", "");

        Assert.Equal(0, score);
        Assert.Equal(PronunciationOutcome.Wrong, outcome);
    }

    [Fact]
    public void A_multi_word_transcript_is_graded_against_its_closest_token()
    {
        var (score, outcome) = PronunciationAnswerChecker.Check("ship", "um ship yeah");

        Assert.Equal(100, score);
        Assert.Equal(PronunciationOutcome.Correct, outcome);
    }
}
