using LanguageLab.Domain.Entities;

namespace LanguageLab.Domain.Training;

/// <summary>
/// Builds the question queue for one session. Pure: all non-determinism comes in through Random.
/// </summary>
public static class QuestionQueueBuilder
{
    public const int OptionsPerQuestion = 6;

    public static IReadOnlyList<PlannedQuestion> Build(
        IReadOnlyList<WordPair> targets,
        int repeats,
        IReadOnlyList<WordPair> distractorPool,
        DirectionPolicy policy,
        Random rng)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(distractorPool);
        ArgumentNullException.ThrowIfNull(rng);

        if (repeats < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(repeats), repeats, "There must be at least 1 repeat.");
        }

        if (targets.Count == 0)
        {
            return [];
        }

        var order = BuildOrder(targets, repeats, rng);
        var questions = new List<PlannedQuestion>(order.Count);

        foreach (var target in order)
        {
            var direction = policy switch
            {
                DirectionPolicy.EnToUa => QuestionDirection.EnToUa,
                DirectionPolicy.Random => rng.Next(2) == 0 ? QuestionDirection.EnToUa : QuestionDirection.UaToEn,
                _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown direction policy.")
            };

            questions.Add(new PlannedQuestion(target.Id, direction, BuildOptions(target, distractorPool, rng)));
        }

        return questions;
    }

    /// <summary>
    /// Lays the words out so the same word never comes twice in a row: each step takes
    /// the word with the most repeats left, except the one just placed.
    /// </summary>
    private static List<WordPair> BuildOrder(IReadOnlyList<WordPair> targets, int repeats, Random rng)
    {
        var total = targets.Count * repeats;
        var left = new int[targets.Count];
        Array.Fill(left, repeats);

        var result = new List<WordPair>(total);
        var candidates = new List<int>(targets.Count);
        var previousIndex = -1;

        for (var placed = 0; placed < total; placed++)
        {
            candidates.Clear();
            var bestLeft = 0;

            for (var i = 0; i < targets.Count; i++)
            {
                if (left[i] == 0 || i == previousIndex)
                {
                    continue;
                }

                if (left[i] > bestLeft)
                {
                    bestLeft = left[i];
                    candidates.Clear();
                }

                if (left[i] == bestLeft)
                {
                    candidates.Add(i);
                }
            }

            if (candidates.Count == 0)
            {
                // Only the word just placed is left — the queue cannot be filled otherwise.
                for (var i = 0; i < targets.Count; i++)
                {
                    if (left[i] > 0)
                    {
                        candidates.Add(i);
                    }
                }
            }

            var chosen = candidates[rng.Next(candidates.Count)];
            result.Add(targets[chosen]);
            left[chosen]--;
            previousIndex = chosen;
        }

        return result;
    }

    private static IReadOnlyList<long> BuildOptions(WordPair target, IReadOnlyList<WordPair> pool, Random rng)
    {
        var valid = new List<WordPair>(pool.Count);

        foreach (var candidate in pool)
        {
            if (candidate.Id == target.Id)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate.Translation))
            {
                continue;
            }

            // A distractor with the same translation would make two correct buttons.
            if (string.Equals(candidate.Translation, target.Translation, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(candidate.Word, target.Word, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            valid.Add(candidate);
        }

        if (valid.Count == 0)
        {
            throw new InvalidOperationException(
                $"No valid distractor for the word '{target.Word}' (id {target.Id}).");
        }

        Shuffle(valid, rng);

        var options = new List<long>(OptionsPerQuestion) { target.Id };

        foreach (var distractor in valid)
        {
            if (options.Count == OptionsPerQuestion)
            {
                break;
            }

            if (!options.Contains(distractor.Id))
            {
                options.Add(distractor.Id);
            }
        }

        Shuffle(options, rng);
        return options;
    }

    private static void Shuffle<T>(IList<T> items, Random rng)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
