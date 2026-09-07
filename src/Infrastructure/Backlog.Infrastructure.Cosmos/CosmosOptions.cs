using System.ComponentModel.DataAnnotations;

namespace Backlog.Infrastructure.Cosmos;

/// <summary>
/// Which database and container the sync service replicates into, and how long a
/// tombstone survives there (inherited ADR 0018: bind, validate, fail fast).
/// <para>
/// No connection string and no key. Locally the AppHost supplies the emulator's
/// connection string under the resource name, and deployed the service reaches
/// Cosmos with a managed identity — a secret in this section would be a secret
/// this section then has to be kept out of source control for.
/// </para>
/// <para>
/// The defaults are the names the AppHost and <c>infra/sync/main.bicep</c>
/// already create, so a deployment that does not set this section is configured
/// correctly rather than unconfigured.
/// </para>
/// </summary>
public sealed class CosmosOptions
{
    /// <summary>The section this binds to.</summary>
    public const string SectionName = "Sync:Cosmos";

    /// <summary>The Aspire resource whose connection string names the account.
    /// It is the database resource rather than the account, which is what
    /// <c>WithReference(cosmosDatabase)</c> in the AppHost publishes.</summary>
    public const string ConnectionName = "backlog";

    /// <summary>The database holding the replica containers.</summary>
    [Required(AllowEmptyStrings = false)]
    public string DatabaseName { get; set; } = "backlog";

    /// <summary>The container holding task documents, partitioned on
    /// <c>/ownerId</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string TasksContainerName { get; set; } = "tasks";

    /// <summary>
    /// How long a tombstone is kept, in seconds — 180 days by default.
    /// <para>
    /// The bound is how long a device may be offline and still converge. Below
    /// it, a device coming back would not see the deletion and would push its
    /// copy again, resurrecting a task the person deleted six months ago. The
    /// range refuses a value under a day, which could only be a mistake, and one
    /// over two years, which is a retention decision rather than a sync one.
    /// </para>
    /// </summary>
    [Range(86_400, 63_072_000)]
    public int TaskTombstoneTtlSeconds { get; set; } = 15_552_000;
}
