using Backlog.Modules.Dashboard.Abstractions.Insights;

namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>
/// One place usage is read from: a provider, and the account within it.
/// <para>
/// Claude is read per organization, so <see cref="Account"/> is the Settings card's
/// account id. GitHub's AI-credit report is read as one source across every login
/// this machine holds, so it has the single <see cref="GitHub"/> value. Azure
/// Foundry's spend is read from the one Cost Management scope Settings names, so
/// it has the single <see cref="AzureFoundry"/> value.
/// </para>
/// </summary>
public readonly record struct UsageSource(SpendProvider Provider, string Account)
{
    /// <summary>The account name GitHub's one source carries.</summary>
    public const string GitHubAccount = "github";

    /// <summary>The GitHub usage source.</summary>
    public static UsageSource GitHub { get; } = new(SpendProvider.Copilot, GitHubAccount);

    /// <summary>The account name Azure Foundry's one source carries.</summary>
    public const string AzureFoundryAccount = "azure-foundry";

    /// <summary>The Azure Foundry usage source.</summary>
    public static UsageSource AzureFoundry { get; } = new(SpendProvider.AzureFoundry, AzureFoundryAccount);

    /// <summary>One Claude organization, by its account id.</summary>
    public static UsageSource Claude(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        return new(SpendProvider.Claude, accountId);
    }
}

/// <summary>
/// When a usage source was last read. <see cref="FailedAt"/> and
/// <see cref="FailureReason"/> are set only while the most recent read failed: a
/// success afterwards clears them, and <see cref="SucceededAt"/> keeps the last
/// success through any number of failures.
/// </summary>
public sealed record UsageReadStatus(
    DateTimeOffset? SucceededAt,
    DateTimeOffset? FailedAt,
    string? FailureReason)
{
    /// <summary>Whether the most recent read failed.</summary>
    public bool LastReadFailed => FailedAt is not null;
}

/// <summary>
/// The read model the Settings page shows beside each AI provider: when each usage
/// source was last read, kept per device and never synced.
/// </summary>
public interface IUsageReadStatus
{
    /// <summary>Raised after a read is recorded, success or failure.</summary>
    event Action? Changed;

    /// <summary>The source's status, or null when it has never been read on this
    /// device.</summary>
    UsageReadStatus? For(UsageSource source);

    /// <summary>Every source read on this device.</summary>
    IReadOnlyDictionary<UsageSource, UsageReadStatus> All { get; }
}

/// <summary>
/// What a usage reader calls when a read completes. Never throws: a status that
/// cannot be saved is a status lost, not a failed read.
/// </summary>
public interface IUsageReadRecorder
{
    void Succeeded(UsageSource source);

    /// <summary>Records a failed read. Only the first line of
    /// <paramref name="reason"/> is kept.</summary>
    void Failed(UsageSource source, string reason);
}
