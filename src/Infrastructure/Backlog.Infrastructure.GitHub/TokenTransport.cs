using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

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
    private readonly HttpClient _http;
    private readonly IGitHubCredentialResolver _credentials;
    private readonly Func<string?> _apiEndpoint;

    public TokenTransport(
        IGitHubCredentialResolver credentials,
        Func<string?>? apiEndpoint = null,
        HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        _credentials = credentials;
        _apiEndpoint = apiEndpoint ?? (() => GitHubSettings.DefaultApiEndpoint);
        _http = http ?? new HttpClient();

        _http.DefaultRequestHeaders.UserAgent.TryParseAdd("Backlog");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        // The API version is deliberately *not* a default header any more. It used
        // to be, which worked while every endpoint this app called lived on one
        // version; the billing usage reports do not, so the version travels per
        // request. A default here would win or lose against the per-request one
        // depending on header-collection semantics rather than on intent.
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

        using var request = new HttpRequestMessage(method, EndpointUri(path, credential.ApiEndpoint));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token.Trim());
        request.Headers.TryAddWithoutValidation(
            "X-GitHub-Api-Version",
            string.IsNullOrWhiteSpace(apiVersion) ? IGitHubTransport.DefaultApiVersion : apiVersion.Trim());

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: GitHubJson.Options);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new GitHubException($"Couldn't reach GitHub: {ex.Message}", ex);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new GitHubException(Describe(response.StatusCode, payload));
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
