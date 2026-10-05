using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>
/// Where a linked task came from: the item in another system that this task
/// follows, and what that system said about it at the last sync.
/// <para>
/// The inbound counterpart of a projection. A projection records an artifact
/// created <em>from</em> a task; this records the item a task was created
/// <em>from</em>, and the sync overwrites it whenever the source says something
/// new — including the normalised state the task's status is moved by when it
/// changes. A task without one is local work. See
/// <c>.devbook/arc42/adr/0020-external-items-arrive-as-linked-tasks.md</c>, §2.
/// </para>
/// <para>
/// <see cref="ConnectorId"/> is a string, not an enum, so a connector added later
/// needs no schema change and a device that meets an id it does not know still
/// has a display key to show. <see cref="Target"/> is the connected repository or
/// product the item came through: the sync archives a task whose item vanished,
/// and "vanished" only means something within the one target that was fetched.
/// </para>
/// <para>
/// Equality is by value, and <see cref="Flags"/> compares as a set: the sync asks
/// "did anything change" by comparing the reference it would write with the one
/// the task holds, and a reordered flag list is not a change. Flags are kept as
/// strings rather than an enum so a flag a newer build sets survives a round trip
/// through an older one — see <see cref="LinkedTaskFlags"/>.
/// </para>
/// <para>
/// In Abstractions rather than beside the aggregate for the reason
/// <see cref="Attachment"/> is: the aggregate holds one, the DTO publishes one and
/// the connector contract beside it is written in the same vocabulary.
/// </para>
/// </summary>
public sealed record SourceRef
{
    private readonly IReadOnlyList<string> _flags = [];
    private readonly string? _blockedReason;

    public SourceRef(
        string connectorId,
        string target,
        string externalId,
        string url,
        string displayKey,
        string? assignee,
        string sourceState,
        DateTimeOffset sourceUpdatedAt,
        IEnumerable<string>? flags = null,
        NormalisedSourceState? normalisedState = null,
        string? sourceTitle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        ConnectorId = connectorId;
        Target = target ?? string.Empty;
        ExternalId = externalId;
        Url = url ?? string.Empty;
        DisplayKey = displayKey ?? string.Empty;
        Assignee = string.IsNullOrWhiteSpace(assignee) ? null : assignee;
        SourceState = sourceState ?? string.Empty;
        SourceUpdatedAt = sourceUpdatedAt;
        Flags = flags?.ToList() ?? [];
        NormalisedState = normalisedState;
        SourceTitle = sourceTitle;
    }

    /// <summary>The connector's id, such as <c>github</c>.</summary>
    public string ConnectorId { get; init; }

    /// <summary>The connected target the item came through — a repository, a
    /// product slug — in the connector's own spelling.</summary>
    public string Target { get; init; }

    /// <summary>The source's own stable id for the item.</summary>
    public string ExternalId { get; init; }

    /// <summary>Where the item opens at the source.</summary>
    public string Url { get; init; }

    /// <summary>How a person names the item, such as <c>#412</c>.</summary>
    public string DisplayKey { get; init; }

    /// <summary>The source's assignee as a display name, or null when the item is
    /// unassigned.</summary>
    public string? Assignee { get; init; }

    /// <summary>The source's own status name, kept for display. What the task's
    /// status became is the sync's mapping, not this.</summary>
    public string SourceState { get; init; }

    /// <summary>The source's update stamp from the last sync.</summary>
    public DateTimeOffset SourceUpdatedAt { get; init; }

    /// <summary>
    /// The source's state as the sync last normalised it, or null on a reference
    /// written before this was kept.
    /// <para>
    /// What the sync compares against to decide whether the source's state
    /// <em>moved</em>. The task's status follows the source only on a move, so a
    /// person who starts or archives a linked task the source still holds open
    /// keeps their status until the source says something new (ADR 0020 §4, as
    /// amended at implementation). Null reads as "moved", once.
    /// </para>
    /// </summary>
    public NormalisedSourceState? NormalisedState { get; init; }

    /// <summary>
    /// The item's title at the source as the last sync saw it, or null on a
    /// reference written before this was kept.
    /// <para>
    /// How the sync tells a local rename from a title that merely went stale: the
    /// title follows the source only while the task's title still equals this, so a
    /// title the person changed stops following until they reset it (ADR 0020 §9).
    /// Null follows, once — an older reference cannot tell the two apart.
    /// </para>
    /// </summary>
    public string? SourceTitle { get; init; }

    /// <summary>
    /// Whether the source says the item cannot be worked on now; false on a
    /// reference written before this was kept. The source's, not a Backlog flag:
    /// the sync overwrites it on every run.
    /// <para>
    /// Also what the sync compares against to decide whether the source's
    /// blocked-ness <em>moved</em>. The task's own blocked mark follows the source
    /// only on a move, the way its status does, so a person who marks or clears it
    /// between two moves keeps what they set.
    /// </para>
    /// </summary>
    public bool Blocked { get; init; }

    /// <summary>Why the source says the item is blocked, or null when it gave no
    /// reason or the item is not blocked.</summary>
    public string? BlockedReason
    {
        get => _blockedReason;
        init => _blockedReason = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>The Backlog-owned sync flags, trimmed, de-duplicated and in
    /// ordinal order, so two equal sets are two equal lists.</summary>
    public IReadOnlyList<string> Flags
    {
        get => _flags;
        init => _flags = [.. (value ?? [])
            .Select(flag => (flag ?? string.Empty).Trim())
            .Where(flag => flag.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>Whether the reference carries <paramref name="flag"/>.</summary>
    public bool HasFlag(string flag) => _flags.Contains(flag, StringComparer.Ordinal);

    /// <summary>The same reference with <paramref name="flag"/> set or cleared.
    /// Answers this instance when nothing would change, so a caller comparing
    /// before and after sees no difference.</summary>
    public SourceRef WithFlag(string flag, bool set) =>
        HasFlag(flag) == set
            ? this
            : this with { Flags = set ? [.. _flags, flag] : [.. _flags.Where(existing => existing != flag)] };

    public bool Equals(SourceRef? other) =>
        other is not null
        && string.Equals(ConnectorId, other.ConnectorId, StringComparison.Ordinal)
        && string.Equals(Target, other.Target, StringComparison.Ordinal)
        && string.Equals(ExternalId, other.ExternalId, StringComparison.Ordinal)
        && string.Equals(Url, other.Url, StringComparison.Ordinal)
        && string.Equals(DisplayKey, other.DisplayKey, StringComparison.Ordinal)
        && string.Equals(Assignee, other.Assignee, StringComparison.Ordinal)
        && string.Equals(SourceState, other.SourceState, StringComparison.Ordinal)
        && SourceUpdatedAt.Equals(other.SourceUpdatedAt)
        && NormalisedState == other.NormalisedState
        && string.Equals(SourceTitle, other.SourceTitle, StringComparison.Ordinal)
        && Blocked == other.Blocked
        && string.Equals(BlockedReason, other.BlockedReason, StringComparison.Ordinal)
        && _flags.SequenceEqual(other._flags, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ConnectorId, StringComparer.Ordinal);
        hash.Add(Target, StringComparer.Ordinal);
        hash.Add(ExternalId, StringComparer.Ordinal);
        hash.Add(SourceUpdatedAt);
        hash.Add(NormalisedState);
        hash.Add(SourceTitle, StringComparer.Ordinal);
        hash.Add(Blocked);
        foreach (var flag in _flags) hash.Add(flag, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

/// <summary>
/// The flags the sync writes on a <see cref="SourceRef"/>. Backlog's, never the
/// source's: each one records something the sync noticed and left in place
/// rather than resolved.
/// <para>
/// Constants over strings rather than an enum, because the flags travel through
/// the replica and an older device must carry a flag it has no name for exactly
/// as it arrived, the way the payload carries a field it has no member for.
/// </para>
/// </summary>
public static class LinkedTaskFlags
{
    /// <summary>The item was no longer at the source, so its task was archived
    /// rather than deleted. Cleared when the item comes back.</summary>
    public const string Vanished = "vanished";

    /// <summary>The person finished the task while the source still holds the item
    /// open, so the sync left it Done. Cleared when the source closes it too.</summary>
    public const string DoneLocally = "done-locally";

    /// <summary>The item carried more than one <c>+</c> label; the first became the
    /// plan tag and the rest were dropped, so no effort counts in two plans.</summary>
    public const string MultiplePlanTags = "multiple-plan-tags";
}
