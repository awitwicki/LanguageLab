using LanguageLab.Api.Endpoints;
using LanguageLab.Domain.Languages;
using LanguageLab.Tests.Fakes;

namespace LanguageLab.Tests;

public class ReaderEndpointsTests
{
    private static readonly LearnerLanguage Uk = LearnerLanguages.Default;

    [Fact]
    public async Task A_linked_dictionary_is_enqueued()
    {
        var queue = new FakeTranslationQueue();

        await ReaderEndpoints.MaybeEnqueueTranslationAsync(queue, 42, Uk, CancellationToken.None);

        Assert.Equal(new[] { (42L, "uk") }, queue.Enqueued);
    }

    [Fact]
    public async Task No_linked_dictionary_enqueues_nothing()
    {
        var queue = new FakeTranslationQueue();

        await ReaderEndpoints.MaybeEnqueueTranslationAsync(queue, null, Uk, CancellationToken.None);

        Assert.Empty(queue.Enqueued);
    }

    /// <summary>
    /// Final review, finding 4: RegisterAsync already saved the book by the time this runs, so a
    /// missing language must be a graceful no-op here, not a throw the caller could turn into a
    /// 409 for a registration that already succeeded.
    /// </summary>
    [Fact]
    public async Task No_language_set_enqueues_nothing()
    {
        var queue = new FakeTranslationQueue();

        await ReaderEndpoints.MaybeEnqueueTranslationAsync(queue, 42, null, CancellationToken.None);

        Assert.Empty(queue.Enqueued);
    }
}
