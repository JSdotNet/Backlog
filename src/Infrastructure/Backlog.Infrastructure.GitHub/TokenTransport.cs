using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Polly.Timeout;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// Talks to the GitHub REST API directly with a token.
/// <para>
/// Two jobs now, not one. It is still the fallback for machines where the
/// <c>gh</c> CLI isn't installed or signed in — and it is also the only way a call
/// can go out as an identity other than the one <c>gh</c> is switched to, because
/// <c>gh api</c> has no per-call account selector. So every bound repository comes
/// through here, whether or not the CLI is available and whether or not the
/// credential originally came from the CLI.
/// </para>
/// <para>
/// Which credential a path leaves with is not this type's decision. It asks
/// <see cref="IGitHubCredentialResolver"/>, per call, which is what lets a token
/// configured after startup take effect and what stops one repository's credential
/// ever being borrowed for another.
/// </para>
/// </summary>
public sealed class TokenTransport : IGitHubTransport
{
    /// <summary>The name <see cref="GitHubHttpRegistration.AddGitHubHttpClient"/>
    /// registers this transport's client under, so the client comes from
    /// <c>IHttpClientFactory</c> with the pipeline sized for GitHub's calls.</summary>
    public const string HttpClientName = "GitHub";

    /// <summary>The client a transport built with neither a client nor a factory
    /// sends over: one per process, shared, and never given default headers, so
    /// nothing here mutates a client another caller holds.</summary>
    private static readonly Lazy<HttpClient> Fallback = new(() => new HttpClient());

    private readonly Func<HttpClient> _http;
    private readonly IGitHubCredentialResolver _credentials;
    private readonly Func<string?> _apiEndpoint;

    /// <param name="http">A fixed client, for a caller that owns one. A host
    /// passes <paramref name="httpClients"/> instead.</param>
    /// <param name="httpClients">Where a client is asked for on every send, by
    /// <see cref="HttpClientName"/>, so the factory's handler rotation reaches
    /// this transport however long it lives. Wins over <paramref name="http"/>.</param>
    public TokenTransport(
        IGitHubCredentialResolver credentials,
        Func<string?>? apiEndpoint = null,
        HttpClient? http = null,
        IHttpClientFactory? httpClients = null)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        _credentials = credentials;
        _apiEndpoint = apiEndpoint ?? (() => GitHubSettings.DefaultApiEndpoint);
        _http = httpClients is not null
            ? () => httpClients.CreateClient(HttpClientName)
            : http is not null
                ? () => http
                : () => Fallback.Value;

        // The User-Agent, the Accept header and the API version all travel on the
        // request, not as a client default. The version is per request because the
        // billing usage reports live on another one; the other two because the
        // client is handed in, and a transport has no business changing the
        // defaults of a client it does not own.
    }

    public string Description => "personal access token";

    /// <summary>The resolver this transport asks, so the transport that composes it
    /// can route on the same answer rather than on a second one of its own.</summary>
    internal IGitHubCredentialResolver Credentials => _credentials;

    /// <summary>
    /// Whether this machine holds a token at all — not whether any particular path
    /// resolves to one.
    /// <para>
    /// It used to ask the token lookup with no path, and the only answer that could
    /// reach was the cross-repository fallback. So deleting the fallback would have
    /// left this transport permanently unavailable, and a machine with no <c>gh</c>
    /// but a working repository token would have been told it could not reach GitHub
    /// at all. The two questions were one question; they are two now.
    /// </para>
    /// </summary>
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_credentials.HasAnyCredential);

    public async Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        // Throws, naming the account, when the path is bound to one this machine
        // cannot satisfy. Never falls through to another identity.
        var credential = await _credentials.ResolveAsync(path, cancellationToken);
        if (credential is null)
        {
            throw new GitHubNotConfiguredException("No GitHub token is configured.");
        }

        return await SendAsAsync(credential, method, path, body, apiVersion, cancellationToken);
    }

    /// <summary>
    /// Sends one call with a credential already in hand, rather than one the path
    /// resolves to.
    /// <para>
    /// Internal, and the one caller is the account check: it resolves the credential
    /// for a path that names the account, then asks <c>GET user</c> with it - a path
    /// that names nobody, and would otherwise leave as this machine's default
    /// identity, which is exactly the thing the check is meant to see past.
    /// </para>
    /// </summary>
    internal async Task<JsonElement> SendAsAsync(
        GitHubCredential credential,
        HttpMethod method,
        string path,
        object? body = null,
        string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        using var request = new HttpRequestMessage(method, EndpointUri(path, credential.ApiEndpoint));
        request.Headers.UserAgent.ParseAdd("Backlog");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token.Trim());
        request.Headers.TryAddWithoutValidation(
            "X-GitHub-Api-Version",
            string.IsNullOrWhiteSpace(apiVersion) ? IGitHubTransport.DefaultApiVersion : apiVersion.Trim());

        if (GitHubGraphQl.IsGraphQl(path))
        {
            // mergeStateStatus began behind the merge-info schema preview, and an
            // Enterprise Server that still gates it answers the field as absent
            // without the preview media type. Set on the request, which replaces
            // the client's default Accept rather than adding to it — so the
            // ordinary media type is named again beside it.
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GitHubGraphQl.MergeInfoPreview));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GitHubGraphQl.GitHubJsonMediaType));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: GitHubJson.Options);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http().SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new GitHubException($"Couldn't reach GitHub: {ex.Message}", ex);
        }
        catch (TimeoutRejectedException ex)
        {
            // The factory client's pipeline gave up: an attempt or the whole call
            // ran past its budget. That is GitHub not answering, which callers
            // already handle as a GitHubException, not a Polly type they never see.
            throw new GitHubException("GitHub didn't answer in time.", ex);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new GitHubException(Describe(response.StatusCode, payload)) { Status = response.StatusCode };
            }

            if (payload.Length == 0) return JsonDocument.Parse("null").RootElement.Clone();

            try
            {
                return JsonDocument.Parse(payload).RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new GitHubException("GitHub returned something that wasn't JSON.", ex);
            }
        }
    }


    /// <summary>
    /// The address one path is requested at.
    /// <para>
    /// GraphQL is the exception to joining the path onto the endpoint. github.com
    /// keeps REST and GraphQL side by side at its API root, so the join is right
    /// there; GitHub Enterprise Server keeps REST under <c>/api/v3</c> and GraphQL
    /// beside it at <c>/api/graphql</c>, where the join would produce
    /// <c>/api/v3/graphql</c> — an address that does not exist. The routing hint a
    /// GraphQL path carries is dropped here too; it names a repository for the
    /// credential resolver and is not part of the request.
    /// </para>
    /// </summary>
    /// <param name="apiEndpoint">An endpoint the resolved credential named, which
    /// wins over the install-wide one. That is how an account on a GitHub Enterprise
    /// Server host reaches its own API without the whole install moving there.</param>
    internal Uri EndpointUri(string path, string? apiEndpoint = null)
    {
        var raw = string.IsNullOrWhiteSpace(apiEndpoint) ? _apiEndpoint() : apiEndpoint;
        var endpoint = string.IsNullOrWhiteSpace(raw)
            ? GitHubSettings.DefaultApiEndpoint
            : raw.Trim().TrimEnd('/');

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri))
        {
            throw new GitHubNotConfiguredException("The GitHub organization API endpoint must be an absolute URL.");
        }

        var baseText = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUri.AbsoluteUri
            : baseUri.AbsoluteUri + "/";

        if (GitHubGraphQl.IsGraphQl(path))
        {
            // "…/api/v3" becomes "…/api/graphql"; anything else takes the plain join.
            var basePath = baseUri.AbsolutePath.TrimEnd('/');

            return basePath.EndsWith("/api/v3", StringComparison.OrdinalIgnoreCase)
                ? new Uri(baseUri, basePath[..^"v3".Length] + GitHubGraphQl.Resource)
                : new Uri(baseText + GitHubGraphQl.Resource);
        }

        return new Uri(baseText + path.TrimStart('/'));
    }

    private static string Describe(HttpStatusCode status, string payload)
    {
        var detail = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("message", out var message))
            {
                detail = message.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error bodies happen; the status code is enough.
        }

        return status switch
        {
            HttpStatusCode.Unauthorized => "GitHub rejected the token — check it hasn't expired.",
            HttpStatusCode.Forbidden => detail.Length > 0 ? detail : "GitHub refused the request — the token may lack repo scope.",
            HttpStatusCode.NotFound => "GitHub couldn't find that repository — check the owner/repo and that the token can see it.",
            _ => detail.Length > 0 ? detail : $"GitHub answered {(int)status}."
        };
    }
}
