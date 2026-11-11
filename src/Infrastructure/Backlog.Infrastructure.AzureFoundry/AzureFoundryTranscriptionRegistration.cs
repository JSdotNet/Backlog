using System.Net;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>
/// The transcription client's registration (local ADR 0045 §3), self-contained: its own
/// options, its own credential, and its own pipeline. It reads nothing of the desktop's
/// Foundry settings, so the sync service can call it with no settings store and no key.
/// <para>
/// The host's <c>AddServiceDefaults()</c> puts the standard pipeline on every client — ten
/// seconds an attempt, three retries, thirty seconds in all — which would cut a long
/// recording's upload off and send it again. This client takes that pipeline off, as
/// <see cref="AzureFoundryRegistration.AddAzureFoundryChatClient"/> does, and puts on what
/// guideline 0015 and the ADR ask for: one retry, on a 429 or a 5xx only, with jittered
/// exponential backoff, and the whole call bounded by
/// <see cref="AzureFoundryTranscriptionOptions.TotalBudget"/>.
/// </para>
/// </summary>
public static class AzureFoundryTranscriptionRegistration
{
    /// <summary>
    /// Registers <see cref="IAzureFoundryTranscriptionClient"/> configured by
    /// <paramref name="configure"/>, and the credential its
    /// <see cref="AzureFoundryTranscriptionOptions.Credential"/> names.
    /// </summary>
    /// <returns>The client's builder, so a test can put a handler under it.</returns>
    public static IHttpClientBuilder AddAzureFoundryTranscriptionClient(
        this IServiceCollection services,
        Action<AzureFoundryTranscriptionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AzureFoundryTranscriptionOptions>>().Value;

            return new AzureFoundryTranscriptionTokens(options.Credential switch
            {
                AzureFoundryTranscriptionCredential.None or AzureFoundryTranscriptionCredential.ApiKey => null,
                _ when string.IsNullOrWhiteSpace(options.ManagedIdentityClientId) =>
                    new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned),
                _ => new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId)),
            });
        });

        return AddClient(services);
    }

    /// <summary>
    /// Registers <see cref="IAzureFoundryTranscriptionClient"/> for the desktop (local ADR 0049
    /// §2): its endpoint, deployment, api-version and key read from the
    /// <see cref="AzureFoundrySettingsStore"/> registered beside it on every call, so a change in
    /// Settings needs no restart, and a budget of <see cref="DesktopPartBudget"/> per part,
    /// because nobody is waiting on a phone. The sync service never calls this: it keeps its
    /// own settings and identity (ADR 0045).
    /// </summary>
    public static IHttpClientBuilder AddAzureFoundryDesktopTranscriptionClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IOptions<AzureFoundryTranscriptionOptions>>(provider =>
            new DesktopTranscriptionOptions(provider.GetRequiredService<AzureFoundrySettingsStore>()));
        services.AddSingleton(new AzureFoundryTranscriptionTokens(null));

        return AddClient(services);
    }

    /// <summary>One part's whole call on the desktop, retry included: five minutes of audio
    /// answers in well under a minute, and the margin is for a slow deployment rather than
    /// for anyone waiting.</summary>
    public static readonly TimeSpan DesktopPartBudget = TimeSpan.FromMinutes(5);

    /// <summary>The desktop's options, rebuilt from the settings each time they are read.</summary>
    internal static AzureFoundryTranscriptionOptions FromSettings(AzureFoundrySettings settings) => new()
    {
        Endpoint = Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint) ? endpoint : null,
        Deployment = settings.TranscriptionDeployment,
        ApiVersion = settings.TranscriptionApiVersion,
        Credential = AzureFoundryTranscriptionCredential.ApiKey,
        ApiKey = settings.ApiKey,
        TotalBudget = DesktopPartBudget,
    };

    private sealed class DesktopTranscriptionOptions(AzureFoundrySettingsStore store) : IOptions<AzureFoundryTranscriptionOptions>
    {
        public AzureFoundryTranscriptionOptions Value => FromSettings(store.Current);
    }

    private static IHttpClientBuilder AddClient(IServiceCollection services)
    {
        var client = services.AddHttpClient<IAzureFoundryTranscriptionClient, AzureFoundryTranscriptionClient>(http =>
            // The pipeline's budget is the one that fires; this only keeps HttpClient's own
            // 100 seconds from being the bound anyone has to reason about.
            http.Timeout = Timeout.InfiniteTimeSpan);

        // The experimental attribute on RemoveAllResilienceHandlers is a warning about the
        // API's shape, not its behaviour; it is the one published way to take the host's
        // default pipeline off a client.
#pragma warning disable EXTEXP0001
        client.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        client.AddResilienceHandler("transcription", (pipeline, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<AzureFoundryTranscriptionOptions>>().Value;

            // Outermost, so the budget covers the retry and its backoff too.
            pipeline.AddTimeout(options.TotalBudget);

            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.RetryDelay,
                ShouldHandle = arguments => ValueTask.FromResult(
                    arguments.Outcome.Result is { } response && IsRetryable(response.StatusCode)),
            });
        });

        return client;
    }

    /// <summary>A 429 or any 5xx; nothing else — a refusal will be refused again.</summary>
    internal static bool IsRetryable(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;
}
