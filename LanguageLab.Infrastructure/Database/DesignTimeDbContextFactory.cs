using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LanguageLab.Infrastructure.Database;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        // Use a dummy connection string for migration generation 
        // or read it from environment variables
        optionsBuilder.UseNpgsql("Host=localhost;Database=dummy;Username=postgres;Password=password");

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
