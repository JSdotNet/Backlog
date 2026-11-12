namespace Backlog.Modules.Progress.UI.WeeklyReview;

/// <summary>
/// What the weekly review reads, in Progress's own words: the shell gathers it from the
/// Inbox, the Brain, the task list, the roadmap and the goals
/// (<see cref="IWeeklyReviewSource"/>), because Progress UI may reach none of those
/// modules. <see cref="WeeklyReviewChecklistBuilder"/> turns it into the checklist.
/// </summary>
/// <param name="Inbox">Every Inbox item still in the queue or put aside to a date.</param>
/// <param name="Brain">What the Brain holds, or null when no Brain is readable here.</param>
/// <param name="Entries">Every backlog entry in the window's scope.</param>
/// <param name="PlanTags">The plan tags the roadmap carries.</param>
/// <param name="Goals">Every goal, or null when this host composes none.</param>
public sealed record WeeklyReviewInputs(
    IReadOnlyList<ReviewInboxItem> Inbox,
    ReviewBrain? Brain,
    IReadOnlyList<ReviewEntry> Entries,
    IReadOnlyList<string> PlanTags,
    IReadOnlyList<ReviewGoal>? Goals)
{
    /// <summary>Nothing to review: no source composed.</summary>
    public static WeeklyReviewInputs None { get; } = new([], null, [], [], null);
}

/// <summary>An Inbox item. <paramref name="Deferred"/> is an item put aside to
/// <paramref name="DeferredUntil"/>; otherwise it is waiting in the queue.</summary>
public sealed record ReviewInboxItem(Guid Id, string Title, bool Deferred = false, DateOnly? DeferredUntil = null);

/// <summary>Where an entry stands, as far as the review is concerned.</summary>
public enum ReviewEntryStatus
{
    Draft,
    Ready,
    InProgress,

    /// <summary>Done or archived: never a finding.</summary>
    Finished,
}

/// <summary>A backlog entry.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Title">Its title.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="PlanTag">The roadmap plan it belongs to, bare, or null.</param>
/// <param name="WaitingFor">Who it waits on outside the backlog, or null.</param>
/// <param name="FollowUpOn">When to chase <paramref name="WaitingFor"/>.</param>
/// <param name="DueOn">Its due date.</param>
/// <param name="Blocked">Marked blocked by hand.</param>
/// <param name="DependenciesDone">Every entry it depends on is finished.</param>
/// <param name="LastActivity">The latest of the days it was created, started or
/// completed, when any is known.</param>
public sealed record ReviewEntry(
    Guid Id,
    string Title,
    ReviewEntryStatus Status,
    string? PlanTag = null,
    string? WaitingFor = null,
    DateOnly? FollowUpOn = null,
    DateOnly? DueOn = null,
    bool Blocked = false,
    bool DependenciesDone = true,
    DateOnly? LastActivity = null)
{
    /// <summary>Not done and not archived.</summary>
    public bool IsOpen => Status != ReviewEntryStatus.Finished;

    /// <summary>
    /// A next action, as the skill defines one: open, ready or in progress, waiting on
    /// nobody, not blocked, and every entry it depends on done.
    /// </summary>
    public bool IsNextAction =>
        Status is ReviewEntryStatus.Ready or ReviewEntryStatus.InProgress && CanMove;

    /// <summary>Nothing holds it: no waiting-for, not blocked, its dependencies done.</summary>
    public bool CanMove => WaitingFor is null && !Blocked && DependenciesDone;
}

/// <summary>The Brain, as the review reads it.</summary>
/// <param name="Areas">Which of its folders are the areas the review looks in.</param>
/// <param name="Notes">Every note, with the plan tags and task ids its meta block
/// names.</param>
/// <param name="Due">The notes whose review date has come.</param>
/// <param name="Unreadable">The notes whose review value is no date.</param>
/// <param name="ToArchive">The project notes of finished plans still in the projects
/// area.</param>
public sealed record ReviewBrain(
    ReviewBrainAreas Areas,
    IReadOnlyList<ReviewNote> Notes,
    IReadOnlyList<ReviewDueNote> Due,
    IReadOnlyList<ReviewDueNote> Unreadable,
    IReadOnlyList<ReviewNote> ToArchive);

/// <summary>The folders of the Brain's areas the review looks in, or null for one the
/// Brain does not have. <paramref name="HasArchive"/> says whether a note can be moved to
/// the archive.</summary>
public sealed record ReviewBrainAreas(
    string? Inbox,
    string? Projects,
    string? Someday,
    string? Areas,
    bool HasArchive);

/// <summary>A Brain note, by its path from the Brain's root.</summary>
public sealed record ReviewNote(
    string Path,
    string Title,
    IReadOnlyList<string>? RoadmapTags = null,
    IReadOnlyList<Guid>? TaskIds = null)
{
    /// <summary>The area folder the note sits in: its path's first segment.</summary>
    public string Folder => Path.Replace('\\', '/').Split('/', 2)[0];

    /// <summary>Whether the note sits in <paramref name="folder"/>, and not at the
    /// Brain's root.</summary>
    public bool IsIn(string? folder) =>
        folder is { Length: > 0 }
        && Path.Replace('\\', '/').Contains('/')
        && string.Equals(Folder, folder, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A note the review date reader answered for: its date, or why its value is
/// none.</summary>
public sealed record ReviewDueNote(string Path, string Title, string Detail);

/// <summary>A goal, as the Goals view counts it.</summary>
public sealed record ReviewGoal(Guid Id, string Title, DateOnly? DueDate, int Open, int Blocked);
