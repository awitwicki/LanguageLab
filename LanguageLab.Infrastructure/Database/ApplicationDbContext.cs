using LanguageLab.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Infrastructure.Database;

public class ApplicationDbContext : DbContext
{
    public DbSet<WordPair> Words { get; set; }
    public DbSet<Dictionary> Dictionaries { get; set; }
    public DbSet<TelegramUser> Users { get; set; }
    public DbSet<Training> Trainings { get; set; }
    public DbSet<TrainingQuestion> TrainingQuestions { get; set; }
    public DbSet<KnownWord> KnownWords { get; set; }
    public DbSet<UnknownWord> UnknownWords { get; set; }
    public DbSet<WordProgress> WordProgresses { get; set; }
    public DbSet<Chapter> Chapters { get; set; }
    public DbSet<ChapterWord> ChapterWords { get; set; }
    public DbSet<DictionaryWord> DictionaryWords { get; set; }
    public DbSet<ExcludedWord> ExcludedWords { get; set; }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<WordPair>()
            .HasIndex(w => w.Word)
            .IsUnique();

        // Join-таблиця тепер сутність із навантаженням (Frequency), але назва й каскади
        // ті самі, що були за конвенцією — міграція лише додає колонку.
        // Каскад потрібен кнопці «🗑 Видалити», яка зносить WordPair глобально.
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

                    // Черга сортування завжди йде ORDER BY Frequency DESC у межах словника.
                    j.HasIndex(dw => new { dw.DictionaryId, dw.Frequency });
                });

        // Одне слово не може бути двічі відоме або двічі невідоме одному юзеру.
        // Досі це трималося тільки перевіркою в C#; сід із задачі 6 покладається на ON CONFLICT.
        builder.Entity<KnownWord>()
            .HasIndex(k => new { k.UserId, k.WordPairId })
            .IsUnique();

        builder.Entity<UnknownWord>()
            .HasIndex(u => new { u.UserId, u.WordPairId })
            .IsUnique();

        builder.Entity<WordProgress>()
            .HasIndex(p => new { p.UserId, p.WordPairId })
            .IsUnique();

        // Вибірка закріплення: слова цього юзера, не вивчені, з простроченим DueAt.
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

        // Третя полиця живе за тими самими правилами, що дві попередні.
        builder.Entity<ExcludedWord>()
            .HasIndex(e => new { e.UserId, e.WordPairId })
            .IsUnique();

        // Вибірка «останні 10» на кожній полиці — це ORDER BY CreatedAt DESC по юзеру.
        builder.Entity<KnownWord>().HasIndex(k => new { k.UserId, k.CreatedAt });
        builder.Entity<UnknownWord>().HasIndex(u => new { u.UserId, u.CreatedAt });
        builder.Entity<ExcludedWord>().HasIndex(e => new { e.UserId, e.CreatedAt });
    }
}
