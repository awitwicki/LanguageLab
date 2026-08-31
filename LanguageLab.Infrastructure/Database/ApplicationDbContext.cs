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

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<WordPair>()
            .HasIndex(w => w.Word)
            .IsUnique();

        // Join-таблиця налаштована явно, а не за конвенцією: інакше EF назвав би колонки
        // "DictionariesId"/"WordsId", і SQL-сід із задачі 6 залежав би від вгадування імен.
        // Каскад потрібен кнопці «🗑 Видалити», яка зносить WordPair глобально.
        builder.Entity<Dictionary>()
            .HasMany(d => d.Words)
            .WithMany(w => w.Dictionaries)
            .UsingEntity(
                "DictionaryWords",
                l => l.HasOne(typeof(WordPair)).WithMany()
                    .HasForeignKey("WordPairId").OnDelete(DeleteBehavior.Cascade),
                r => r.HasOne(typeof(Dictionary)).WithMany()
                    .HasForeignKey("DictionaryId").OnDelete(DeleteBehavior.Cascade),
                j => j.HasKey("DictionaryId", "WordPairId"));

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
    }
}
