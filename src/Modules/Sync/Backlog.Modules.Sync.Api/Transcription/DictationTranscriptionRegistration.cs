using Backlog.Infrastructure.AzureFoundry;
using Backlog.Modules.Sync.Api.Options;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Api.Transcription;

/// <summary>
/// Wires the dictation relay into the sync service (local ADR 0045) — or, with no endpoint
/// configured, wires only its settings, so the module's stand-in answers "unavailable" and
/// every phone dictates on the device.
/// </summary>
internal static class DictationTranscriptionRegistration
{
    /// <summary>The variable the deployed container names its user-assigned identity in,
    /// which <c>infra/sync/main.bicep</c> sets.</summary>
    internal const string ManagedIdentityClientIdKey = "AZURE_CLIENT_ID";

    /// <summary>
    /// Binds and validates <see cref="SyncTranscriptionOptions"/>, and — when an endpoint is
    /// set — registers the Foundry transcription client and the transcriber over it.
    /// <para>
    /// Call it before <c>AddSyncModule()</c>: the module's stand-in is a <c>TryAdd</c>, so
    /// whichever runs first wins. A missing endpoint is a 503 in every environment rather
    /// than a refusal to start, because a phone that is told so falls back and loses nothing.
    /// </para>
    /// </summary>
    internal static WebApplicationBuilder AddDictationTranscription(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<SyncTranscriptionOptions>()
            .BindConfiguration(SyncTranscriptionOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var settings = builder.Configuration.GetSection(SyncTranscriptionOptions.SectionName).Get<SyncTranscriptionOptions>()
                       ?? new SyncTranscriptionOptions();

        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            return builder;
        }

        var managedIdentityClientId = builder.Configuration[ManagedIdentityClientIdKey];

        builder.Services.AddAzureFoundryTranscriptionClient(foundry =>
        {
            // An endpoint that does not parse leaves this null; the options validation above
            // then stops the start and says why.
            foundry.Endpoint = settings.EndpointUri();
            foundry.Deployment = settings.Deployment;
            foundry.ApiVersion = settings.ApiVersion;
            foundry.Credential = settings.Credential;
            foundry.ManagedIdentityClientId = managedIdentityClientId;
        });

        builder.Services.AddSingleton<IDictationTranscriber, FoundryDictationTranscriber>();

        return builder;
    }
}
