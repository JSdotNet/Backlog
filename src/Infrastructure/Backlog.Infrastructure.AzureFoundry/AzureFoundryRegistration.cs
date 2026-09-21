using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>
/// The chat client's registration, in one place both hosts call so the
/// pipeline on it cannot drift between the desktop app and the web harness.
/// <para>
/// The host's <c>AddServiceDefaults()</c> puts the standard resilience pipeline
/// on every client with the defaults meant for a service talking to a service:
/// ten seconds an attempt, three retries, thirty seconds in all. A chat
/// completion over a screen of tasks routinely takes longer than ten seconds,
/// so under those defaults every real answer was cut off, re-sent and cut off
/// again until the thirty seconds ran out — as Polly's timeout exception, which
/// nothing in the panel was written for. This client takes that pipeline off
/// and puts its own on, with one answer given the whole
/// <see cref="AnswerBudget"/>: the attempt and the total are the same two
/// minutes, so a slow answer is waited for once rather than retried into the
/// budget, while a fast refusal — a 429, a 503 — is still retried with backoff
/// inside it. Its own rather than a second one beside the host's, because the
/// outer pipeline's thirty seconds would still be the one that fires.
/// </para>
/// </summary>
public static class AzureFoundryRegistration
{
    /// <summary>How long one completion is waited for. Two minutes covers the
    /// longest answer over the content cap the panel sends while still ending a
    /// deployment that has stopped answering; the panel shows the wait as
    /// "Asking..." and the timeout as a toast.</summary>
    public static readonly TimeSpan AnswerBudget = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Registers <see cref="IAzureFoundryChatClient"/> over an
    /// <see cref="HttpClient"/> with the pipeline sized for a chat completion.
    /// Needs an <see cref="AzureFoundrySettingsStore"/> registered beside it.
    /// </summary>
    public static IServiceCollection AddAzureFoundryChatClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var chat = services.AddHttpClient<IAzureFoundryChatClient, AzureFoundryChatClient>();

        // The experimental attribute on RemoveAllResilienceHandlers is a
        // warning about the API's shape, not its behaviour; it is the one
        // published way to take the host's default pipeline off a client.
#pragma warning disable EXTEXP0001
        chat.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        chat.AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = AnswerBudget;
            options.TotalRequestTimeout.Timeout = AnswerBudget;
            // The circuit breaker must sample over at least twice the attempt
            // or the options fail validation. It will not open for one person
            // asking one question at a time — its minimum throughput is a
            // hundred calls — so the window is set for the validator, not
            // the breaker.
            options.CircuitBreaker.SamplingDuration = AnswerBudget * 2;
        });

        return services;
    }
}
