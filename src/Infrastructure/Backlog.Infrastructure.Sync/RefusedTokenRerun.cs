using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The one rule the three sync loops share about a refused token: an exchange
/// that failed while a token was refused during it runs once more, at once.
/// <para>
/// A token refused during the exchange has already been dropped by
/// <see cref="SyncAuthenticationHandler"/>, so the next attempt starts from the
/// credential - which is how the first cycle after the service restarted with a
/// new signing key ends in a 401 the device has already recovered from. That one
/// is run again rather than shown.
/// </para>
/// <para>
/// Once only, and never for the credential itself (inherited ADR 0015): the
/// handler sends each request once, a second failure is the answer, and a
/// credential the token endpoint has refused is not asked about again here.
/// </para>
/// <para>
/// Only the decision is shared. The loops themselves stay copies of each other,
/// for the reasons <see cref="Sessions.SessionSyncWorker"/> records.
/// </para>
/// </summary>
internal static class RefusedTokenRerun
{
    /// <param name="services">Where the loop resolves its session; a host that
    /// registered no <see cref="SyncTokenProvider"/> has no token to refuse, and
    /// the exchange runs exactly once.</param>
    /// <param name="exchange">One whole exchange, run once or twice.</param>
    /// <param name="log">The loop's own log.</param>
    /// <param name="loop">The loop's name as its log lines use it - "task",
    /// "session", "annotation".</param>
    public static async Task<Result<T>> RunAsync<T>(
        IServiceProvider services,
        Func<Task<Result<T>>> exchange,
        ILogger log,
        string loop)
    {
        var tokens = services.GetService<SyncTokenProvider>();
        var refusedBefore = tokens?.TokensRefused;

        var result = await exchange().ConfigureAwait(false);

        if (result.IsFailure
            && tokens is not null
            && tokens.TokensRefused != refusedBefore
            && !tokens.CredentialRejected)
        {
            log.LogInformation(
                "A {Loop} sync cycle met a refused token; running it once more with a fresh one: {Code}", loop, result.Error.Code);
            result = await exchange().ConfigureAwait(false);
        }

        return result;
    }
}
