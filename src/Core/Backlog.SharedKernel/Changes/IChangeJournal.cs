namespace Backlog.SharedKernel.Changes;

/// <summary>
/// Reads the change journal (local ADR 0061 §2): the change sets, newest first, and
/// what each one changed. Read-only; <see cref="IChangeRestore"/> takes one back.
/// </summary>
public interface IChangeJournal
{
    /// <summary>The newest <paramref name="limit"/> change sets, newest first, each
    /// with the number of documents it changed. With <paramref name="sessionId"/>,
    /// only the change sets an agent made in that session, its id matched without
    /// regard to case.</summary>
    Task<IReadOnlyList<ChangeSetEntry>> ListAsync(
        string? sessionId = null,
        int limit = 200,
        CancellationToken cancellationToken = default);

    /// <summary>The newest change sets that match every filter <paramref name="query"/>
    /// sets, newest first, each with the number of documents it changed: what
    /// <c>list_change_sets</c> answers an agent (local ADR 0061 §5).</summary>
    Task<IReadOnlyList<ChangeSetEntry>> QueryAsync(
        ChangeSetQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>The change set <paramref name="changeSetId"/>, with its document count,
    /// or null when the journal does not hold it.</summary>
    Task<ChangeSetEntry?> FindAsync(
        Guid changeSetId,
        CancellationToken cancellationToken = default);

    /// <summary>Every document <paramref name="changeSetId"/> changed, in the order it
    /// first touched them. Empty for a change set the journal does not hold.</summary>
    Task<IReadOnlyList<ChangedDocument>> ReadAsync(
        Guid changeSetId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Which change sets <see cref="IChangeJournal.QueryAsync"/> answers. Every filter set
/// must hold; one left null does not narrow.
/// </summary>
/// <param name="SessionId">Only the change sets made in this session, matched without
/// regard to case.</param>
/// <param name="Caller">Only the change sets this MCP caller made — a grant as
/// "name (id)", <c>unauthenticated</c> — matched without regard to case.</param>
/// <param name="Since">Only the change sets started at or after this instant.</param>
/// <param name="Until">Only the change sets started before this instant.</param>
/// <param name="Limit">The most change sets answered; at least one.</param>
public sealed record ChangeSetQuery(
    string? SessionId = null,
    string? Caller = null,
    DateTimeOffset? Since = null,
    DateTimeOffset? Until = null,
    int Limit = 200);

/// <summary>One change set as the journal lists it.</summary>
/// <param name="ChangeSet">Who made it, when, and under which label.</param>
/// <param name="DocumentCount">How many documents it changed.</param>
public sealed record ChangeSetEntry(ChangeSet ChangeSet, int DocumentCount);

/// <summary>
/// One document a change set changed, as text a person can read side by side.
/// </summary>
/// <param name="Kind">What the journal calls the document: <c>task</c>, today.</param>
/// <param name="Id">The document's id.</param>
/// <param name="Title">Its title now, or as it was when it is gone.</param>
/// <param name="Before">Its text before the change set first touched it — a task's
/// entry text, any other kind's fields one per line — or null when the change set
/// created it.</param>
/// <param name="Current">Its text now, in the same form, or null when the store no
/// longer holds it or cannot read its kind.</param>
public sealed record ChangedDocument(string Kind, string Id, string Title, string? Before, string? Current);
