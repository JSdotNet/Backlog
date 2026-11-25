using Aspire.Hosting.ApplicationModel;

namespace Backlog.Aspire.AppHost;

/// <summary>
/// What the sync service needs once it is deployed, modelled in the AppHost (org ADR 0002):
/// its settings and secrets as AppHost parameters, its Application Insights component, and
/// the alert that watches that component for exceptions.
/// <para>
/// Publish mode only. A Development run needs none of it: the service mints a throwaway
/// signing key, enrols a device without the owner's sign-in, and defaults the connector's
/// issuer to its own endpoint, which <c>Program.cs</c> sets. A run-mode parameter with no
/// value holds its resource until someone types one into the dashboard, so declaring these
/// in run mode would stop every unattended <c>aspire start</c> on a question nobody is there
/// to answer.
/// </para>
/// <para>
/// The values come per environment from the AppHost's configuration under
/// <c>Parameters:&lt;name&gt;</c>, set with <c>aspire secret set --environment</c> for the
/// secrets. The required ones have no default, so a publish without them fails rather than
/// deploying a service that refuses to start. The two secrets kept in Key Vault — the token
/// signing key and the GitHub OAuth client secret — are not parameters: the service reads
/// them with its managed identity (local ADR 0058), and a managed identity beats a secret
/// wherever the target takes one. The Notification Hubs connection strings are parameters
/// because that service takes no managed identity (local ADR 0039 §5).
/// </para>
/// <para>
/// <c>infra/sync/main.bicep</c> and <c>azd</c> still provision and deploy the service; this
/// model is what <c>aspire publish</c> generates once the deployment moves onto it, and the
/// two name the same settings. Each parameter's comment names the Bicep parameter or
/// setting it stands for.
/// </para>
/// </summary>
internal static class SyncDeployment
{
    /// <summary>The Bicep template of the exception alert, beside this file.</summary>
    internal const string ExceptionAlertTemplate = "Alerts/sync-exception-alert.bicep";

    internal static void AddPublishedSyncSettings<T>(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<T> sync)
        where T : IResourceWithEnvironment
    {
        if (!builder.ExecutionContext.IsPublishMode) return;

        // --- Settings ------------------------------------------------------------------

        // The owner's numeric GitHub user id (ownerGitHubUserId); outside Development the
        // service refuses to start without it.
        var ownerGitHubUserId = builder.AddParameter("sync-owner-github-user-id")
            .WithDescription("Numeric GitHub user id of the owner; the only account that can enrol a device. `gh api user --jq .id`.");

        // The "Backlog sync" OAuth App's client id (gitHubOAuthClientId). Public, so a setting.
        var gitHubOAuthClientId = builder.AddParameter("sync-github-oauth-client-id")
            .WithDescription("Client id of the \"Backlog sync\" GitHub OAuth App the owner signs in through.");

        // The vault the signing key and the OAuth client secret are read from (keyVaultName).
        var keyVaultUri = builder.AddParameter("sync-key-vault-uri")
            .WithDescription("URI of the Key Vault holding sync-token-signing-key and github-oauth-client-secret.");

        // The service's public origin: the connector's issuer, and with /mcp its resource
        // (syncOrigin in the Bicep).
        var publicOrigin = builder.AddParameter("sync-public-origin")
            .WithDescription("The sync service's public https origin, with no path and no trailing slash: the connector's OAuth issuer.");

        // The owner pin (ownerId, azd's SYNC_OWNER_ID). Optional: empty derives the owner
        // from the GitHub user id, which the binder reads as no pin.
        var ownerId = builder.AddParameter("sync-owner-id", "", publishValueAsDefault: true)
            .WithDescription("Existing owner id (GUID) the owner's devices join; empty derives one from the GitHub user id.");

        // The Android push hub's name (pushHubName).
        var pushHub = builder.AddParameter("sync-notification-hubs-hub", "backlog", publishValueAsDefault: true)
            .WithDescription("The Notification Hubs hub the phones' installations live in.");

        // --- Secrets -------------------------------------------------------------------

        // The hub's two SAS rules, sync-send and sync-manage. Notification Hubs takes no
        // managed identity, so these are the only data credentials the service holds.
        var pushSend = builder.AddParameter("sync-notification-hubs-send-connection-string", secret: true)
            .WithDescription("Connection string of the hub's sync-send rule.");

        var pushManage = builder.AddParameter("sync-notification-hubs-manage-connection-string", secret: true)
            .WithDescription("Connection string of the hub's sync-manage rule.");

        sync.WithEnvironment("Owner__GitHubUserId", ownerGitHubUserId)
            .WithEnvironment("Owner__OwnerId", ownerId)
            .WithEnvironment("GitHub__ClientId", gitHubOAuthClientId)
            .WithEnvironment("Modules__Sync__Tokens__KeyVaultUri", keyVaultUri)
            .WithEnvironment("Modules__Sync__Connector__Issuer", publicOrigin)
            .WithEnvironment("Modules__Sync__Connector__Resource", ReferenceExpression.Create($"{publicOrigin}/mcp"))
            .WithEnvironment("Sync__NotificationHubs__Hub", pushHub)
            .WithEnvironment("Sync__NotificationHubs__SendConnectionString", pushSend)
            .WithEnvironment("Sync__NotificationHubs__ManageConnectionString", pushManage);

        // --- Observability and alerts ----------------------------------------------------

        // The component the sync tier reports to (appInsights in the Bicep). The reference
        // sets APPLICATIONINSIGHTS_CONNECTION_STRING, which ServiceDefaults exports to.
        var appInsights = builder.AddAzureApplicationInsights("sync-appinsights");
        sync.WithReference(appInsights);

        // Who the exception alert tells. Required: an alert nobody hears is no alert.
        var alertEmail = builder.AddParameter("sync-alert-email")
            .WithDescription("Email address the sync exception alert notifies.");

        // The exceptions alert (org ADR 0002: alert rules are modelled in the AppHost and
        // deploy with it). It watches the same `exceptions` table, grouped by the same
        // problemId, that .github/workflows/app-insights-exceptions.yml files issues from,
        // so the alert says a problem is happening and the workflow files it. Aspire has no
        // integration for a log alert, so it goes in through the Bicep customization hook.
        builder.AddBicepTemplate("sync-alerts", ExceptionAlertTemplate)
            .WithParameter("appInsightsName", appInsights.Resource.NameOutputReference)
            .WithParameter("alertEmail", alertEmail);
    }
}
