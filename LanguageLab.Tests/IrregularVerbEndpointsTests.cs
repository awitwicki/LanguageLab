using LanguageLab.Api.Endpoints;
using LanguageLab.Application.Services;
using LanguageLab.Domain.IrregularVerbs;
using LanguageLab.Domain.Languages;

namespace LanguageLab.Tests;

/// <summary>
/// <see cref="IrregularVerbEndpoints.ParseSession"/> is what `GET /session` uses to read its
/// query string, by hand: minimal API's own binding for a query enum is case-sensitive
/// (`Enum.TryParse` without `ignoreCase: true`), so it would 400 on the lowercase `batch` the
/// SPA sends. No HTTP here — this is the parsing and validation behind the endpoints.
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
        var request = IrregularVerbEndpoints.ParseSession(mode, group: 3, scope: null);

        Assert.Equal(new SessionRequest(DrillMode.Batch, 3, DrillScope.Stage), request);
    }

    [Theory]
    [InlineData("stage", DrillScope.Stage)]
    [InlineData("Cumulative", DrillScope.Cumulative)]
    public void A_free_run_reads_its_scope_case_insensitively(string scope, DrillScope expected)
    {
        var request = IrregularVerbEndpoints.ParseSession("free", group: 2, scope);

        Assert.Equal(new SessionRequest(DrillMode.Free, 2, expected), request);
    }

    [Fact]
    public void The_whole_catalog_needs_no_stage_and_ignores_one()
    {
        Assert.Equal(
            new SessionRequest(DrillMode.Free, null, DrillScope.All),
            IrregularVerbEndpoints.ParseSession("free", group: null, "all"));

        Assert.Equal(
            new SessionRequest(DrillMode.Free, null, DrillScope.All),
            IrregularVerbEndpoints.ParseSession("free", group: 2, "all"));
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
        Assert.Null(IrregularVerbEndpoints.ParseSession(mode, group, scope));
    }

    [Fact]
    public void Every_stage_of_the_catalog_is_accepted()
    {
        for (var group = 1; group <= IrregularVerbCatalog.GroupCount; group++)
        {
            Assert.NotNull(IrregularVerbEndpoints.ParseSession("batch", group, null));
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

    private static VerbAnswersRequest Chunk(params VerbAnswerRequest[] answers) => new(answers);

    /// <summary>
    /// A chunk is all-or-nothing: the browser retries a failed request, and without an answer
    /// identity to deduplicate against, a half-applied chunk would count its first answers
    /// twice. So one bad entry refuses the whole body.
    /// </summary>
    [Fact]
    public void A_well_formed_chunk_is_valid()
    {
        Assert.True(IrregularVerbEndpoints.IsValidChunk(Chunk(Answer())));
        Assert.True(IrregularVerbEndpoints.IsValidChunk(
            Chunk(Answer(), Answer("put"), Answer(mode: DrillMode.Free, group: null))));
    }

    [Fact]
    public void A_chunk_with_no_answers_is_refused()
    {
        Assert.False(IrregularVerbEndpoints.IsValidChunk(Chunk()));
        Assert.False(IrregularVerbEndpoints.IsValidChunk(new VerbAnswersRequest(null)));
    }

    [Fact]
    public void A_chunk_longer_than_the_cap_is_refused()
    {
        var cap = IrregularVerbEndpoints.MaxAnswersPerRequest;

        Assert.True(IrregularVerbEndpoints.IsValidChunk(
            new VerbAnswersRequest(Enumerable.Repeat(Answer(), cap).ToList())));
        Assert.False(IrregularVerbEndpoints.IsValidChunk(
            new VerbAnswersRequest(Enumerable.Repeat(Answer(), cap + 1).ToList())));
    }

    [Fact]
    public void One_bad_entry_refuses_the_whole_chunk()
    {
        Assert.False(IrregularVerbEndpoints.IsValidChunk(Chunk(Answer(), Answer(verb: ""))));
        Assert.False(IrregularVerbEndpoints.IsValidChunk(Chunk(Answer(), Answer(form: (PromptForm)7))));
        Assert.False(IrregularVerbEndpoints.IsValidChunk(Chunk(Answer(), Answer(group: 9))));
    }

    /// <summary>
    /// A verb outside the catalog used to come back as a 404 from the service. Among ninety-nine
    /// good answers that is a broken client, not a missing resource — and it must not let the
    /// other ninety-nine through either, since the retry would double-count them.
    /// </summary>
    [Fact]
    public void A_verb_outside_the_catalog_refuses_the_chunk()
    {
        Assert.False(IrregularVerbEndpoints.IsValidChunk(Chunk(Answer(), Answer(verb: "frobnicate"))));
    }

    [Theory]
    [InlineData("uk", true)]
    [InlineData("pl", false)]
    [InlineData("tl", false)]
    [InlineData(null, false)]
    public void Only_Ukrainian_learners_get_the_trainer(string? code, bool expected)
    {
        Assert.Equal(expected, IrregularVerbEndpoints.IsAvailableFor(LearnerLanguages.Find(code)));
    }
}
