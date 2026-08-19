namespace Backlog.Modules.Backlog.Abstractions;

/// <summary>Classification of a backlog entry.</summary>
public enum EntryType
{
    Prompt,
    Task,
    Idea,
    FollowUp
}

/// <summary>Lifecycle state of a backlog entry.</summary>
public enum EntryStatus
{
    Draft,
    Ready,
    InProgress,
    Done,
    Archived
}

/// <summary>Ranking of a backlog entry.</summary>
public enum Priority
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Which of the two readings of an entry's body a reader last asked for: the
/// steps, or the markdown block they are written in.
/// <para>
/// A presentation preference, and deliberately not a fact about the work. It is
/// here rather than in a view-model because the entry carries it in its own text
/// — the markdown is canonical, so a preference kept in a sidecar would not
/// survive the file being shared and the reader who opened the entry from a clone
/// would get somebody else's default. See
/// <c>.design/content-editing.md#scheduling-and-dependency-tokens</c>.
/// </para>
/// <para>
/// Two members and no third. "Both at once" was the layout this replaced, and a
/// member for it would make the switch a three-state control whose middle state
/// is the thing the reader was trying to get away from.
/// </para>
/// </summary>
public enum EntryView
{
    /// <summary>The steps, as a list of rows.</summary>
    Steps,

    /// <summary>The body, as one markdown block.</summary>
    Notes
}

/// <summary>Completion state of a sub-item.</summary>
public enum SubItemStatus
{
    Pending,
    Done
}
