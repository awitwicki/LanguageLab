using LanguageLab.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Infrastructure.Database;

public class ApplicationDbContext : DbContext
{
    public DbSet<WordPair> Words { get; set; }
    public DbSet<Dictionary> Dictionaries { get; set; }
    public DbSet<TelegramUser> Users { get; set; }
    public DbSet<Training> Trainings { get; set; }
    public DbSet<TrainingEvent> TrainingEvents { get; set; }
    public DbSet<KnownWord> KnownWords { get; set; }
    public DbSet<UnknownWord> UnknownWords { get; set; }
   
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {

    }
    
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<WordPair>()
            .HasIndex(u => u.Word)
            .IsUnique();
    }
}
