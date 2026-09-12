using LanguageLab.Domain.Entities;
using LanguageLab.Domain.IrregularVerbs;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public sealed record FormProgressView(VerbForm Form, FormState State, int Streak, int Correct, int Wrong);

public sealed record VerbView(
    string V1, string V2, string V3, string Translation, bool Learned, IReadOnlyList<FormProgressView> Forms);

public sealed record StepView(
    int Step,
    string Title,
    int TotalVerbs,
    int LearnedVerbs,
    int TotalForms,
    int LearnedForms,
    IReadOnlyList<VerbView> Verbs);

public sealed record IrregularVerbsOverview(IReadOnlyList<StepView> Steps);

public sealed record SessionCardView(string V1, string V2, string V3, string Translation, VerbForm Open);

public sealed record IrregularVerbSessionView(int Step, string Title, bool Review, IReadOnlyList<SessionCardView> Cards);

public sealed record GradeResult(string Verb, bool Learned, IReadOnlyList<FormProgressView> Forms);

/// <summary>
/// Reads and grades a user's progress through the irregular-verbs program. The verb table
/// is static (IrregularVerbTable); this service only touches the per-user, per-form
/// progress rows and folds them through VerbProgress for the overview and the planner.
/// Sessions are not stored: the client posts each grade as it happens.
/// </summary>
public class IrregularVerbService
{
    private readonly ApplicationDbContext _dbContext;

    public IrregularVerbService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IrregularVerbsOverview> GetOverviewAsync(long userId)
    {
        var rows = await RowsAsync(userId);

        var steps = IrregularVerbTable.Steps
            .OrderBy(kv => kv.Key)
            .Select(kv =>
            {
                var verbs = kv.Value.Select(v => VerbProgress.Of(v, rows)).ToList();

                return new StepView(
                    kv.Key,
                    IrregularVerbTable.TitleOf(kv.Key),
                    verbs.Count,
                    verbs.Count(v => v.IsLearned),
                    verbs.Count * VerbProgress.AllForms.Count,
                    verbs.Sum(v => v.LearnedForms),
                    verbs.Select(ToView).ToList());
            })
            .ToList();

        return new IrregularVerbsOverview(steps);
    }

    public async Task<IrregularVerbSessionView?> GetSessionAsync(long userId, int step)
    {
        if (!IrregularVerbTable.Steps.ContainsKey(step))
        {
            return null;
        }

        var plan = IrregularVerbSessionPlanner.Plan(step, await RowsAsync(userId));

        var cards = plan.Cards
            .Select(c => new SessionCardView(c.Verb.V1, c.Verb.V2, c.Verb.V3, c.Verb.Translation, c.Open))
            .ToList();

        return new IrregularVerbSessionView(step, IrregularVerbTable.TitleOf(step), plan.IsReview, cards);
    }

    /// <summary>Upserts the row for that form. Null for a verb that is not in the table.</summary>
    public async Task<GradeResult?> GradeAsync(long userId, string verb, VerbForm form, bool correct, DateTime nowUtc)
    {
        var known = IrregularVerbTable.Find(verb);

        if (known == null)
        {
            return null;
        }

        var rows = await _dbContext.IrregularVerbFormProgresses
            .Where(p => p.UserId == userId && p.Verb == verb)
            .ToListAsync();

        var row = rows.FirstOrDefault(p => p.Form == form);

        if (row == null)
        {
            row = new IrregularVerbFormProgress { UserId = userId, Verb = verb, Form = form };
            rows.Add(row);
            _dbContext.IrregularVerbFormProgresses.Add(row);
        }

        row.Apply(correct, nowUtc);
        await _dbContext.SaveChangesAsync();

        var progress = VerbProgress.Of(known, rows);

        return new GradeResult(verb, progress.IsLearned, progress.Forms.Select(ToView).ToList());
    }

    private Task<List<IrregularVerbFormProgress>> RowsAsync(long userId) =>
        _dbContext.IrregularVerbFormProgresses.Where(p => p.UserId == userId).ToListAsync();

    private static VerbView ToView(VerbProgress p) =>
        new(p.Verb.V1, p.Verb.V2, p.Verb.V3, p.Verb.Translation, p.IsLearned, p.Forms.Select(ToView).ToList());

    private static FormProgressView ToView(FormProgress f) => new(f.Form, f.State, f.Streak, f.Correct, f.Wrong);
}
