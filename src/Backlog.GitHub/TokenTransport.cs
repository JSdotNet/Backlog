using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Backlog.GitHub;

/// <summary>
/// Talks to the GitHub REST API directly with a personal access token — the
/// fallback for machines where the <c>gh</c> CLI isn't installed or signed in.
/// </summary>
public sealed class TokenTransport : IGitHubTransport
{
    private const string ApiRoot = "https://api.github.com/";

    private readonly HttpClient _http;
    private readonly Func<string?> _token;

    public TokenTransport(Func<string?> token, HttpClient? http = null)
    {
        _token = token;
        _http = http ?? new HttpClient();

        if (_http.BaseAddress is null) _http.BaseAddress = new Uri(ApiRoot);
        _http.DefaultRequestHeaders.UserAgent.TryParseAdd("Backlog");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    public string Description => "personal access token";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(_token()));

    public async Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        var token = _token();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new GitHubNotConfiguredException("No GitHub token is configured.");
        }

        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

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
