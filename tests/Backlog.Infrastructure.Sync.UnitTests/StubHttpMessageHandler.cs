using System.Net;
using System.Text;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// A sync service that answers from a script. Every test here drives the real
/// client through a real <see cref="HttpClient"/>; only the wire is faked, so
/// what is asserted is the request that would actually have gone out.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _respond;
    private readonly Func<Task>? _before;

    /// <param name="respond">The answer, given the request and how many came
    /// before it.</param>
    /// <param name="before">Held open until the test lets go, for the one test
    /// about several callers arriving while a request is still in flight. It has
    /// to be awaited rather than blocked on: a handler that blocks its calling
    /// thread never lets the second caller start.</param>
    public StubHttpMessageHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage> respond,
        Func<Task>? before = null)
    {
        _respond = respond;
        _before = before;
    }

    /// <summary>Every request that reached the wire, in order, with its
    /// Authorization header and path already captured — the message itself is
    /// disposed by the client before a test can read it.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var index = Requests.Count;

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri?.AbsolutePath ?? string.Empty,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter));

        if (_before is not null) await _before().ConfigureAwait(false);

        return _respond(request, index);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
        new(status)
        {
            Content = new StringContent(
                $$"""
                {"type":"https://backlog.jsdotnet.dev/problems/{{code}}","title":"Pairing failed","status":{{(int)status}},"detail":"{{detail}}","code":"{{code}}"}
                """,
                Encoding.UTF8,
                "application/problem+json")
        };

    internal sealed record RecordedRequest(HttpMethod Method, string Path, string? AuthorizationScheme, string? AuthorizationParameter);
}
