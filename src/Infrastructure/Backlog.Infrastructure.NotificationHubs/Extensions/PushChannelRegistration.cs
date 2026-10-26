using Backlog.Modules.Sync.Ports;

using Microsoft.Azure.NotificationHubs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.NotificationHubs.Extensions;

/// <summary>
/// Wires the Notification Hubs push into a host — or, with nothing configured, deliberately
/// does nothing, so the module's no-op channel stands and every phone learns of a notification
/// on its next pull (local ADR 0039 §4).
/// </summary>
public static partial class PushChannelRegistration
{
    /// <summary>The Send-only rule's connection string. Bicep sets it as
    /// <c>Sync__NotificationHubs__SendConnectionString</c> from a container app secret; nothing
    /// sets it under Aspire.</summary>
    public const string SendConnectionStringKey = "Sync:NotificationHubs:SendConnectionString";

    /// <summary>The Manage rule's connection string, which creating and deleting an installation
    /// needs. Set beside the Send one, from its own secret.</summary>
    public const string ManageConnectionStringKey = "Sync:NotificationHubs:ManageConnectionString";

    /// <summary>The hub's name, which Bicep sets beside the connection strings.</summary>
    public const string HubKey = "Sync:NotificationHubs:Hub";

    /// <summary>The hub the phones are pushed through when <see cref="HubKey"/> names none.</summary>
    public const string DefaultHub = "backlog";

    /// <summary>
    /// Registers the channel that keeps the phones' installations and sends their pushes, over
    /// two Notification Hubs clients built from the SAS connection strings.
    /// <para>
    /// <strong>With neither connection string it registers nothing, in every
    /// environment.</strong> That is the Aspire run and the endpoint tests, and deliberately also
    /// a deployed service whose settings went missing: the push holds no data, so the fallback
    /// loses nothing. A deployed host says so in its log at startup instead of refusing to start.
    /// One without the other is a broken deployment, and refuses to start naming the missing key.
    /// </para>
    /// <para>
    /// Call it before <c>AddSyncModule()</c>; the module's registration is a <c>TryAdd</c>, so
    /// whichever runs first wins.
    /// </para>
    /// </summary>
    public static TBuilder AddNotificationHubsPush<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        var sendConnection = builder.Configuration[SendConnectionStringKey];
        var manageConnection = builder.Configuration[ManageConnectionStringKey];

        if (string.IsNullOrWhiteSpace(sendConnection) && string.IsNullOrWhiteSpace(manageConnection))
        {
            if (!builder.Environment.IsDevelopment())
            {
                builder.Services.AddHostedService<MissingPushWarning>();
            }

            return builder;
        }

        var missing = string.IsNullOrWhiteSpace(sendConnection) ? SendConnectionStringKey
            : string.IsNullOrWhiteSpace(manageConnection) ? ManageConnectionStringKey
            : null;

        if (missing is not null)
        {
            throw new InvalidOperationException(
                $"'{missing}' is not set while its partner is; the push needs both the Send and the Manage connection strings.");
        }

        var configuredHub = builder.Configuration[HubKey];
        var hub = string.IsNullOrWhiteSpace(configuredHub) ? DefaultHub : configuredHub;

        // Built here rather than in the factory, so a connection string that is not one fails the
        // start naming its key (guideline ADR 0018) instead of failing every owner push the first
        // time a handler resolves the channel.
        var send = Client(SendConnectionStringKey, sendConnection!, hub);
        var manage = Client(ManageConnectionStringKey, manageConnection!, hub);

        // Add, not TryAdd: this is the concrete registration the no-op stand-in steps aside for.
        // A factory, because the two clients share one interface and differ only in their rule.
        builder.Services.AddSingleton<INotificationPushChannel>(services => new NotificationHubsPushChannel(
            send,
            manage,
            services.GetRequiredService<ILogger<NotificationHubsPushChannel>>()));

        return builder;
    }

    /// <summary>A client for one rule, from the connection string under <paramref name="key"/>.
    /// Building it parses the string and opens no connection; a string that is not a
    /// Notification Hubs connection string throws naming the key and never echoing the value,
    /// which holds the SAS key.</summary>
    private static NotificationHubClient Client(string key, string connectionString, string hub)
    {
        try
        {
            var parsed = new NotificationHubConnectionStringBuilder(connectionString);

            if (parsed.Endpoint is null
                || string.IsNullOrWhiteSpace(parsed.SharedAccessKeyName)
                || string.IsNullOrWhiteSpace(parsed.SharedAccessKey))
            {
                throw new FormatException("It names no endpoint, SharedAccessKeyName or SharedAccessKey.");
            }

            return new NotificationHubClient(connectionString, hub, new NotificationHubSettings
            {
                // Guideline ADR 0015: no unbounded waits. The SDK's defaults wait 60 seconds per
                // operation and retry several times, inside the owner's push request; one retry
                // with a short timeout is all a push is worth.
                OperationTimeout = TimeSpan.FromSeconds(5),
                RetryOptions = new NotificationHubRetryOptions { MaxRetries = 1 },
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Not chained: the SDK's own message may quote the string, and the string holds the
            // SAS key. The exception type is enough to tell the two failures apart.
            throw new InvalidOperationException(
                $"'{key}' is not a Notification Hubs connection string (Endpoint=sb://…;SharedAccessKeyName=…;SharedAccessKey=…); the parser said {ex.GetType().Name}.");
        }
    }

    /// <summary>Says once, at startup, that a deployed service is running without its push.</summary>
    private sealed partial class MissingPushWarning(ILogger<MissingPushWarning> log) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            LogMissing(log, SendConnectionStringKey, ManageConnectionStringKey);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        [LoggerMessage(Level = LogLevel.Warning, Message = "'{SendKey}' and '{ManageKey}' are not set, so no phone is pushed; every phone learns of a notification on its next pull.")]
        private static partial void LogMissing(ILogger logger, string sendKey, string manageKey);
    }
}
