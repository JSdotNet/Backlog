using System.Net;
using System.Text;

namespace Backlog.Infrastructure.Capture.UnitTests;

/// <summary>
/// The web, answered from a script keyed by URL. Every test here drives the
/// real adapter through a real <see cref="HttpClient"/>; only the wire is
/// faked, so what is asserted is the request that would actually have gone out
/// — which page was fetched, and in what order.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every URL that reached the wire, in order.</summary>
    public List<string> Requested { get; } = [];

    /// <summary>Every request that reached the wire, in order, with the
    /// headers it carried — snapshotted at send time, because the client owns
    /// the message afterwards.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>The value of one header on the request for a URL, or null
    /// when the request did not carry it.</summary>
    public string? HeaderSentTo(string url, string header) =>
        Requests.Single(request => string.Equals(request.Url, url, StringComparison.OrdinalIgnoreCase))
            .Headers.GetValueOrDefault(header);

    public StubHttpMessageHandler Map(string url, Func<HttpResponseMessage> respond)
    {
        _routes[url] = respond;
        return this;
    }

    public StubHttpMessageHandler Xml(string url, string body) =>
        Map(url, () => Body(HttpStatusCode.OK, body, "application/xml"));

    public StubHttpMessageHandler Html(string url, string body) =>
        Map(url, () => Body(HttpStatusCode.OK, body, "text/html"));

    public StubHttpMessageHandler Status(string url, HttpStatusCode status) =>
        Map(url, () => new HttpResponseMessage(status) { ReasonPhrase = status.ToString() });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;
        Requested.Add(url);
        Requests.Add(new RecordedRequest(
            url,
            request.Headers.ToDictionary(
                header => header.Key,
                header => string.Join("; ", header.Value),
                StringComparer.OrdinalIgnoreCase)));

        return Task.FromResult(_routes.TryGetValue(url, out var respond)
            ? respond()
            : new HttpResponseMessage(HttpStatusCode.NotFound) { ReasonPhrase = "Not Found" });
    }

    public static HttpResponseMessage Body(HttpStatusCode status, string body, string mediaType) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
}

/// <summary>One request as the wire saw it.</summary>
internal sealed record RecordedRequest(string Url, IReadOnlyDictionary<string, string> Headers);

/// <summary>Hands out clients over one scripted handler, whatever name is
/// asked for — the tests are about what the adapters do with an answer, not
/// about which client they asked. The timeout is the registered client's
/// unless a test is about the timeout itself.</summary>
internal sealed class StubHttpClientFactory(HttpMessageHandler handler, TimeSpan? timeout = null) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(handler, disposeHandler: false) { Timeout = timeout ?? TimeSpan.FromSeconds(15) };
}
