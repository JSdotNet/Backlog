using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// The token route's <see cref="HttpClient"/> registration, in one place both
/// hosts call so the pipeline on it cannot drift between the desktop app and the
/// web harness.
/// <para>
/// The host's <c>AddServiceDefaults()</c> puts the standard resilience pipeline
/// on every client with the defaults meant for a service talking to a service:
/// retries on 5xx, 408, 429 and timeouts for every method, ten seconds an
/// attempt, thirty in all. Neither half fits GitHub's calls. This client carries
/// writes that are not safe to send twice — <c>POST</c> an issue, <c>POST</c> a
/// ref, the backup's <c>PUT</c> of a whole database — and a 502 on one of those
/// does not say the write did not land. And that backup <c>PUT</c> and a branch
/// snapshot download are megabytes, not the small answer ten seconds is sized
/// for. So this client takes that pipeline off and puts its own on: retries for
/// reads only, and budgets sized for the largest transfer rather than the
/// typical call.
/// </para>
/// </summary>
public static class GitHubHttpRegistration
{
    /// <summary>How long one attempt is waited for: the hundred seconds a plain
    /// <see cref="HttpClient"/> allowed before this client came from the factory,
    /// which is what a database-sized <c>PUT</c> was already sized against.</summary>
    public static readonly TimeSpan AttemptBudget = TimeSpan.FromSeconds(100);

    /// <summary>How long one call is waited for across its retries. Only reads
    /// retry, so a write never sees more than one <see cref="AttemptBudget"/>.</summary>
    public static readonly TimeSpan TotalBudget = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Registers the named client <see cref="TokenTransport.HttpClientName"/>
    /// with the pipeline sized for GitHub's REST calls. The host passes its
    /// <see cref="IHttpClientFactory"/> to <see cref="ResolvingGitHubTransport"/>,
    /// which asks it for this client on every send.
    /// </summary>
    public static IServiceCollection AddGitHubHttpClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var github = services.AddHttpClient(TokenTransport.HttpClientName);

        // The experimental attribute on RemoveAllResilienceHandlers is a
        // warning about the API's shape, not its behaviour; it is the one
        // published way to take the host's default pipeline off a client.
#pragma warning disable EXTEXP0001
        github.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        github.AddStandardResilienceHandler(ConfigurePipeline);

        return services;
    }

    /// <summary>The options <see cref="AddGitHubHttpClient"/> applies, apart so a
    /// test can hold the pipeline to them without composing a host.</summary>
    internal static void ConfigurePipeline(HttpStandardResilienceOptions options)
    {
        // POST, PUT, PATCH, DELETE and CONNECT go out once. A retried write
        // is a second issue, or a backup PUT that conflicts with itself.
        options.Retry.DisableForUnsafeHttpMethods();
        options.AttemptTimeout.Timeout = AttemptBudget;
        options.TotalRequestTimeout.Timeout = TotalBudget;
        // The circuit breaker must sample over at least twice the attempt or
        // the options fail validation. Its minimum throughput is a hundred
        // calls, which one person's app does not reach in that window, so the
        // window is set for the validator, not the breaker.
        options.CircuitBreaker.SamplingDuration = AttemptBudget * 2;
    }
}
