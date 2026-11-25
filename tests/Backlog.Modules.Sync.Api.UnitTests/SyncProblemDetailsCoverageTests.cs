using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Backlog.Modules.Sync.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// Every route the sync service maps, read from the service's own endpoint data source,
/// answers every failure as RFC 9457 Problem Details (org ADR 0004): a failure a handler's
/// <c>Result</c> carries, a refusal the edge makes, a 401 from the bearer handler, a 400 from a
/// body that does not bind. The table is the service's, not this file's, so a route added
/// without going through <c>SyncResults</c> fails here without anyone having to list it.
/// <para>
/// Each route is probed three ways: with no token, with a paired device's token and an empty
/// JSON object, and with that token and a body that is not JSON. A route parameter is a fresh
/// id, which nothing in the store holds. A probe may succeed — a pull with no cursor is a
/// success — but every route must fail at least one probe, or the coverage would be empty.
/// </para>
/// </summary>
public sealed partial class SyncProblemDetailsCoverageTests : IDisposable
{
    /// <summary>
    /// The routes outside <see cref="SyncRoutes.Base"/>, which answer in a contract of their
    /// own rather than in Problem Details, each recorded in arc42 §8 (errors):
    /// the OAuth authorization server answers RFC 6749 §5.2 error bodies and browser pages,
    /// the GitHub callback is a browser redirect, the remote MCP endpoint answers JSON-RPC
    /// and its RFC 9728 metadata, <c>/</c> is the anonymous service description, and the
    /// OpenAPI document is the contract itself.
    /// Pinned exactly, so a new root route has to be placed on one side or the other.
    /// </summary>
    private static readonly HashSet<string> OwnContractRoutes = new(StringComparer.Ordinal)
    {
        "GET /",
        "GET " + SyncRoutes.GitHubCallback,
        "GET " + SyncRoutes.AuthorizationServerMetadata,
        "GET " + SyncRoutes.ConnectorAuthorize,
        "GET " + SyncRoutes.ConnectorConsent,
        "POST " + SyncRoutes.ConnectorConsent,
        "POST " + SyncRoutes.ConnectorToken,
        "GET " + SyncRoutes.RemoteMcpResourceMetadata,
        "GET /openapi/{documentName}.json",
    };

    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Every_sync_route_answers_each_failure_as_problem_details()
    {
        var anonymous = _service.CreateClient();
        var paired = await _service.CreateClient().RegisteredDevice("Problem details probe");

        var routes = SyncRouteTable();
        Assert.NotEmpty(routes);

        var failures = new List<string>();

        foreach (var (method, template) in routes)
        {
            var path = Fill(template);
            var failedOnce = false;

            foreach (var (probe, client, body) in Probes(anonymous, paired))
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                if (method is "POST" or "PUT" && body is not null)
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }

                using var response = await client.SendAsync(request, Cancellation);

                if ((int)response.StatusCode < 400)
                {
                    continue;
                }

                failedOnce = true;

                // Every probe is a request the caller got wrong, so a 500 is the service
                // failing to say so: the body did not bind, or a missing field reached a
                // handler. Problem Details or not, that is a defect.
                if (response.StatusCode == HttpStatusCode.InternalServerError)
                {
                    failures.Add($"{method} {template} ({probe}) answered 500.");
                    continue;
                }

                if (await NotProblemDetails(response) is { } why)
                {
                    failures.Add($"{method} {template} ({probe}) answered {(int)response.StatusCode}: {why}");
                }
            }

            if (!failedOnce)
            {
                failures.Add($"{method} {template} failed none of the probes, so nothing here proves its failures.");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Every_route_outside_the_sync_base_is_one_with_a_contract_of_its_own()
    {
        var outside = AllRoutes()
            .Where(route => !route.Template.StartsWith(SyncRoutes.Base, StringComparison.Ordinal))
            .Select(route => $"{route.Method} {route.Template}")
            .ToHashSet(StringComparer.Ordinal);

        // The remote MCP endpoint maps with MapMcp, under every method it answers, and Scalar
        // maps its page and its assets (Development only, which is what the factory runs);
        // both are the libraries' own routes rather than this service's.
        outside.RemoveWhere(route => route.Split(' ')[1].TrimEnd('/') == SyncRoutes.RemoteMcp
            || route.StartsWith("GET /scalar", StringComparison.Ordinal));
        Assert.Contains(AllRoutes(), route => route.Template.StartsWith("/scalar", StringComparison.Ordinal));

        Assert.Equal(OwnContractRoutes.Order(StringComparer.Ordinal), outside.Order(StringComparer.Ordinal));
    }

    private static IEnumerable<(string Probe, HttpClient Client, string? Body)> Probes(HttpClient anonymous, HttpClient paired)
    {
        yield return ("no token", anonymous, null);
        yield return ("empty object", paired, "{}");
        yield return ("not JSON", paired, "{");
    }

    /// <summary>Why <paramref name="response"/> is not a problem body, or null when it is one:
    /// <c>application/problem+json</c> carrying type, title, the response's own status and a
    /// trace id — and, for a failure a handler's <c>Result</c> carried (it has a <c>code</c>),
    /// the detail that says what was wrong.</summary>
    private static async Task<string?> NotProblemDetails(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentType?.MediaType != "application/problem+json")
        {
            return $"content type {response.Content.Headers.ContentType?.MediaType ?? "(none)"}";
        }

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);

        return problem switch
        {
            null => "an empty body",
            { Type: null or "" } => "no type",
            { Title: null or "" } => "no title",
            { Status: var status } when status != (int)response.StatusCode => $"status {status} in the body",
            { TraceId: null or "" } => "no traceId",
            { Code: not null, Detail: null or "" } => "a coded problem with no detail",
            _ => null,
        };
    }

    private List<(string Method, string Template)> SyncRouteTable() =>
        [.. AllRoutes().Where(route => route.Template.StartsWith(SyncRoutes.Base + "/", StringComparison.Ordinal))];

    private IEnumerable<(string Method, string Template)> AllRoutes()
    {
        _ = _service.Server;

        return _service.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(method => (Method: method, Template: "/" + endpoint.RoutePattern.RawText!.TrimStart('/'))))
            .Distinct();
    }

    /// <summary>A template with every parameter filled by a fresh id.</summary>
    private static string Fill(string template) =>
        RouteParameter().Replace(template, _ => Guid.CreateVersion7().ToString("D"));

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();
}
