using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// The folder this exchange lives in shares the Devbook module's class name.
using SubjectNames = Backlog.Modules.Devbook.Abstractions.DevbookSubjects;

namespace Backlog.Infrastructure.Sync.DevbookSubjects;

/// <summary>What one page did: how many documents were written.</summary>
public sealed record DevbookSubjectMergeOutcome(int Applied);

/// <summary>
/// Reconciles a page of the owner's devbook subject feed with this device's own
/// subject state store — <see cref="Annotations.AnnotationReplicaMerge"/>'s rules
/// without the tombstone, because the two devices have to agree on them exactly.
/// <para>
/// Within a page, the later of two records for one subject is decided by the
/// replica's stamp, then the writing device's <c>UpdatedAt</c>, then the device
/// id. Against the local copy, an arriving version wins only when its
/// <c>UpdatedAt</c> is later — the rule the replica applies to a push
/// (<c>DevbookSubjectChangePrecedence</c>), here
/// <see cref="DevbookSubjectRules.Supersedes"/> — and then it wins whole: state,
/// who, when, approval and acceptance together. The store applies that rule in
/// the same step as its write (<see cref="IDevbookSubjectStateStore.Apply"/>), so
/// nothing set on this device while a page is merged can be overwritten.
/// </para>
/// <para>
/// A record this device could not file is skipped rather than thrown on: one
/// whose fields the wire's own rule refuses, one whose key names no subject, or
/// one filed under any id but <see cref="DevbookSubject.IdFor"/> of its alias and
/// subject. That last is what keeps two devices on one document per subject — a
/// state under a stray id would be a second state nothing reads.
/// </para>
/// <para>
/// This class is also where the wire shape and the store's record meet, in both
/// directions.
/// </para>
/// </summary>
public sealed class DevbookSubjectReplicaMerge
{
    private readonly IDevbookSubjectStateStore _store;
    private readonly ILogger _log;

    public DevbookSubjectReplicaMerge(IDevbookSubjectStateStore store, ILogger<DevbookSubjectReplicaMerge>? log = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _log = log ?? NullLogger<DevbookSubjectReplicaMerge>.Instance;
    }

    /// <summary>The wire shape of one stored state.</summary>
    public static DevbookSubjectChange ToChange(DevbookSubjectState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new DevbookSubjectChange(
            state.Id,
            state.UpdatedAt,
            new DevbookSubjectPayload(
                state.RepositoryAlias,
                SubjectNames.KindName(state.Subject.Kind),
                state.Subject.Key,
                state.Subject.PullRequest,
                state.State,
                state.By,
                state.At,
                Payload(state.Approval),
                Payload(state.Acceptance)));
    }

    /// <summary>The stored record for one arriving change, or null when this
    /// device could not file it — see the class remarks.</summary>
    public static DevbookSubjectState? ToState(DevbookSubjectChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (DevbookSubjectRecordLimits.WhyRefused(change) is not null) return null;

        var payload = change.Subject;

        if (!SubjectNames.TryParseKind(payload.Kind, out var kind)) return null;

        DevbookSubject subject;
        try
        {
            subject = new DevbookSubject(kind, payload.Key, payload.PullRequest);
        }
        catch (ArgumentException)
        {
            // A key the wire rule lets through and the contract cannot name —
            // a path that normalizes to nothing.
            return null;
        }

        if (change.Id != DevbookSubject.IdFor(payload.RepositoryAlias, subject)) return null;

        return new DevbookSubjectState(
            change.Id,
            payload.RepositoryAlias,
            subject,
            payload.State,
            payload.By,
            payload.At,
            Decision(payload.Approval),
            Decision(payload.Acceptance),
            change.UpdatedAt);
    }

    /// <summary>Whether <paramref name="inbound"/> is the later of two records
    /// for the same subject — the three-field rule, static and pure so it can be
    /// asserted on its own.</summary>
    public static bool Wins(DevbookSubjectChangeRecord inbound, DevbookSubjectChangeRecord against)
    {
        ArgumentNullException.ThrowIfNull(inbound);
        ArgumentNullException.ThrowIfNull(against);

        if (inbound.ServerTimestamp != against.ServerTimestamp)
        {
            return inbound.ServerTimestamp > against.ServerTimestamp;
        }

        if (inbound.Change.UpdatedAt != against.Change.UpdatedAt)
        {
            return inbound.Change.UpdatedAt > against.Change.UpdatedAt;
        }

        return string.Compare(
            inbound.DeviceId.ToString("D"),
            against.DeviceId.ToString("D"),
            StringComparison.Ordinal) > 0;
    }

    /// <summary>Applies a page of the feed, reduced to one winner per subject
    /// first, and answers how many documents it wrote.</summary>
    public DevbookSubjectMergeOutcome Apply(IReadOnlyList<DevbookSubjectChangeRecord> page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var applied = 0;

        foreach (var record in Winners(page))
        {
            if (ToState(record.Change) is not { } arriving)
            {
                _log.LogWarning(
                    "Skipping devbook subject {SubjectId} from the sync service: this device could not file it under its subject.",
                    record.Change.Id);
                continue;
            }

            // The store decides against its own copy, under the lock it writes
            // under: a merge that read the local copy first and wrote second
            // would let a state set here in between be overwritten by an older
            // one, and lost for good.
            if (_store.Apply(arriving)) applied++;
        }

        return new DevbookSubjectMergeOutcome(applied);
    }

    private static IEnumerable<DevbookSubjectChangeRecord> Winners(IReadOnlyList<DevbookSubjectChangeRecord> page)
    {
        var winners = new Dictionary<Guid, DevbookSubjectChangeRecord>();

        foreach (var record in page)
        {
            if (!winners.TryGetValue(record.Change.Id, out var current) || Wins(record, current))
            {
                winners[record.Change.Id] = record;
            }
        }

        return winners.Values;
    }

    private static DevbookSubjectDecisionPayload? Payload(DevbookSubjectDecision? decision) =>
        decision is null ? null : new DevbookSubjectDecisionPayload(decision.By, decision.At, decision.Fingerprint);

    private static DevbookSubjectDecision? Decision(DevbookSubjectDecisionPayload? payload) =>
        payload is null ? null : new DevbookSubjectDecision(payload.By, payload.At, payload.Fingerprint);
}
