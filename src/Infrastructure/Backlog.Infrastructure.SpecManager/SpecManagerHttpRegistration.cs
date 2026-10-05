using Backlog.Infrastructure.SpecManager.Api;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Backlog.Infrastructure.SpecManager;

/// <summary>
/// The "spec-manager" <see cref="HttpClient"/> and the pipeline on it (ADR 0015 in
/// <c>.devbook/arc42/adr/guidelines/</c>).
/// <para>
/// The host's <c>AddServiceDefaults()</c> puts the service-to-service pipeline on
/// every client, retrying every method. This client sends two posts that must not
/// go twice: a client registration — a second one is a second client at the
/// installation — and a token request, where a retried refresh presents a refresh
/// token the first attempt may already have rotated, which signs the person out.
/// So that pipeline comes off and this one goes on: retries for reads only, the
/// same shape <c>GitHubHttpRegistration</c> gives GitHub's client, with budgets
/// sized for a backlog that is one unpaged answer.
/// </para>
/// </summary>
internal static class SpecManagerHttpRegistration
{
    /// <summary>How long one attempt is waited for: a whole product's backlog in one
    /// answer.</summary>
    public static readonly TimeSpan AttemptBudget = TimeSpan.FromSeconds(30);

    /// <summary>How long one call is waited for across its retries.</summary>
    public static readonly TimeSpan TotalBudget = TimeSpan.FromMinutes(2);

    public static IServiceCollection AddSpecManagerHttpClient(this IServiceCollection services)
    {
        var client = services.AddHttpClient(SpecManagerClient.HttpClientName);

        // Experimental in its shape, not its behaviour; the one published way to
        // take the host's default pipeline off a client.
#pragma warning disable EXTEXP0001
        client.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        client.AddStandardResilienceHandler(ConfigurePipeline);

        return services;
    }

    /// <summary>The options <see cref="AddSpecManagerHttpClient"/> applies, apart so
    /// a test can hold the pipeline to them.</summary>
    internal static void ConfigurePipeline(HttpStandardResilienceOptions options)
    {
        options.Retry.DisableForUnsafeHttpMethods();
        options.AttemptTimeout.Timeout = AttemptBudget;
        options.TotalRequestTimeout.Timeout = TotalBudget;
        // The breaker must sample over at least twice the attempt or the options
        // fail validation.
        options.CircuitBreaker.SamplingDuration = AttemptBudget * 2;
    }
}
