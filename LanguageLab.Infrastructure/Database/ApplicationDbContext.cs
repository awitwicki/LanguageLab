using LanguageLab.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Infrastructure.Database;

public class ApplicationDbContext : DbContext
{
    public DbSet<WordPair> Words { get; set; }
   
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {

    }
}
