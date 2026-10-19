using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Backlog.Infrastructure.SpecManager.OAuth;

namespace Backlog.Infrastructure.SpecManager.Api;

/// <summary>
/// The REST calls the connector makes, and nothing else: five reads — the products,
/// a product's backlog, its statuses, its labels and its members — and one write, an
/// item's status.
/// Thin on purpose — what an answer means is the connector's business.
/// <para>
/// Every call carries the bearer <see cref="SpecManagerSignIn"/> hands out. A 401 is
/// answered once by refreshing and sending again; a second 401 is the call's
/// failure. Nobody signed in is <see cref="SpecManagerSignInRequiredException"/>,
/// before anything is sent.
/// </para>
/// </summary>
internal sealed class SpecManagerClient
{
    /// <summary>The named client <see cref="SpecManagerHttpRegistration"/> puts the
    /// pipeline on.</summary>
    public const string HttpClientName = "spec-manager";

    private readonly Func<HttpClient> _http;
    private readonly SpecManagerSignIn _signIn;
    private readonly SpecManagerOptions _options;

    public SpecManagerClient(Func<HttpClient> http, SpecManagerSignIn signIn, SpecManagerOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _signIn = signIn;
        _options = options;
    }

    /// <summary>
    /// The product's backlog: the archive when <paramref name="archived"/>, the rest
    /// otherwise, and — with <paramref name="changedSince"/> — only what changed at
    /// or after it. The moment is sent round-trip with its offset, so the server
    /// never has to guess which zone it was meant in.
    /// </summary>
    public async Task<IReadOnlyList<BacklogItemDto>> GetBacklogAsync(
        string product,
        bool archived,
        DateTimeOffset? changedSince,
        CancellationToken cancellationToken)
    {
        var query = "?archived=" + (archived ? "true" : "false");
        if (changedSince is { } since)
        {
            query += "&changedSince=" + Uri.EscapeDataString(since.ToString("O", CultureInfo.InvariantCulture));
        }

        var answer = await GetAsync<BacklogResponse>(ProductPath(product, "backlog") + query, cancellationToken).ConfigureAwait(false);
        return answer?.Items ?? [];
    }

    /// <summary>
    /// The products the signed-in account is a member of — <c>GET /api/products</c>.
    /// With the app's agent token spec-manager answers only the products in which
    /// MCP is on, so a product offered here is one whose backlog will answer too.
    /// Read like every other path, a 401 refreshed once and asked again. A refusal
    /// throws <see cref="HttpRequestException"/> with its status; nobody signed in
    /// throws <see cref="SpecManagerSignInRequiredException"/>.
    /// </summary>
    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync(CancellationToken cancellationToken) =>
        await GetAsync<IReadOnlyList<ProductDto>>("/api/products", cancellationToken).ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<BacklogStatusDto>> GetStatusesAsync(string product, CancellationToken cancellationToken)
    {
        var answer = await GetAsync<StatusesResponse>(ProductPath(product, "backlog-statuses"), cancellationToken).ConfigureAwait(false);
        return answer?.Statuses ?? [];
    }

    public async Task<IReadOnlyList<BacklogLabelDto>> GetLabelsAsync(string product, CancellationToken cancellationToken)
    {
        var answer = await GetAsync<LabelsResponse>(ProductPath(product, "backlog-labels"), cancellationToken).ConfigureAwait(false);
        return answer?.Labels ?? [];
    }

    /// <summary>
    /// The product's members, or null when there is no answer to be had: nobody
    /// signed in, the account may not read them (401, 403), or the installation
    /// does not have the path yet (404).
    /// </summary>
    public async Task<IReadOnlyList<BacklogMemberDto>?> GetMembersAsync(string product, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(ProductPath(product, "backlog-members"), cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var answer = await response.Content.ReadFromJsonAsync<MembersResponse>(SpecManagerJson.Options, cancellationToken).ConfigureAwait(false);
            return answer?.Members ?? [];
        }
        catch (SpecManagerSignInRequiredException)
        {
            return null;
        }
    }

    /// <summary>
    /// Moves <paramref name="itemId"/> to <paramref name="statusId"/> — what finishing
    /// an item at its source is on spec-manager. Any answer but a success throws
    /// <see cref="SpecManagerRefusedException"/> with the server's own reason; a
    /// product whose agent switch is off answers an agent's token with a 403.
    /// </summary>
    public async Task SetStatusAsync(string product, string itemId, string statusId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(statusId);

        using var response = await SendAsync(
            HttpMethod.Put,
            ProductPath(product, $"backlog/{Uri.EscapeDataString(itemId)}/status"),
            new SetStatusRequest(statusId),
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new SpecManagerRefusedException(response.StatusCode, await ProblemOfAsync(response, cancellationToken).ConfigureAwait(false));
        }
    }

    private async Task<T?> GetAsync<T>(string pathAndQuery, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(pathAndQuery, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(SpecManagerJson.Options, cancellationToken).ConfigureAwait(false);
    }

    private Task<HttpResponseMessage> SendAsync(string pathAndQuery, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, pathAndQuery, body: null, cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string pathAndQuery, object? body, CancellationToken cancellationToken)
    {
        var token = await _signIn.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new SpecManagerSignInRequiredException();

        var response = await SendOnceAsync(method, pathAndQuery, body, token, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        // The token was refused before its time — revoked, or the clock here is
        // off. One refresh, one retry; a second refusal is the answer.
        response.Dispose();
        var renewed = await _signIn.RefreshAfterRejectionAsync(token, cancellationToken).ConfigureAwait(false)
            ?? throw new SpecManagerSignInRequiredException();

        return await SendOnceAsync(method, pathAndQuery, body, renewed, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One request. The body is serialised afresh each time, because a
    /// retry is a new request and a sent one's content is spent.</summary>
    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string pathAndQuery, object? body, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_options.Root + pathAndQuery));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: SpecManagerJson.Options);

        return await _http().SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What a refusal says: spec-manager's own <c>message</c>, else a problem's
    /// <c>detail</c>, else its <c>title</c>, else nothing — and the caller then says
    /// the status alone. A body that is neither shape is no reason.
    /// </summary>
    private static async Task<string?> ProblemOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(SpecManagerJson.Options, cancellationToken).ConfigureAwait(false);
            return !string.IsNullOrWhiteSpace(problem?.Message) ? problem.Message.Trim()
                : !string.IsNullOrWhiteSpace(problem?.Detail) ? problem.Detail.Trim()
                : !string.IsNullOrWhiteSpace(problem?.Title) ? problem.Title.Trim()
                : null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static string ProductPath(string product, string resource) =>
        $"/api/products/{Uri.EscapeDataString(product)}/{resource}";
}

/// <summary>
/// A spec-manager call with nobody signed in. Not an
/// <see cref="OperationCanceledException"/>, so the sync turns it into a fetch that
/// failed and writes nothing.
/// </summary>
internal sealed class SpecManagerSignInRequiredException()
    : InvalidOperationException("Not signed in to spec-manager. Sign in from Settings before this product can sync.");

/// <summary>
/// A write spec-manager answered with anything but a success. Carries the status and
/// the server's own reason, when it gave one, so the connector can put both in front
/// of the person.
/// </summary>
internal sealed class SpecManagerRefusedException(HttpStatusCode status, string? reason)
    : InvalidOperationException(reason is null ? $"spec-manager answered {(int)status}." : $"spec-manager answered {(int)status}: {reason}")
{
    public HttpStatusCode Status { get; } = status;

    /// <summary>spec-manager's <c>message</c>, or a problem's <c>detail</c> or
    /// <c>title</c>, or null when it gave none of them.</summary>
    public string? Reason { get; } = reason;
}
