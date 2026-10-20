using System.Net;
using System.Text;
using System.Text.Json;

using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// Sends nothing to a sync service newer than this app, and answers in its
/// place.
/// <para>
/// The answer is a 426 ProblemDetails with <see cref="SyncErrorCodes.ClientOutdated"/>
/// and <see cref="SyncCompatibility.UpdateRequiredMessage"/>, the shape the
/// service gives every failure. So each caller reads it the way it already
/// reads one: <c>SyncHttp</c> turns it into the error a worker shows as its
/// <c>LastError</c>, the phone's client throws or returns it, and the token
/// provider ends up with no token, which leaves the next request to this
/// handler as well. No caller needs a branch of its own to stop syncing.
/// </para>
/// <para>
/// It sits after <see cref="SyncAuthenticationHandler"/> in each pipeline, so
/// the question it asks — <c>GET /</c> through the rest of the pipeline — carries
/// no token and never passes through itself. On the token client it is the only
/// handler; the provider's own request is held the same way.
/// </para>
/// </summary>
public sealed class SyncCompatibilityHandler(SyncCompatibility compatibility) : DelegatingHandler
{
    private const int UpgradeRequired = 426;

    private readonly SyncCompatibility _compatibility = compatibility ?? throw new ArgumentNullException(nameof(compatibility));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequestUri is { IsAbsoluteUri: true } address
            && await _compatibility
                .UpdateRequiredForAsync(address, (probe, token) => base.SendAsync(probe, token), cancellationToken)
                .ConfigureAwait(false))
        {
            return Outdated(request);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static HttpResponseMessage Outdated(HttpRequestMessage request)
    {
        var problem = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["type"] = $"https://backlog.jsdotnet.dev/problems/{SyncErrorCodes.ClientOutdated}",
            ["title"] = "Update required",
            ["status"] = UpgradeRequired,
            ["detail"] = SyncCompatibility.UpdateRequiredMessage,
            ["code"] = SyncErrorCodes.ClientOutdated
        });

        return new HttpResponseMessage((HttpStatusCode)UpgradeRequired)
        {
            ReasonPhrase = "Upgrade Required",
            RequestMessage = request,
            Content = new StringContent(problem, Encoding.UTF8, "application/problem+json")
        };
    }
}
