namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>What a suggestion proposes to change about an item.</summary>
public enum InboxSuggestionKind
{
    /// <summary>Add a tag. <see cref="InboxSuggestionDto.Value"/> is the bare tag.</summary>
    Tag,

    /// <summary>Assign a repository. <see cref="InboxSuggestionDto.Value"/> is its
    /// <c>owner/name</c> id.</summary>
    Repository,

    /// <summary>Decide the item. <see cref="InboxSuggestionDto.Value"/> is the
    /// routing domain's slug: <c>tasks</c>, <c>devbook</c> or <c>archive</c>.</summary>
    Destination
}

/// <summary>
/// One thing Classification proposes for an item, which nothing applies until
/// the reader accepts it.
/// <para>
/// <see cref="Key"/> is what the reader turns down when they reject it —
/// <c>tag:sync</c>, <c>repository:owner/name</c>, <c>destination:tasks</c> — and
/// what keeps it from being offered for the same item again.
/// <see cref="Reason"/> says, in a sentence, why it was proposed, so a chip is
/// never a guess the reader has to take on trust.
/// </para>
/// <para>
/// <see cref="UnavailableReason"/> is set on a suggestion that can be read and
/// rejected but not accepted, because the act it would take is not built: a
/// suggestion to keep the item as knowledge, today. It is still offered, because
/// unavailability never hides a decision; it says why it cannot be taken.
/// </para>
/// </summary>
public sealed record InboxSuggestionDto(
    string Key,
    InboxSuggestionKind Kind,
    string Value,
    string Reason)
{
    public string? UnavailableReason { get; init; }

    public bool Available => UnavailableReason is null;
}
