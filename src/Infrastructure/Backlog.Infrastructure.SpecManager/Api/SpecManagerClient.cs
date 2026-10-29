using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Backlog.Infrastructure.SpecManager.OAuth;

namespace Backlog.Infrastructure.SpecManager.Api;

/// <summary>
/// The REST calls the connector and the question source make, and nothing else: the
/// reads — the products, a product's backlog, its statuses, its labels, its members and
/// its questions, and the person's unread notifications — the writes — an item's status,
/// a question's answer and a notification marked read — and the notification stream that
/// says when to read the questions and notifications again.
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

    /// <summary>The named client the notification stream is held open on: no retry and
    /// no timeout, because a stream meant to stay open for a quarter of an hour would be
    /// cut by either.</summary>
    public const string StreamHttpClientName = "spec-manager-stream";

    private readonly Func<HttpClient> _http;
    private readonly Func<HttpClient> _streamHttp;
    private readonly SpecManagerSignIn _signIn;
    private readonly SpecManagerOptions _options;

    /// <param name="streamHttp">The client the notification stream is read on; the
    /// pipeline's client when null, which is what a test hands in.</param>
    public SpecManagerClient(Func<HttpClient> http, SpecManagerSignIn signIn, SpecManagerOptions options, Func<HttpClient>? streamHttp = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _streamHttp = streamHttp ?? http;
        _signIn = signIn;
        _options = options;
    }

    /// <summary>The installation's root, as every path is appended to.</summary>
    public string Root => _options.Root;

    /// <summary>
    /// The product's questions in <paramref name="state"/> — <c>open</c> or
    /// <c>answered</c> — and, with <paramref name="changedSince"/>, only those asked,
    /// answered, reassigned or edited at or after it. Read like every other path: a
    /// 401 refreshed once and asked again, a refusal thrown as
    /// <see cref="HttpRequestException"/> with its status, nobody signed in as
    /// <see cref="SpecManagerSignInRequiredException"/>.
    /// </summary>
    public async Task<IReadOnlyList<BacklogQuestionDto>> GetQuestionsAsync(
        string product,
        string state,
        DateTimeOffset? changedSince,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        var query = "?state=" + Uri.EscapeDataString(state);
        if (changedSince is { } since)
        {
            query += "&changedSince=" + Uri.EscapeDataString(since.ToString("O", CultureInfo.InvariantCulture));
        }

        return await GetAsync<IReadOnlyList<BacklogQuestionDto>>(ProductPath(product, "backlog-questions") + query, cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>
    /// Answers <paramref name="questionId"/>: one option by its position, or "Other"
    /// with its own text, and a note; answers the question as it then stands. A refusal
    /// — what spec-manager says no to and would say no to again — throws
    /// <see cref="SpecManagerRefusedException"/> with the server's own reason: a 403 while
    /// the product does not allow answering from a connected app (or keeps agents out), a
    /// 400 when someone else answered first, a 404 when the question is gone, a 409 when
    /// it changed under the answer. A token refused again after the refresh is
    /// <see cref="SpecManagerSignInRequiredException"/>, and any other failure — a server
    /// error, a throttle the pipeline gave up on — an <see cref="HttpRequestException"/>:
    /// neither is an answer about the question, so the caller keeps the answer to send
    /// again. The same answer sent again by the same person is a correction to the same
    /// thing, which spec-manager accepts.
    /// </summary>
    public async Task<BacklogQuestionDto?> AnswerQuestionAsync(
        string product,
        string questionId,
        BacklogAnswerRequest answer,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(questionId);
        ArgumentNullException.ThrowIfNull(answer);

        using var response = await SendAsync(
            HttpMethod.Put,
            ProductPath(product, $"backlog-questions/{Uri.EscapeDataString(questionId)}/answer"),
            answer,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            throw new SpecManagerRefusedException(response.StatusCode, await ProblemOfAsync(response, cancellationToken).ConfigureAwait(false));
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new SpecManagerSignInRequiredException();

        response.EnsureSuccessStatusCode();

        // The answer is recorded once the status says so; a body this build cannot read
        // does not make it unrecorded, and sending it again would only correct it to itself.
        try
        {
            return await response.Content.ReadFromJsonAsync<BacklogQuestionDto>(SpecManagerJson.Options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens <c>GET /api/notifications/stream</c>, spec-manager's Server-Sent Events
    /// stream that says when the person's notification centre changed, and answers the
    /// response with its headers read and its body not: the caller reads the events off
    /// it and disposes it. A 401 is refreshed once like every other path; any other
    /// refusal throws <see cref="HttpRequestException"/> with its status.
    /// </summary>
    public async Task<HttpResponseMessage> OpenNotificationStreamAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            HttpMethod.Get,
            "/api/notifications/stream",
            body: null,
            cancellationToken,
            HttpCompletionOption.ResponseHeadersRead,
            "text/event-stream",
            _streamHttp).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"spec-manager answered {(int)status} to the notification stream.", null, status);
        }

        return response;
    }

    /// <summary>
    /// The person's unread notifications, newest first —
    /// <c>GET /api/notifications?unread=true&amp;count=N</c>, <paramref name="count"/> clamped
    /// to 1..200 by the server — with how many unread there are in all. Read like every other
    /// path: a 401 refreshed once and asked again, a refusal thrown as
    /// <see cref="HttpRequestException"/> with its status, nobody signed in as
    /// <see cref="SpecManagerSignInRequiredException"/>.
    /// </summary>
    public async Task<NotificationsResponse?> GetUnreadNotificationsAsync(int count, CancellationToken cancellationToken) =>
        await GetAsync<NotificationsResponse>(
            "/api/notifications?unread=true&count=" + count.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Marks <paramref name="notificationId"/> read in the person's notification centre —
    /// <c>POST /api/notifications/{id}/read</c>. A 204 is done, and so is a 404: spec-manager
    /// answers it for an id it no longer has, or that is not the person's, and sending it
    /// again would only be answered the same. A token refused again after the refresh is
    /// <see cref="SpecManagerSignInRequiredException"/>; any other failure an
    /// <see cref="HttpRequestException"/> with its status, and the read is sent again later.
    /// </summary>
    public async Task MarkNotificationReadAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"/api/notifications/{notificationId:D}/read",
            body: null,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound) return;
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new SpecManagerSignInRequiredException();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"spec-manager answered {(int)response.StatusCode} to marking a notification read.",
                null,
                response.StatusCode);
        }
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

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string pathAndQuery,
        object? body,
        CancellationToken cancellationToken,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead,
        string accept = "application/json",
        Func<HttpClient>? http = null)
    {
        var token = await _signIn.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new SpecManagerSignInRequiredException();

        var client = http ?? _http;
        var response = await SendOnceAsync(method, pathAndQuery, body, token, completion, accept, client, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        // The token was refused before its time — revoked, or the clock here is
        // off. One refresh, one retry; a second refusal is the answer.
        response.Dispose();
        var renewed = await _signIn.RefreshAfterRejectionAsync(token, cancellationToken).ConfigureAwait(false)
            ?? throw new SpecManagerSignInRequiredException();

        return await SendOnceAsync(method, pathAndQuery, body, renewed, completion, accept, client, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One request. The body is serialised afresh each time, because a
    /// retry is a new request and a sent one's content is spent.</summary>
    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method,
        string pathAndQuery,
        object? body,
        string token,
        HttpCompletionOption completion,
        string accept,
        Func<HttpClient> http,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_options.Root + pathAndQuery));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: SpecManagerJson.Options);

        return await http().SendAsync(request, completion, cancellationToken).ConfigureAwait(false);
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
