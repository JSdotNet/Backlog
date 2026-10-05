using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Backlog.Infrastructure.SpecManager.OAuth;

namespace Backlog.Infrastructure.SpecManager.Api;

/// <summary>
/// The four REST reads the connector makes, and nothing else: the backlog, its
/// statuses, its labels and its members. Thin on purpose — what an answer means is
/// the connector's business.
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
    public async Task<IReadOnlyList<BacklogitemDto>> GetBacklogAsync(
        string product,
        bool archived,
        DateTimeOffset? changedSince,
        CancellationToken cancellationToken)
    {
        var query = "?gearchiveerd=" + (archived ? "true" : "false");
        if (changedSince is { } since)
        {
            query += "&gewijzigdSinds=" + Uri.EscapeDataString(since.ToString("O", CultureInfo.InvariantCulture));
        }

        var answer = await GetAsync<BacklogResponse>(ProductPath(product, "backlog") + query, cancellationToken).ConfigureAwait(false);
        return answer?.Items ?? [];
    }

    public async Task<IReadOnlyList<BacklogstatusDto>> GetStatusesAsync(string product, CancellationToken cancellationToken)
    {
        var answer = await GetAsync<StatusesResponse>(ProductPath(product, "backlogstatussen"), cancellationToken).ConfigureAwait(false);
        return answer?.Statussen ?? [];
    }

    public async Task<IReadOnlyList<BackloglabelDto>> GetLabelsAsync(string product, CancellationToken cancellationToken)
    {
        var answer = await GetAsync<LabelsResponse>(ProductPath(product, "backloglabels"), cancellationToken).ConfigureAwait(false);
        return answer?.Labels ?? [];
    }

    /// <summary>
    /// The product's members, or null when there is no answer to be had: nobody
    /// signed in, the account may not read them (401, 403), or the installation
    /// does not have the path yet (404).
    /// </summary>
    public async Task<IReadOnlyList<BacklogledDto>?> GetMembersAsync(string product, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(ProductPath(product, "backlogleden"), cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var answer = await response.Content.ReadFromJsonAsync<MembersResponse>(SpecManagerJson.Options, cancellationToken).ConfigureAwait(false);
            return answer?.Leden ?? [];
        }
        catch (SpecManagerSignInRequiredException)
        {
            return null;
        }
    }

    private async Task<T?> GetAsync<T>(string pathAndQuery, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(pathAndQuery, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(SpecManagerJson.Options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        var token = await _signIn.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new SpecManagerSignInRequiredException();

        var response = await SendOnceAsync(pathAndQuery, token, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        // The token was refused before its time — revoked, or the clock here is
        // off. One refresh, one retry; a second refusal is the answer.
        response.Dispose();
        var renewed = await _signIn.RefreshAfterRejectionAsync(token, cancellationToken).ConfigureAwait(false)
            ?? throw new SpecManagerSignInRequiredException();

        return await SendOnceAsync(pathAndQuery, renewed, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(string pathAndQuery, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_options.Root + pathAndQuery));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return await _http().SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static string ProductPath(string product, string resource) =>
        $"/api/producten/{Uri.EscapeDataString(product)}/{resource}";
}

/// <summary>
/// A spec-manager call with nobody signed in. Not an
/// <see cref="OperationCanceledException"/>, so the sync turns it into a fetch that
/// failed and writes nothing.
/// </summary>
internal sealed class SpecManagerSignInRequiredException()
    : InvalidOperationException("Not signed in to spec-manager. Sign in from Settings before this product can sync.");
