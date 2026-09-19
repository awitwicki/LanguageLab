namespace LanguageLab.Domain.Pronunciation;

public enum Accent
{
    Us,
    Uk,
}

public enum PronunciationOutcome
{
    Correct,
    Wrong,
}

/// <summary>
/// Grades the browser SpeechRecognition transcript of an attempt against the target word.
/// This is a word-recognition proxy for pronunciation quality, not phoneme-level scoring —
/// see the design doc's Goal section for why that's the deliberate, honest scope.
/// </summary>
public static class PronunciationAnswerChecker
{
    /// <summary>Score at or above which an attempt counts as correct.</summary>
    public const int CorrectThreshold = 75;

    /// <summary>
    /// Edit distance that always counts as correct, whatever the score says. The score is
    /// length-normalized, so on a short word one mistyped character already falls under
    /// <see cref="CorrectThreshold"/> — one edit in a 3-letter word scores 67, and for a
    /// 1-letter word anything but an exact match scores 0. That makes short words nearly
    /// impossible to pass through ordinary speech-recognition noise, which defeats the
    /// point of a deliberately forgiving proxy, so a single edit is tolerated outright.
    /// </summary>
    public const int ToleratedEditDistance = 1;

    /// <summary>
    /// Returns the attempt's score out of 100 and its outcome. An attempt is correct when
    /// it is within <see cref="ToleratedEditDistance"/> of the target or scores at least
    /// <see cref="CorrectThreshold"/> — whichever is the more forgiving for that word's length.
    /// </summary>
    public static (int Score, PronunciationOutcome Outcome) Check(string targetWord, string transcript)
    {
        var target = Normalize(targetWord);
        var tokens = Normalize(transcript).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (target.Length == 0 || tokens.Length == 0)
        {
            return (0, PronunciationOutcome.Wrong);
        }

        var bestToken = tokens.OrderBy(t => Levenshtein(target, t)).First();
        var distance = Levenshtein(target, bestToken);
        var maxLen = Math.Max(target.Length, bestToken.Length);
        var score = Math.Clamp((int)Math.Round(100.0 * (1 - (double)distance / maxLen)), 0, 100);
        var correct = distance <= ToleratedEditDistance || score >= CorrectThreshold;

        return (score, correct ? PronunciationOutcome.Correct : PronunciationOutcome.Wrong);
    }

    private static string Normalize(string s) =>
        new(s.Trim().ToLowerInvariant().Where(c => char.IsLetter(c) || char.IsWhiteSpace(c)).ToArray());

    private static int Levenshtein(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();

        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            previous = current;
        }

        return previous[b.Length];
    }
}
