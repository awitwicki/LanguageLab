using System.Net;
using System.Text;
using LanguageLab.Application.Translation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LanguageLab.Tests;

public class FallbackSentenceTranslatorTests
{
    private sealed class CountingHandler(string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (FallbackSentenceTranslator Translator, CountingHandler DeepL, CountingHandler MyMemory) Build(string? deepLKey)
    {
        var deepLHandler = new CountingHandler("""{"translations":[{"text":"від DeepL"}]}""");
        var myMemoryHandler = new CountingHandler("""{"responseData":{"translatedText":"від MyMemory"},"responseStatus":200}""");
        var options = Options.Create(new TranslationOptions { DeepLApiKey = deepLKey });

        var deepL = new DeepLTranslator(
            new HttpClient(deepLHandler) { BaseAddress = new Uri(DeepLTranslator.BaseUrl) }, options, NullLogger<DeepLTranslator>.Instance);
        var myMemory = new MyMemorySentenceTranslator(
            new HttpClient(myMemoryHandler) { BaseAddress = new Uri(MyMemoryTranslator.BaseUrl) }, options,
            new MyMemorySentenceBudget(options),
            NullLogger<MyMemorySentenceTranslator>.Instance);

        return (new FallbackSentenceTranslator(deepL, myMemory), deepLHandler, myMemoryHandler);
    }

    [Fact]
    public async Task With_a_deepl_key_deepl_translates()
    {
        var (translator, deepL, myMemory) = Build("secret:fx");

        var result = await translator.TranslateAsync("Hello.", CancellationToken.None);

        Assert.Equal("від DeepL", result.Text);
        Assert.Equal((1, 0), (deepL.Calls, myMemory.Calls));
    }

    [Fact]
    public async Task Without_a_key_mymemory_translates()
    {
        var (translator, deepL, myMemory) = Build(null);

        var result = await translator.TranslateAsync("Hello.", CancellationToken.None);

        Assert.Equal("від MyMemory", result.Text);
        Assert.Equal((0, 1), (deepL.Calls, myMemory.Calls));
    }

    [Fact]
    public void Sentence_translation_is_always_available()
    {
        Assert.True(Build(null).Translator.IsConfigured);
    }
}
