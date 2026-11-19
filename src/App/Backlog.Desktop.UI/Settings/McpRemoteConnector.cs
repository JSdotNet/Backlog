using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

// The component's published namespace, not the folder's: see the note at the top
// of Settings.razor for why the two must differ.
namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// What the MCP server page's Remote connector card says (local ADR 0058 §8): why it offers
/// nothing, where the secret stands, and the questions its destructive acts ask in place.
/// Words only, so the card and its tests agree on them without rendering.
/// </summary>
public static class McpRemoteConnector
{
    /// <summary>The card's title - one of the page's <c>SettingsSection.CardTitles</c>.</summary>
    public const string CardTitle = "Remote connector";

    /// <summary>The sentence under the title.</summary>
    public const string Description =
        "claude.ai reaches your Inbox through the sync service. Enter this client id and a secret created here in its custom connector.";

    /// <summary>What a host with no sync client says, and offers nothing.</summary>
    public const string NoSyncClient = "This host has no sync client, so it manages no remote connector.";

    /// <summary>What a device that never paired says, and offers nothing.</summary>
    public const string NotPaired =
        "This device is not paired with a sync service. Pair it on the Devices page to manage the remote connector.";

    /// <summary>The line over a secret just created.</summary>
    public const string SecretShownOnce =
        "Copy this secret into claude.ai now. It is shown once: after Done only a new secret replaces it.";

    /// <summary>The question Revoke asks before it acts.</summary>
    public const string RevokeQuestion = "Revoke the connector's secret? claude.ai loses access now.";

    /// <summary>The question Replace secret asks before it acts: the old secret stops working
    /// the moment the new one is made.</summary>
    public const string ReplaceQuestion = "Replace the secret? claude.ai loses access until you enter the new one.";

    /// <summary>Whether a secret exists, and when it was made and last used.</summary>
    public static string State(ConnectorStatusResponse status, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(status);

        if (!status.HasSecret) return "No secret yet. Create one to connect claude.ai.";

        var created = status.CreatedAt is { } at ? $"created {AiProvidersOverview.Ago(at, now)}" : "created earlier";
        var used = status.LastUsedAt is { } last ? $"last used {AiProvidersOverview.Ago(last, now)}" : "not used yet";
        return $"A secret is set: {created} · {used}.";
    }
}
