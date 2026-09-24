namespace LanguageLab.Application.Translation;

/// <summary>
/// The reader's sentence translator: DeepL when Translation:DeepLApiKey is set (better, 500 000
/// characters a month), MyMemory otherwise — so sentence translation always works.
/// </summary>
public sealed class FallbackSentenceTranslator : ISentenceTranslator
{
    private readonly DeepLTranslator _deepL;
    private readonly MyMemorySentenceTranslator _myMemory;

    public FallbackSentenceTranslator(DeepLTranslator deepL, MyMemorySentenceTranslator myMemory)
    {
        _deepL = deepL;
        _myMemory = myMemory;
    }

    public bool IsConfigured => true;

    public Task<SentenceTranslation> TranslateAsync(string text, CancellationToken cancellationToken) =>
        _deepL.IsConfigured ? _deepL.TranslateAsync(text, cancellationToken) : _myMemory.TranslateAsync(text, cancellationToken);
}
