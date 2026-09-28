using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LanguageLab.Application.Books;

public static class BooksServiceCollectionExtensions
{
    /// <summary>BookParser is stateless and thread-safe by design — one instance serves every request.</summary>
    public static IServiceCollection AddBookParser(this IServiceCollection services)
    {
        services.TryAddSingleton<IBookParser, BookParser>();

        return services;
    }
}
