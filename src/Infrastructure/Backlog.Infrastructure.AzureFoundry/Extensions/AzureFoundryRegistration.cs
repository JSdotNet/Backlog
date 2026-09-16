using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly.Timeout;

namespace Backlog.Infrastructure.AzureFoundry.Extensions;

/// <summary>The named clients this adapter registers, so a test can read a
/// pipeline back under the name the standard handler files it under.</summary>
public static class AzureFoundryHttpClients
{
    public const string Chat = "azure-foundry-chat";
}

public static class AzureFoundryRegistration
{
    /// <summary>How long one chat completion may take. A model writing a plan
    /// from an inbox item runs well past the ten seconds the host's defaults
    /// allow an attempt; this is the budget for the answer, not for a header.</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(90);

    /// <summary>The whole request, one retry included. The retry below only
    /// follows a refusal that came back at once, so this is the attempt plus a
    /// little, not two attempts.</summary>
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// The typed chat client on a resilience pipeline sized for a chat completion.
    /// <para>
    /// Both hosts call <c>AddServiceDefaults()</c>, whose standard pipeline —
    /// a ten-second attempt, three retries, thirty seconds in all — is a web
    /// service's budget. Against a model it fails in two ways at once: an answer
    /// that takes longer than ten seconds is cut off and asked for again, each
    /// retry re-sending the whole prompt and paying for it, and after thirty
    /// seconds the pipeline throws Polly's timeout rejection at whoever asked.
    /// The first of those to reach the Home page's Ask handler took the page
    /// down. So the host's pipeline is taken off this client and its own put on:
    /// one long attempt, and a single retry that follows only a refusal that
    /// cost nothing — a connection that never opened, a 429, a 5xx — never an
    /// attempt that already timed out.
    /// </para>
    /// <para>
    /// The circuit breaker's sampling window is stretched with the attempt: the
    /// handler refuses a window shorter than two attempts, and the default of
    /// thirty seconds would be. The client's exception translation lives in
    /// <see cref="AzureFoundryChatClient"/>, which is where the timeout this
    /// pipeline throws becomes the client's own failure.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAzureFoundryChatClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var chat = services.AddHttpClient<IAzureFoundryChatClient, AzureFoundryChatClient>(AzureFoundryHttpClients.Chat);

        // The experimental attribute on RemoveAllResilienceHandlers is a
        // warning about the API's shape, not its behaviour; it is the one
        // published way to take the host's default pipeline off a client.
#pragma warning disable EXTEXP0001
        chat.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        chat.AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = AttemptTimeout;
            options.TotalRequestTimeout.Timeout = TotalTimeout;
            options.CircuitBreaker.SamplingDuration = AttemptTimeout * 2;
            options.Retry.MaxRetryAttempts = 1;
            options.Retry.ShouldHandle = args => new ValueTask<bool>(
                args.Outcome.Exception is not TimeoutRejectedException
                && HttpClientResiliencePredicates.IsTransient(args.Outcome));
        });

        return services;
    }
}
