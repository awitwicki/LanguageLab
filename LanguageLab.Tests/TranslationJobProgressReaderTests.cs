using LanguageLab.Application.Translation.Queue;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using LanguageLab.Tests.Fakes;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class TranslationJobProgressReaderTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 8, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static TranslationJobProgressReader Reader(ApplicationDbContext db, bool configured = true) =>
        new(db, new FakeWordBatchTranslator(new Dictionary<string, string>()) { IsConfigured = configured });

    [Fact]
    public async Task A_pending_job_with_work_left_reports_progress()
    {
        await using var db = NewContext();
        db.TranslationJobs.Add(new TranslationJob
        {
            DictionaryId = 1, Language = "uk", Status = TranslationJobStatus.Pending, Total = 10, Done = 4, CreatedAt = Now,
        });
        await db.SaveChangesAsync();

        var progress = await Reader(db).GetAsync(1, "uk");

        Assert.Equal(new TranslationProgress(4, 10), progress);
    }

    [Fact]
    public async Task No_job_reports_null()
    {
        await using var db = NewContext();

        Assert.Null(await Reader(db).GetAsync(1, "uk"));
    }

    [Theory]
    [InlineData(TranslationJobStatus.Completed)]
    [InlineData(TranslationJobStatus.Failed)]
    public async Task A_finished_job_reports_null(TranslationJobStatus status)
    {
        await using var db = NewContext();
        db.TranslationJobs.Add(new TranslationJob
        {
            DictionaryId = 1, Language = "uk", Status = status, Total = 10, Done = 10, CreatedAt = Now,
        });
        await db.SaveChangesAsync();

        Assert.Null(await Reader(db).GetAsync(1, "uk"));
    }

    [Fact]
    public async Task A_pending_job_with_nothing_left_reports_null()
    {
        await using var db = NewContext();
        db.TranslationJobs.Add(new TranslationJob
        {
            DictionaryId = 1, Language = "uk", Status = TranslationJobStatus.Pending, Total = 0, Done = 0, CreatedAt = Now,
        });
        await db.SaveChangesAsync();

        Assert.Null(await Reader(db).GetAsync(1, "uk"));
    }

    [Fact]
    public async Task A_different_languages_job_does_not_answer()
    {
        await using var db = NewContext();
        db.TranslationJobs.Add(new TranslationJob
        {
            DictionaryId = 1, Language = "pl", Status = TranslationJobStatus.Pending, Total = 10, Done = 0, CreatedAt = Now,
        });
        await db.SaveChangesAsync();

        Assert.Null(await Reader(db).GetAsync(1, "uk"));
    }

    /// <summary>Final review, finding 2: an unconfigured translator never completes a job, so a
    /// "Translating… 0 of N" banner that can never move must not show at all.</summary>
    [Fact]
    public async Task An_unconfigured_translator_reports_null_even_with_work_left()
    {
        await using var db = NewContext();
        db.TranslationJobs.Add(new TranslationJob
        {
            DictionaryId = 1, Language = "uk", Status = TranslationJobStatus.Pending, Total = 10, Done = 4, CreatedAt = Now,
        });
        await db.SaveChangesAsync();

        Assert.Null(await Reader(db, configured: false).GetAsync(1, "uk"));
    }
}
