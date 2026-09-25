using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Pronunciation;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Infrastructure.Database;

public class ApplicationDbContext : DbContext, IDataProtectionKeyContext
{
    public DbSet<WordPair> Words { get; set; }
    public DbSet<Dictionary> Dictionaries { get; set; }
    public DbSet<TelegramUser> Users { get; set; }
    public DbSet<Training> Trainings { get; set; }
    public DbSet<TrainingQuestion> TrainingQuestions { get; set; }
    public DbSet<KnownWord> KnownWords { get; set; }
    public DbSet<UnknownWord> UnknownWords { get; set; }
    public DbSet<WordProgress> WordProgresses { get; set; }
    public DbSet<VerbKnowledge> VerbKnowledges { get; set; }
    public DbSet<VerbAnswer> VerbAnswers { get; set; }
    public DbSet<PronunciationProgress> PronunciationProgresses { get; set; }
    public DbSet<PronunciationAttempt> PronunciationAttempts { get; set; }
    public DbSet<Chapter> Chapters { get; set; }
    public DbSet<ChapterWord> ChapterWords { get; set; }
    public DbSet<DictionaryWord> DictionaryWords { get; set; }
    public DbSet<ExcludedWord> ExcludedWords { get; set; }
    public DbSet<StarredChapter> StarredChapters { get; set; }
    public DbSet<ReaderBook> ReaderBooks { get; set; }

    /// <summary>
    /// The keys the session cookie is encrypted with. Kept in the database, not in the
    /// container's filesystem, so a redeploy does not sign everybody out.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Shared words (OwnerId null) stay unique among themselves and each user's personal
        // words unique per user: NULLS NOT DISTINCT makes the two-column index behave that
        // way (Postgres 15+). Without it every null owner would count as distinct and the
        // shared vocabulary could grow duplicates.
        builder.Entity<WordPair>()
            .HasIndex(w => new { w.Word, w.OwnerId })
            .IsUnique()
            .AreNullsDistinct(false);

        // A personal word has no life outside its owner's dictionary.
        builder.Entity<WordPair>()
            .HasOne(w => w.Owner)
            .WithMany()
            .HasForeignKey(w => w.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Login upserts by TelegramUserId; without the unique index two concurrent
        // first logins could create two accounts for the same Telegram user.
        builder.Entity<TelegramUser>()
            .HasIndex(u => u.TelegramUserId)
            .IsUnique();

        // Deleting an account must not delete the books it imported — they become
        // ownerless system dictionaries instead.
        builder.Entity<Dictionary>()
            .HasOne(d => d.Owner)
            .WithMany()
            .HasForeignKey(d => d.OwnerId)
            .OnDelete(DeleteBehavior.SetNull);

        // Listing dictionaries always filters on visibility.
        builder.Entity<Dictionary>()
            .HasIndex(d => new { d.PublicationStatus, d.OwnerId });

        // One personal dictionary per user, enforced where the get-or-create race lives.
        builder.Entity<Dictionary>()
            .HasIndex(d => d.OwnerId)
            .IsUnique()
            .HasFilter("\"IsPersonal\"")
            .HasDatabaseName("IX_Dictionaries_OwnerId_Personal");

        // The join table is now an entity with payload (Frequency), but the name and the
        // cascades are the ones the convention produced — the migration only adds a column.
        // The cascade is needed by the "🗑 Delete" button, which removes a WordPair globally.
        builder.Entity<Dictionary>()
            .HasMany(d => d.Words)
            .WithMany(w => w.Dictionaries)
            .UsingEntity<DictionaryWord>(
                l => l.HasOne(dw => dw.WordPair).WithMany()
                    .HasForeignKey(dw => dw.WordPairId).OnDelete(DeleteBehavior.Cascade),
                r => r.HasOne(dw => dw.Dictionary).WithMany()
                    .HasForeignKey(dw => dw.DictionaryId).OnDelete(DeleteBehavior.Cascade),
                j =>
                {
                    j.ToTable("DictionaryWords");
                    j.HasKey(dw => new { dw.DictionaryId, dw.WordPairId });

                    // The sorting queue always goes ORDER BY Frequency DESC within a dictionary.
                    j.HasIndex(dw => new { dw.DictionaryId, dw.Frequency });
                });

        // One word cannot be known twice or unknown twice for the same user. Until now this
        // was held only by a check in C#; the seed from task 6 relies on ON CONFLICT.
        builder.Entity<KnownWord>()
            .HasIndex(k => new { k.UserId, k.WordPairId })
            .IsUnique();

        builder.Entity<UnknownWord>()
            .HasIndex(u => new { u.UserId, u.WordPairId })
            .IsUnique();

        builder.Entity<WordProgress>()
            .HasIndex(p => new { p.UserId, p.WordPairId })
            .IsUnique();

        // The irregular-verbs trainer: one standing per user × verb, and an append-only
        // log of every card they judged. Both hang off the user; there is no session.
        builder.Entity<VerbKnowledge>()
            .HasIndex(k => new { k.UserId, k.Verb })
            .IsUnique();

        builder.Entity<VerbAnswer>()
            .HasIndex(a => new { a.UserId, a.CreatedAt });

        // The pronunciation trainer: one standing per user × word, and an append-only attempt log.
        builder.Entity<PronunciationProgress>()
            .HasIndex(p => new { p.UserId, p.Word })
            .IsUnique();

        builder.Entity<PronunciationAttempt>()
            .HasIndex(a => new { a.UserId, a.CreatedAt });

        // The review selection: this user's words, not learned, with an overdue DueAt.
        builder.Entity<WordProgress>()
            .HasIndex(p => new { p.UserId, p.IsLearned, p.DueAt });

        builder.Entity<TrainingQuestion>()
            .HasIndex(q => new { q.TrainingId, q.Order });

        builder.Entity<Chapter>()
            .HasIndex(c => new { c.DictionaryId, c.Order })
            .IsUnique();

        builder.Entity<Chapter>()
            .HasOne(c => c.Dictionary)
            .WithMany(d => d.Chapters)
            .HasForeignKey(c => c.DictionaryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ChapterWord>()
            .HasKey(cw => new { cw.ChapterId, cw.WordPairId });

        builder.Entity<ChapterWord>()
            .HasOne(cw => cw.Chapter)
            .WithMany(c => c.Words)
            .HasForeignKey(cw => cw.ChapterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ChapterWord>()
            .HasOne(cw => cw.WordPair)
            .WithMany()
            .HasForeignKey(cw => cw.WordPairId)
            .OnDelete(DeleteBehavior.Cascade);

        // The third shelf lives by the same rules as the previous two.
        builder.Entity<ExcludedWord>()
            .HasIndex(e => new { e.UserId, e.WordPairId })
            .IsUnique();

        // The "last 10" selection on each shelf is ORDER BY CreatedAt DESC per user.
        builder.Entity<KnownWord>().HasIndex(k => new { k.UserId, k.CreatedAt });
        builder.Entity<UnknownWord>().HasIndex(u => new { u.UserId, u.CreatedAt });
        builder.Entity<ExcludedWord>().HasIndex(e => new { e.UserId, e.CreatedAt });

        // One star per user per chapter; the list on the home screen is a lookup by user.
        builder.Entity<StarredChapter>()
            .HasIndex(s => new { s.UserId, s.ChapterId })
            .IsUnique();

        // The reader's library: one row per user per file, gone with the user.
        builder.Entity<ReaderBook>()
            .HasIndex(b => new { b.UserId, b.FileHash })
            .IsUnique();

        builder.Entity<ReaderBook>()
            .HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The library links a book to the dictionary imported from the same file.
        builder.Entity<Dictionary>()
            .HasIndex(d => d.FileHash);
    }
}
