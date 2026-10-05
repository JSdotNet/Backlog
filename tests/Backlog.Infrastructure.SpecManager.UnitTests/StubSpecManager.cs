using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// A stand-in for a spec-manager installation at <see cref="BaseUrl"/>: routes
/// answer by method and path, every request is recorded with its body and bearer,
/// and a path nobody routed answers 404 — what the server says for a path it does
/// not have yet.
/// </summary>
internal sealed class StubSpecManager : HttpMessageHandler
{
    public static readonly Uri BaseUrl = new("https://spec.test");

    public const string Product = "backlog-demo";

    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, string?, HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    /// <summary>A stub with the discovery, registration and token endpoints and the
    /// product's statuses and labels answered from the recorded fixtures.</summary>
    public static StubSpecManager WithRecordedResponses()
    {
        var stub = new StubSpecManager();
        stub.Json(HttpMethod.Get, "/.well-known/oauth-authorization-server", "oauth-metadata.json");
        stub.Json(HttpMethod.Post, "/oauth/registreren", "registration.json", HttpStatusCode.Created);
        stub.Json(HttpMethod.Post, "/oauth/token", "token.json");
        stub.Json(HttpMethod.Get, $"/api/producten/{Product}/backlogstatussen", "statuses.json");
        stub.Json(HttpMethod.Get, $"/api/producten/{Product}/backloglabels", "labels.json");
        return stub;
    }

    /// <summary>Answers <paramref name="path"/> with a fixture file.</summary>
    public void Json(HttpMethod method, string path, string fixture, HttpStatusCode status = HttpStatusCode.OK) =>
        Route(method, path, (_, _) => Respond(status, Fixtures.Read(fixture)));

    public void Route(HttpMethod method, string path, Func<HttpRequestMessage, string?, HttpResponseMessage> respond) =>
        _routes[Key(method, path)] = respond;

    public IReadOnlyList<RecordedRequest> To(string path) =>
        Requests.Where(request => request.Uri.AbsolutePath == path).ToList();

    public HttpClient CreateClient() => new(this, disposeHandler: false);

    public static HttpResponseMessage Respond(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            body,
            request.Headers.Authorization?.Scheme == "Bearer" ? request.Headers.Authorization.Parameter : null));

        return _routes.TryGetValue(Key(request.Method, request.RequestUri!.AbsolutePath), out var respond)
            ? respond(request, body)
            : Respond(HttpStatusCode.NotFound, "{}");
    }

    private static string Key(HttpMethod method, string path) => $"{method.Method} {path}";
}

/// <param name="Bearer">The access token the request carried, or null.</param>
internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, string? Bearer);

/// <summary>The recorded responses under <c>Fixtures/</c>, copied beside the test
/// assembly.</summary>
internal static class Fixtures
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
