using System.Net;
using System.Text;

namespace LanguageLab.Tests.Fakes;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers from a delegate and records what was sent — the
/// shared form of the private StubHandler in DeepLTranslatorTests, for the LLM clients' tests and
/// the translators built on them. The delegate may throw to simulate a network failure.
/// </summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastBody { get; private set; }
    public int Calls { get; private set; }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        LastRequest = request;
        // Read here: the client disposes the request (and its content) once it returns.
        LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return _respond(request);
    }
}
