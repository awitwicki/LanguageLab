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
   
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {

    }
}
