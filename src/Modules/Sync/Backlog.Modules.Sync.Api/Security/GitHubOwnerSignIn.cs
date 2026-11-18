using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Backlog.Modules.Sync.Api.Options;
using Backlog.Modules.Sync.Services;
using Microsoft.Extensions.Options;

namespace Backlog.Modules.Sync.Api.Security;

/// <summary>
/// The owner's GitHub sign-in over the OAuth App's web flow (local ADR 0052 §1): the
/// authorize address for the browser, then the code-for-token exchange with the client
/// secret and one <c>GET /user</c> for the numeric id. The token is used for that one
/// call and kept nowhere. No scope is asked for: the public profile carries the id.
/// </summary>
internal sealed partial class GitHubOwnerSignIn(
    HttpClient http,
    IOptions<GitHubOAuthOptions> options,
    ILogger<GitHubOwnerSignIn> log) : IGitHubOwnerSignIn
{
    public Uri AuthorizeAddress(string state)
    {
        var settings = options.Value;

        // No redirect_uri: GitHub then uses the callback the OAuth App is registered
        // with, so the address cannot be pointed anywhere else from here.
        var query = $"client_id={Uri.EscapeDataString(settings.ClientId ?? string.Empty)}"
            + $"&state={Uri.EscapeDataString(state)}&allow_signup=false";

        return new UriBuilder(settings.AuthorizeEndpoint) { Query = query }.Uri;
    }

    public async Task<long?> ResolveUserIdAsync(string authorizationCode, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        try
        {
            using var exchange = new HttpRequestMessage(HttpMethod.Post, settings.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = settings.ClientId ?? string.Empty,
                    ["client_secret"] = settings.ClientSecret ?? string.Empty,
                    ["code"] = authorizationCode,
                }),
            };
            exchange.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var tokenResponse = await http.SendAsync(exchange, cancellationToken);
            var token = tokenResponse.IsSuccessStatusCode
                ? await tokenResponse.Content.ReadFromJsonAsync<TokenAnswer>(cancellationToken)
                : null;

            if (string.IsNullOrEmpty(token?.AccessToken))
            {
                LogExchangeRefused(log, (int)tokenResponse.StatusCode, token?.Error ?? "no token");
                return null;
            }

            using var user = new HttpRequestMessage(HttpMethod.Get, new Uri(settings.ApiBaseAddress, "user"));
            user.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            user.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            user.Headers.UserAgent.Add(new ProductInfoHeaderValue("backlog-sync", "1"));

            using var userResponse = await http.SendAsync(user, cancellationToken);
            if (!userResponse.IsSuccessStatusCode)
            {
                LogUserRefused(log, (int)userResponse.StatusCode);
                return null;
            }

            return (await userResponse.Content.ReadFromJsonAsync<UserAnswer>(cancellationToken))?.Id;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
            && !cancellationToken.IsCancellationRequested)
        {
            LogUnreachable(log, ex);
            return null;
        }
    }

    private sealed record TokenAnswer(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("error")] string? Error);

    private sealed record UserAnswer([property: JsonPropertyName("id")] long? Id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GitHub refused the owner sign-in's code exchange ({Status}, {Error}).")]
    private static partial void LogExchangeRefused(ILogger logger, int status, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GitHub refused the signed-in user read ({Status}).")]
    private static partial void LogUserRefused(ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GitHub did not answer the owner sign-in.")]
    private static partial void LogUnreachable(ILogger logger, Exception exception);
}
