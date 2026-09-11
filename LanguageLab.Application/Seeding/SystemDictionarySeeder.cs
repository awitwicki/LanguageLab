using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Seeding;

/// <summary>
/// Creates the built-in dictionaries that ship with the app (Owner == null, public).
/// Runs at startup right after migrations, so a fresh deployment has them without
/// anyone importing anything.
/// </summary>
public class SystemDictionarySeeder
{
    private readonly ApplicationDbContext _dbContext;

    public SystemDictionarySeeder(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SeedAsync()
    {
        // Name + no owner is the identity of a system dictionary; deleting it from
        // the admin UI brings it back on the next start, which is the point of built-in.
        var exists = await _dbContext.Dictionaries
            .AnyAsync(d => d.OwnerId == null && d.Name == IrregularVerbs.DictionaryName);

        if (exists)
        {
            return;
        }

        var dictionary = new Domain.Entities.Dictionary
        {
            Name = IrregularVerbs.DictionaryName,
            WordsCount = IrregularVerbs.All.Count,
            OwnerId = null,
            IsPublic = true,
        };

        _dbContext.Dictionaries.Add(dictionary);

        // WordPair is global and unique by Word. A triplet that is somehow already there keeps
        // its translation unless it is empty — the same rule book import follows for its words.
        var words = IrregularVerbs.All.Select(v => v.Word).ToList();

        var existing = await _dbContext.Words
            .Where(w => words.Contains(w.Word))
            .ToDictionaryAsync(w => w.Word, StringComparer.Ordinal);

        // A seeded dictionary has no real frequencies, but batches are "most frequent first",
        // so the table position stands in for one: 68 for the first row down to 1 for the
        // last. That keeps the mini-families of the table together in a batch instead of
        // falling back to alphabetical order.
        var rank = IrregularVerbs.All
            .Select((verb, index) => (verb, count: IrregularVerbs.All.Count - index))
            .ToDictionary(x => x.verb, x => x.count);

        var groups = IrregularVerbs.All.GroupBy(v => v.Group).OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            var chapter = new Chapter
            {
                Order = group.Key - 1,
                Title = IrregularVerbs.GroupTitles[group.Key - 1],
                WordsCount = group.Count(),
            };

            foreach (var verb in group)
            {
                var count = rank[verb];

                if (!existing.TryGetValue(verb.Word, out var pair))
                {
                    pair = new WordPair { Word = verb.Word, Translation = verb.Translation };
                    existing[verb.Word] = pair;
                    _dbContext.Words.Add(pair);
                }
                else if (pair.Translation.Length == 0)
                {
                    pair.Translation = verb.Translation;
                }

                chapter.Words.Add(new ChapterWord { WordPair = pair, Count = count });
                _dbContext.DictionaryWords.Add(new DictionaryWord { Dictionary = dictionary, WordPair = pair, Frequency = count });
            }

            dictionary.Chapters.Add(chapter);
        }

        await _dbContext.SaveChangesAsync();
    }
}
