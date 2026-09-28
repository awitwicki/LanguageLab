using LanguageLab.Domain.Lexicon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LanguageLab.Application.Lexicon;

public static class LexiconServiceCollectionExtensions
{
    /// <summary>
    /// The English lexicon as one shared instance: its table is immutable and read once. A
    /// lexicon registered earlier — a test's fake — is kept.
    /// </summary>
    public static IServiceCollection AddEnglishLexicon(this IServiceCollection services)
    {
        services.TryAddSingleton<IEnglishLexicon, EnglishLexicon>();
        return services;
    }
}
