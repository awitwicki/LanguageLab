using LanguageLab.Api.Endpoints;
using LanguageLab.Application.Services;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

/// <summary>
/// <see cref="IrregularVerbEndpoints.ParseDrill"/> is what `GET /next` uses to read its
/// query string, by hand: minimal API's own binding for a query enum is case-sensitive
/// (`Enum.TryParse` without `ignoreCase: true`), so it would 400 on the lowercase `batch`
/// the SPA sends. No HTTP here — this is the parsing and validation behind the endpoint.
/// </summary>
public class IrregularVerbEndpointsTests
{
    /// <summary>Review focus 2: the SPA sends lowercase.</summary>
    [Theory]
    [InlineData("batch")]
    [InlineData("Batch")]
    [InlineData("BATCH")]
    public void Batch_is_read_case_insensitively(string mode)
    {
        var request = IrregularVerbEndpoints.ParseDrill(mode, group: 3, scope: null, exclude: null);

        Assert.Equal(new DrillRequest(DrillMode.Batch, 3, DrillScope.Stage, null), request);
    }

    [Theory]
    [InlineData("stage", DrillScope.Stage)]
    [InlineData("Cumulative", DrillScope.Cumulative)]
    public void A_free_run_reads_its_scope_case_insensitively(string scope, DrillScope expected)
    {
        var request = IrregularVerbEndpoints.ParseDrill("free", group: 2, scope, exclude: null);

        Assert.Equal(new DrillRequest(DrillMode.Free, 2, expected, null), request);
    }

    [Fact]
    public void The_whole_catalog_needs_no_stage_and_ignores_one()
    {
        Assert.Equal(
            new DrillRequest(DrillMode.Free, null, DrillScope.All, null),
            IrregularVerbEndpoints.ParseDrill("free", group: null, "all", exclude: null));

        Assert.Equal(
            new DrillRequest(DrillMode.Free, null, DrillScope.All, null),
            IrregularVerbEndpoints.ParseDrill("free", group: 2, "all", exclude: null));
    }

    [Fact]
    public void The_card_on_screen_is_carried_through()
    {
        var request = IrregularVerbEndpoints.ParseDrill("batch", 1, null, exclude: "cut");

        Assert.Equal("cut", request!.Exclude);
    }

    /// <summary>Review focus 2: junk and numbers must not slip through as enum values.</summary>
    [Theory]
    [InlineData(null, 1, null)]          // no mode
    [InlineData("", 1, null)]            // empty mode
    [InlineData("0", 1, null)]           // numeric enum value
    [InlineData("learn", 1, null)]       // a mode from the old trainer
    [InlineData("batch", null, null)]    // batch without a stage
    [InlineData("batch", 1, "stage")]    // batch does not take a scope
    [InlineData("batch", 0, null)]       // stage out of range
    [InlineData("batch", 5, null)]
    [InlineData("batch", -1, null)]
    [InlineData("free", 1, null)]        // free without a scope
    [InlineData("free", 1, "1")]         // numeric scope value
    [InlineData("free", 1, "mixed")]     // a scope from the old trainer
    [InlineData("free", null, "stage")]  // a stage run without a stage
    [InlineData("free", 1, "cumulative")] // stage 1 has nothing earlier
    [InlineData("free", 9, "all")]       // stage out of range even when ignored
    public void Incoherent_parameters_are_refused(string? mode, int? group, string? scope)
    {
        Assert.Null(IrregularVerbEndpoints.ParseDrill(mode, group, scope, exclude: null));
    }

    [Fact]
    public void Every_stage_of_the_catalog_is_accepted()
    {
        for (var group = 1; group <= IrregularVerbCatalog.GroupCount; group++)
        {
            Assert.NotNull(IrregularVerbEndpoints.ParseDrill("batch", group, null, null));
        }
    }

    private static VerbAnswerRequest Answer(
        string verb = "cut", PromptForm form = PromptForm.V1, DrillMode mode = DrillMode.Batch, int? group = 1) =>
        new(verb, form, Known: true, ResponseMs: 500, mode, group);

    /// <summary>
    /// Found on review: `POST /answers`'s body is ordinary JSON, not a query string, but a
    /// stale or hand-crafted client can still send a verb-less body or an out-of-range enum
    /// — `JsonStringEnumConverter` accepts integers by default — straight into the
    /// append-only answer log.
    /// </summary>
    [Fact]
    public void A_well_formed_answer_is_valid()
    {
        Assert.True(IrregularVerbEndpoints.IsValidAnswer(Answer()));
        Assert.True(IrregularVerbEndpoints.IsValidAnswer(Answer(mode: DrillMode.Free, group: null)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void An_empty_or_missing_verb_is_refused(string? verb)
    {
        Assert.False(IrregularVerbEndpoints.IsValidAnswer(Answer(verb: verb!)));
    }

    [Fact]
    public void An_out_of_range_prompt_form_is_refused()
    {
        Assert.False(IrregularVerbEndpoints.IsValidAnswer(Answer(form: (PromptForm)7)));
    }

    [Fact]
    public void An_out_of_range_mode_is_refused()
    {
        Assert.False(IrregularVerbEndpoints.IsValidAnswer(Answer(mode: (DrillMode)9)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5)]
    public void A_stage_outside_the_catalog_is_refused(int group)
    {
        Assert.False(IrregularVerbEndpoints.IsValidAnswer(Answer(group: group)));
    }
}
