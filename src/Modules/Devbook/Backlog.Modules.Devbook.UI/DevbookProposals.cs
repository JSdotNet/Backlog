using Backlog.Infrastructure.Devbook.Chapters;
using Backlog.Infrastructure.Devbook.Fingerprints;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// One change proposal under <c>openspec/changes/&lt;name&gt;/</c>, as the devbook
/// view offers it for a decision: its name, the change's fingerprint when it was
/// read — null when its files could not be read — and the state Backlog keeps for
/// it.
/// </summary>
public sealed record DevbookProposalRow(string Name, string? Fingerprint, DevbookSubjectState? State)
{
    public DevbookSubject Subject => DevbookSubject.Proposal(Name);

    /// <summary>Whether a decision stands at another fingerprint than the
    /// change's own: the change moved since, and the person decides
    /// again.</summary>
    public bool Lapsed(DevbookSubjectDecision? decision) =>
        decision is not null && Fingerprint is not null && !string.Equals(decision.Fingerprint, Fingerprint, StringComparison.Ordinal);

    /// <summary>Approve is offered while the change can be read and is not
    /// already approved at its current fingerprint.</summary>
    public bool CanApprove => Fingerprint is not null
        && !(State?.Approval is { } approval && string.Equals(approval.Fingerprint, Fingerprint, StringComparison.Ordinal));

    /// <summary>Accept is offered on an approval at the current fingerprint
    /// that is not yet accepted there.</summary>
    public bool CanAccept => Fingerprint is not null
        && State?.Approval is { } approval
        && string.Equals(approval.Fingerprint, Fingerprint, StringComparison.Ordinal)
        && !(State.Acceptance is { } acceptance && string.Equals(acceptance.Fingerprint, Fingerprint, StringComparison.Ordinal));
}

/// <summary>
/// The person's decisions on a repository's change proposals — Approve and Accept
/// in the devbook view, which local ADR 0052 §7 moved off MCP.
/// <para>
/// A decision goes through <see cref="DevbookProposalDecision"/>, the use case the
/// retired <c>devbook_approve</c> and <c>devbook_accept</c> tools ran, stamped
/// with <see cref="DevbookLocalAuthor.Name"/>: a click in the app is the person's.
/// It is taken at the fingerprint the change has at the click, which the caller
/// reads again (<see cref="DevbookChangeFolders.Fingerprint"/>), never the one the
/// row was drawn with.
/// </para>
/// </summary>
public static class DevbookProposals
{
    /// <summary>The rows for the changes <see cref="DevbookChangeFolders.Read"/>
    /// found, each with the state the store keeps for it.</summary>
    public static IReadOnlyList<DevbookProposalRow> Rows(
        IReadOnlyList<DevbookChangeReading> changes,
        IDevbookSubjectStateStore subjects,
        string? repositoryAlias)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(subjects);

        return
        [
            .. changes.Select(change => new DevbookProposalRow(
                change.Name,
                change.Fingerprint,
                subjects.Get(repositoryAlias, DevbookSubject.Proposal(change.Name))))
        ];
    }

    /// <summary>The person approves <paramref name="row"/> at
    /// <paramref name="fingerprint"/>, the change's fingerprint now.</summary>
    public static DevbookSubjectOutcome Approve(
        IDevbookSubjectStateStore subjects,
        IDevbookAnnotationStore annotations,
        string? repositoryAlias,
        bool repositoryArchived,
        DevbookProposalRow row,
        string? fingerprint) =>
        Decide(row, repositoryArchived, fingerprint, current =>
            DevbookProposalDecision.Approve(subjects, annotations, repositoryAlias, row.Subject, current, DevbookLocalAuthor.Name));

    /// <summary>The person accepts <paramref name="row"/> at
    /// <paramref name="fingerprint"/>, the change's fingerprint now.</summary>
    public static DevbookSubjectOutcome Accept(
        IDevbookSubjectStateStore subjects,
        IDevbookAnnotationStore annotations,
        string? repositoryAlias,
        bool repositoryArchived,
        DevbookProposalRow row,
        string? fingerprint) =>
        Decide(row, repositoryArchived, fingerprint, current =>
            DevbookProposalDecision.Accept(subjects, annotations, repositoryAlias, row.Subject, current, DevbookLocalAuthor.Name));

    private static DevbookSubjectOutcome Decide(
        DevbookProposalRow row,
        bool repositoryArchived,
        string? fingerprint,
        Func<string, DevbookSubjectOutcome> decide)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (repositoryArchived)
        {
            return DevbookSubjectOutcome.Archived(DevbookWriteGuard.ArchivedMessage("Proposals"));
        }

        return fingerprint is not null
            ? decide(fingerprint)
            : DevbookSubjectOutcome.Refuse(
                DevbookSubjectRefusal.BadState,
                $"'{row.Name}' could not be read, so there is no fingerprint to decide it at.");
    }
}
