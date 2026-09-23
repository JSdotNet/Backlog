namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>Classification of a backlog entry.</summary>
public enum EntryType
{
    Prompt,
    Task,
    Idea,

    /// <summary>A check a person runs by hand against work already done.</summary>
    Test
}

/// <summary>
/// Which half of the entry-text grammar a parsed segment belongs to.
/// <para>
/// The bare type word says what an entry <em>is</em>, and four of the five
/// words it may be — <c>prompt</c>, <c>task</c>, <c>idea</c>, <c>test</c> —
/// classify a task and live in <see cref="EntryType"/>. All four are
/// <see cref="Task"/> here: the kind is not a fifth type, it is which thing the
/// segment describes. The fifth word, <c>plan</c>, describes a roadmap item, which is not a task and
/// has no row in <see cref="EntryType"/> — so the kind is read off the same
/// token and kept beside the type rather than inside it, and a <c>plan</c>
/// segment carries no <see cref="EntryType"/> at all.
/// </para>
/// <para>
/// Import is the one way a <c>plan</c> segment may be acted on; every other
/// path that turns entry text into a task refuses it. See
/// <c>.arc42/adr/0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md</c>
/// and <c>.design/content-editing.md#structured-metadata-sigils</c>.
/// </para>
/// </summary>
public enum EntryKind
{
    Task,
    Plan
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
