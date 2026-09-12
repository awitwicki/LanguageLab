using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class LearningPathTests
{
    private static Dictionary<string, VerbState> States(params (string Verb, VerbState State)[] pairs) =>
        pairs.ToDictionary(p => p.Verb, p => p.State, StringComparer.Ordinal);

    private static Dictionary<string, VerbState> FamilyAt(string family, VerbState state, int count) =>
        States(IrregularVerbCatalog.VerbsOf(family).Take(count).Select(v => (v.V1, state)).ToArray());

    [Fact]
    public void With_no_progress_only_the_first_family_is_available()
    {
        var path = LearningPath.Evaluate(States());

        Assert.Equal(15, path.Count);
        Assert.Equal(FamilyStatus.Available, path[0].Status);
        Assert.All(path.Skip(1), f => Assert.Equal(FamilyStatus.Locked, f.Status));
        Assert.Equal((9, 0, false), (path[0].Total, path[0].Learned, path[0].Started));
        Assert.False(LearningPath.MixedAvailable(path));
    }

    [Fact]
    public void Eighty_percent_learned_makes_a_family_done_and_unlocks_the_next()
    {
        var path = LearningPath.Evaluate(FamilyAt("same", VerbState.Learned, 8));

        Assert.Equal(FamilyStatus.Done, path[0].Status);
        Assert.Equal(8, path[0].Learned);
        Assert.Equal(FamilyStatus.Available, path[1].Status);
        Assert.Equal(FamilyStatus.Locked, path[2].Status);
    }

    [Fact]
    public void Below_the_threshold_the_next_family_stays_locked()
    {
        var path = LearningPath.Evaluate(FamilyAt("same", VerbState.Learned, 7));

        Assert.Equal(FamilyStatus.Available, path[0].Status);
        Assert.Equal(FamilyStatus.Locked, path[1].Status);
    }

    [Fact]
    public void A_family_with_any_progress_stays_available_even_if_the_previous_one_regressed()
    {
        var states = FamilyAt("same", VerbState.Learned, 7);
        states["come"] = VerbState.Learning1;

        var path = LearningPath.Evaluate(states);

        Assert.True(path[1].Started);
        Assert.Equal(FamilyStatus.Available, path[1].Status);
    }

    [Fact]
    public void Two_done_families_open_the_mixed_session()
    {
        var states = FamilyAt("same", VerbState.Learned, 9);

        foreach (var (verb, state) in FamilyAt("back", VerbState.Learned, 3))
        {
            states[verb] = state;
        }

        var path = LearningPath.Evaluate(states);

        Assert.Equal(FamilyStatus.Done, path[1].Status);
        Assert.Equal(FamilyStatus.Available, path[2].Status);
        Assert.True(LearningPath.MixedAvailable(path));
    }

    [Fact]
    public void Forgotten_verbs_do_not_count_as_learned()
    {
        var states = FamilyAt("same", VerbState.Learned, 8);
        states["cut"] = VerbState.Forgotten;

        var path = LearningPath.Evaluate(states);

        Assert.Equal(7, path[0].Learned);
        Assert.Equal(FamilyStatus.Available, path[0].Status);
    }
}
