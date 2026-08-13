using System.Net;
using System.Text.Json;

namespace Backlog.Infrastructure.Claude;

/// <summary>
/// Talks to the Claude Admin API with an Admin API key. There is no CLI
/// alternative the way GitHub has <c>gh</c>, so this is the only transport —
/// the interface exists so tests and future auth flows have somewhere to go.
/// </summary>
public sealed class ClaudeAdminTransport : IClaudeTransport
{
    private readonly HttpClient _http;
    private readonly ClaudeSettingsStore _settings;

    public ClaudeAdminTransport(HttpClient http, ClaudeSettingsStore settings)
    {
        _settings = settings;
        _http = http;

        _http.DefaultRequestHeaders.UserAgent.TryParseAdd("Backlog");
    }

    public string Description => "Anthropic Admin API key";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_settings.Current.IsConfigured);

    public async Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken = default)
    {
        var settings = _settings.Current;

        if (!settings.IsConfigured)
        {
            throw new ClaudeNotConfiguredException(
                "No Anthropic Admin API key is configured. Usage and cost reports come from the "
                + "Claude Admin API, which needs an organization in the Claude Console — Anthropic "
                + "does not offer it to individual accounts.");
        }

        if (!settings.LooksLikeAdminKey)
        {
            throw new ClaudeNotConfiguredException(
                "That looks like a regular Claude API key. Usage reports need an Admin API key "
                + "(it starts with 'sk-ant-admin'), which an organization owner creates in the "
                + "Claude Console.");
        }

        using var request = new HttpRequestMessage(method, EndpointUri(settings.ApiEndpoint, path));
        request.Headers.TryAddWithoutValidation("x-api-key", settings.AdminApiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", settings.ApiVersion);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ClaudeException($"Couldn't reach Anthropic: {ex.Message}", ex);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new ClaudeException(Describe(response.StatusCode, payload));
            }

            if (payload.Length == 0) return JsonDocument.Parse("null").RootElement.Clone();

            try
            {
                return JsonDocument.Parse(payload).RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new ClaudeException("Anthropic returned something that wasn't JSON.", ex);
            }
        }
    }


    internal static Uri EndpointUri(string endpoint, string path)
    {
        var trimmed = endpoint.Trim();
        if (trimmed.Length == 0)
        {
            throw new ClaudeNotConfiguredException("Set the Claude organization API endpoint before reading usage.");
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var baseUri))
        {
            throw new ClaudeNotConfiguredException("The Claude organization API endpoint must be an absolute URL.");
        }

        var baseText = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUri.AbsoluteUri
            : baseUri.AbsoluteUri + "/";

        return new Uri(baseText + path.TrimStart('/'));
    }

    internal static string Describe(HttpStatusCode status, string payload)
    {
        var detail = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message))
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
            HttpStatusCode.Unauthorized => "Anthropic rejected the admin key — check it hasn't been revoked.",
            HttpStatusCode.Forbidden =>
                "Anthropic refused the request. Usage reports are an organization feature; an "
                + "individual account cannot read them even with a valid key.",
            HttpStatusCode.NotFound => "Anthropic couldn't find that report — the API version may have moved on.",
            HttpStatusCode.TooManyRequests => "Anthropic is rate-limiting the usage report — try again shortly.",
            _ => detail.Length > 0 ? detail : $"Anthropic answered {(int)status}."
        };
    }
}
