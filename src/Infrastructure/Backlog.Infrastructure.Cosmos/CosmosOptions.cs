using System.ComponentModel.DataAnnotations;

namespace Backlog.Infrastructure.Cosmos;

/// <summary>
/// Which database and containers the sync service replicates into, and how long a
/// task tombstone survives there (inherited ADR 0018: bind, validate, fail fast).
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
    /// The container holding session records, partitioned on <c>/ownerId</c> as
    /// well.
    /// <para>
    /// It gets no TTL option beside <see cref="TaskTombstoneTtlSeconds"/>, and
    /// that asymmetry is the point rather than an omission. Task retention has to
    /// be expressed per document — a live task carries no <c>ttl</c> and a
    /// tombstone carries one — so the number has to reach the code that writes
    /// the document. Session retention is the whole record at twelve months, so
    /// it is <c>defaultTtl</c> on the container in <c>infra/sync/main.bicep</c>
    /// and nothing in this process ever needs to know it. Configuring it here
    /// would be configuring something no code reads.
    /// </para>
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string SessionsContainerName { get; set; } = "sessions";

    /// <summary>
    /// The container holding Devbook annotation documents, partitioned on
    /// <c>/ownerId</c> as well. The third replica container: an annotation is
    /// edited and deleted like a task, so it carries the task container's
    /// per-document tombstone TTL (<see cref="AnnotationTombstoneTtlSeconds"/>)
    /// rather than the session container's whole-record one.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string AnnotationsContainerName { get; set; } = "annotations";

    /// <summary>
    /// The container holding registered devices, partitioned on <c>/id</c> —
    /// the device id, not the owner. The one read on every token mint has only
    /// the device id in hand, and partitioning on the owner would put a
    /// cross-partition query on that path to make the two owner-scoped reads
    /// behind the Devices tab point reads instead; see <c>DeviceDocument</c>.
    /// No TTL: a registration lasts until somebody forgets the device.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string DevicesContainerName { get; set; } = "devices";

    /// <summary>
    /// The container holding live pairing codes, partitioned on <c>/id</c> —
    /// the code hash, which is the only handle a redemption ever has. Its TTL is
    /// per document like a tombstone's, from the code's own expiry, and is not
    /// configurable here: the window is fixed by the handler that issues the
    /// code, so the number the document carries is derived rather than chosen.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string PairingCodesContainerName { get; set; } = "pairingCodes";

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

    /// <summary>
    /// How long a request waits, in seconds, for an account that has not
    /// answered yet before it is told 503 <c>sync.replica_unavailable</c> — two
    /// by default. See <c>CosmosAccountGate</c>.
    /// <para>
    /// Short on purpose: the desktop gives up on an attempt after ten seconds,
    /// and a 503 it receives is one it can branch on where a timeout is not.
    /// The account read carries on past the budget, so a store that is merely
    /// slow to answer the first time is ready for the next request rather than
    /// refused again.
    /// </para>
    /// </summary>
    [Range(1, 30)]
    public int ReadinessTimeoutSeconds { get; set; } = 2;

    /// <summary>How long an annotation tombstone is kept, in seconds — the same
    /// 180 days as a task's and for the same offline-convergence bound. Its own
    /// setting so the two retentions can be provisioned apart without a code
    /// change.</summary>
    [Range(86_400, 63_072_000)]
    public int AnnotationTombstoneTtlSeconds { get; set; } = 15_552_000;

    /// <summary>
    /// The container holding notification documents (local ADR 0039), partitioned on
    /// <c>/ownerId</c> — the fourth replica container. Each notification has one writer, its
    /// owner instance, and expires <see cref="NotificationSettledTtlSeconds"/> after it left
    /// Open.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string NotificationsContainerName { get; set; } = "notifications";

    /// <summary>
    /// The container holding replies to notifications, partitioned on <c>/ownerId</c>. A
    /// reply is written once by a viewer and deleted by the owner instance once processed;
    /// <see cref="NotificationReplyTtlSeconds"/> is only the backstop for an owner that never
    /// comes back.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string NotificationRepliesContainerName { get; set; } = "notificationReplies";

    /// <summary>How long a notification is kept after it left Open, in seconds — 30 days
    /// (local ADR 0039 §3).</summary>
    [Range(86_400, 63_072_000)]
    public int NotificationSettledTtlSeconds { get; set; } = 2_592_000;

    /// <summary>
    /// How long an open notification is kept after its last push, in seconds — 90 days. Not a
    /// deadline: the owner instance pushes an open notification again well inside it, so a
    /// question on a running desktop never expires. It is what makes an owner that is unpaired
    /// for good leave nothing behind (ADR 0039 §1, "those expire").
    /// </summary>
    [Range(86_400, 63_072_000)]
    public int NotificationOpenTtlSeconds { get; set; } = 7_776_000;

    /// <summary>How long an unprocessed reply is kept, in seconds — 30 days, the same as a
    /// settled notification: past it, the question the reply answers is gone too.</summary>
    [Range(86_400, 63_072_000)]
    public int NotificationReplyTtlSeconds { get; set; } = 2_592_000;
}
