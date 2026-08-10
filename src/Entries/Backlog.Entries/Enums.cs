namespace Backlog.Entries;

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

/// <summary>Completion state of a sub-item.</summary>
public enum SubItemStatus
{
    Pending,
    Done
}
