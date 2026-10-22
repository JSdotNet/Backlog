using Backlog.Modules.Sync.Ports;

using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.WebPubSub.Extensions;

/// <summary>
/// Wires the Web PubSub nudge into a host — or, with nothing configured, deliberately does
/// nothing, so the module's no-op channel stands and every desktop polls (local ADR 0038 §4).
/// </summary>
public static partial class NudgeChannelRegistration
{
    /// <summary>The hub's endpoint, <c>https://&lt;name&gt;.webpubsub.azure.com</c>. Bicep
    /// sets it as <c>Sync__WebPubSub__Endpoint</c>; nothing sets it under Aspire.</summary>
    public const string EndpointKey = "Sync:WebPubSub:Endpoint";

    /// <summary>The hub's name, which Bicep sets beside the endpoint.</summary>
    public const string HubKey = "Sync:WebPubSub:Hub";

    /// <summary>The hub the desktops connect to when <see cref="HubKey"/> names none.</summary>
    public const string DefaultHub = "notifications";

    /// <summary>
    /// Registers the Web PubSub service client, reached with the managed identity, and the
    /// channel that mints client URLs and publishes through it.
    /// <para>
    /// <strong>With no endpoint it registers nothing, in every environment.</strong> That is
    /// the Aspire run and the endpoint tests, and it is deliberately also a deployed service
    /// whose setting went missing: unlike a replica or the attachment store, the nudge holds no
    /// data, so the fallback loses nothing — every desktop keeps its two-minute poll. A deployed
    /// host says so in its log at startup instead of refusing to start.
    /// </para>
    /// <para>
    /// Call it before <c>AddSyncModule()</c>; the module's registration is a <c>TryAdd</c>, so
    /// whichever runs first wins.
    /// </para>
    /// </summary>
    public static TBuilder AddWebPubSubNudge<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        var endpoint = builder.Configuration[EndpointKey];

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            if (!builder.Environment.IsDevelopment())
            {
                builder.Services.AddHostedService<MissingNudgeWarning>();
            }

            return builder;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) || endpointUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"'{EndpointKey}' must be the hub's absolute https endpoint; it was '{endpoint}'.");
        }

        var hub = builder.Configuration[HubKey];

        builder.AddAzureWebPubSubServiceClient(
            connectionName: "webpubsub",
            configureSettings: settings =>
            {
                settings.Endpoint = endpointUri;
                settings.HubName = string.IsNullOrWhiteSpace(hub) ? DefaultHub : hub;

                // The health check calls the hub, and a sync service that scales to zero
                // should not be woken, or reported unhealthy, over a channel that only nudges.
                settings.DisableHealthChecks = true;
            },
            configureClientBuilder: client => client.ConfigureOptions(options =>
            {
                // Guideline ADR 0015: no unbounded waits. The SDK's defaults retry three times
                // with a 100-second network timeout each, inside a push or reply request; one
                // retry with a short timeout is all a nudge is worth.
                options.Retry.MaxRetries = 1;
                options.Retry.NetworkTimeout = TimeSpan.FromSeconds(5);
            }));

        // Add, not TryAdd: this is the concrete registration the no-op stand-in steps aside for.
        builder.Services.AddSingleton<INotificationNudgeChannel, WebPubSubNudgeChannel>();

        return builder;
    }

    /// <summary>Says once, at startup, that a deployed service is running without its nudge.</summary>
    private sealed partial class MissingNudgeWarning(ILogger<MissingNudgeWarning> log) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            LogMissing(log, EndpointKey);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        [LoggerMessage(Level = LogLevel.Warning, Message = "'{Key}' is not set, so no desktop is nudged; every desktop falls back to its two-minute poll.")]
        private static partial void LogMissing(ILogger logger, string key);
    }
}
