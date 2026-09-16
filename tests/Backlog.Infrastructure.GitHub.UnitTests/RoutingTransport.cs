using System.Text.Json;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// A transport that answers by path, records what it was asked, and can be told to
/// refuse.
/// </summary>
/// <remarks>
/// <para>
/// Matching on a path fragment rather than the whole query string, because the
/// clients under test build long queries and a test that restated them would be
/// asserting its own string-building rather than the client's behaviour. The
/// recorded paths are still available for the cases where the query <em>is</em> the
/// point.
/// </para>
/// <para>
/// A route may be a refusal. That is what makes the billing client's two-endpoint
/// fallback testable at all: the user endpoint has to be seen to be tried and
/// refused before the organization one is asked.
/// </para>
/// </remarks>
internal sealed class RoutingTransport : IGitHubTransport
{
    private readonly List<(HttpMethod? Method, string Fragment, Func<string> Answer)> _routes = [];

    public string Description => "stub";

    /// <summary>Every path asked for, in order.</summary>
    public List<string> Paths { get; } = [];

    /// <summary>Every body sent, serialised, in the order of <see cref="Paths"/>;
    /// null where a call sent none. For the cases where what was sent is the point.</summary>
    public List<string?> Bodies { get; } = [];

    /// <summary>The API version each call asked for, in order. Null means the caller
    /// left the transport's default alone.</summary>
    public List<string?> ApiVersions { get; } = [];

    public bool Available { get; init; } = true;

    /// <summary>Answers any path containing <paramref name="fragment"/> with this JSON.</summary>
    public RoutingTransport Returns(string fragment, string json)
    {
        _routes.Add((null, fragment, () => json));
        return this;
    }

    /// <summary>As <see cref="Returns(string, string)"/>, for one method only —
    /// the Contents API is read and written at the same path.</summary>
    public RoutingTransport Returns(HttpMethod method, string fragment, string json)
    {
        _routes.Add((method, fragment, () => json));
        return this;
    }

    /// <summary>Refuses any path containing <paramref name="fragment"/>, the way
    /// GitHub refuses an endpoint the credential cannot reach.</summary>
    public RoutingTransport Refuses(string fragment, string message = "GitHub refused the request.")
    {
        _routes.Add((null, fragment, () => throw new GitHubException(message)));
        return this;
    }

    /// <summary>Refuses one method at a path with a status, the way a transport
    /// reports a 404 for a file that is not there yet.</summary>
    public RoutingTransport Refuses(HttpMethod method, string fragment, System.Net.HttpStatusCode status, string message = "GitHub refused the request.")
    {
        _routes.Add((method, fragment, () => throw new GitHubException(message) { Status = status }));
        return this;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Available);

    public Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        Paths.Add(path);
        ApiVersions.Add(apiVersion);
        Bodies.Add(body is null ? null : JsonSerializer.Serialize(body));

        foreach (var (routeMethod, fragment, answer) in _routes)
        {
            if (routeMethod is not null && routeMethod != method) continue;
            if (!path.Contains(fragment, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                return Task.FromResult(Parse(answer()));
            }
            catch (GitHubException exception)
            {
                return Task.FromException<JsonElement>(exception);
            }
        }

        // An unrouted path answers with an empty array rather than throwing. A client
        // that walks pages should be able to reach the end of one it was not given.
        return Task.FromResult(Parse("[]"));
    }

    /// <summary>How many times a path containing this fragment was asked for.</summary>
    public int CallsTo(string fragment) =>
        Paths.Count(path => path.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
