namespace Backlog.UI.Components.Tasks;

/// <summary>
/// One row in a task list, as <see cref="TaskListView"/> needs to know it.
/// <para>
/// A task, not an entry: no area, no repository, no sub-items, no status
/// vocabulary. What is here is what a list of things-to-do has anywhere — is it
/// done, does it matter, when is it due, how far through is it — so the shape
/// can be worn by a backlog entry, a checklist item, or a step under either.
/// </para>
/// </summary>
/// <param name="Id">Unique within the list, and what every callback reports.</param>
/// <param name="Title">The line the reader is here for.</param>
/// <param name="Done">Whether it is finished.</param>
/// <param name="Important">The star. A second axis from done, because the thing
/// people star is usually the thing they have not done.</param>
/// <param name="InMyDay">Picked for today. Deliberately not a due date: it is a
/// decision the reader made this morning, and it expires on its own.</param>
/// <param name="Group">The quiet line under the title — which list it came from,
/// a folder, a source.</param>
/// <param name="Due">When it is due, already formatted. What "tomorrow" is, and
/// what language it is said in, belongs to the host.</param>
/// <param name="Reminder">A set reminder, already formatted.</param>
/// <param name="Repeats">Whether it recurs.</param>
/// <param name="StepsDone">How many steps are finished, when it has steps.</param>
/// <param name="StepCount">How many steps there are. Zero means it has none, and
/// the counter is left off rather than reading "0 of 0".</param>
/// <param name="Note">Whether a note hangs off it. The note itself is the host's
/// to show; this only says the paperclip belongs on the row.</param>
/// <param name="Tags">What it is filed under. Rendered as chips rather than as
/// more of the metadata line: a tag is a thing you click to find its siblings,
/// and the rest of that line is text about this one task.</param>
public sealed record TaskRow(
    string Id,
    string Title,
    bool Done = false,
    bool Important = false,
    bool InMyDay = false,
    string? Group = null,
    string? Due = null,
    string? Reminder = null,
    bool Repeats = false,
    int StepsDone = 0,
    int StepCount = 0,
    bool Note = false,
    IReadOnlyList<string>? Tags = null)
{
    public bool HasSteps => StepCount > 0;

    public IReadOnlyList<string> TagList => Tags ?? [];

    /// <summary>
    /// The metadata line, in the order a reader asks for it: where it came from,
    /// how far through it is, then when it has to happen.
    /// <para>
    /// Each part carries the glyph that says what kind of fact it is, because
    /// "Friday" and "09:00" side by side are two dates until something says one
    /// is a deadline and the other an alarm. Everything absent is left out
    /// rather than filled in.
    /// </para>
    /// </summary>
    public IReadOnlyList<TaskDetail> Details =>
    [
        .. new TaskDetail?[]
        {
            Group is null ? null : new TaskDetail(TaskDetailKind.Group, Group),
            InMyDay ? new TaskDetail(TaskDetailKind.MyDay, "My Day") : null,
            HasSteps ? new TaskDetail(TaskDetailKind.Steps, $"{StepsDone} of {StepCount}") : null,
            Due is null ? null : new TaskDetail(TaskDetailKind.Due, Due),
            Reminder is null ? null : new TaskDetail(TaskDetailKind.Reminder, Reminder),
            Repeats ? new TaskDetail(TaskDetailKind.Repeat, RepeatLabel ?? "Repeats") : null,
            Note ? new TaskDetail(TaskDetailKind.Note, "Note") : null
        }
        .Where(detail => detail is not null && !string.IsNullOrWhiteSpace(detail.Text))
        .Select(detail => detail!)
    ];

    /// <summary>How often it recurs, already said — "Weekly", "Every weekday".
    /// Null falls back to saying only that it does.</summary>
    public string? RepeatLabel { get; init; }
}

/// <summary>What kind of fact a metadata part is. The glyph follows from it, so
/// a caller cannot hand the row a date wearing an alarm clock.</summary>
public enum TaskDetailKind
{
    Group,
    MyDay,
    Steps,
    Due,
    Reminder,
    Repeat,
    Note
}

/// <summary>One part of a row's metadata line.</summary>
public sealed record TaskDetail(TaskDetailKind Kind, string Text)
{
    /// <summary>The glyph. Text rather than an icon set, because this library
    /// ships no icon font and one component needing one would need it
    /// everywhere.</summary>
    public string Glyph => Kind switch
    {
        TaskDetailKind.MyDay => "☀",
        TaskDetailKind.Steps => "≡",
        TaskDetailKind.Due => "🗓",
        TaskDetailKind.Reminder => "⏰",
        TaskDetailKind.Repeat => "🔁",
        TaskDetailKind.Note => "📝",
        _ => string.Empty
    };

    /// <summary>What the glyph means, for anything that cannot see it. The glyph
    /// itself is aria-hidden — a screen reader announcing "alarm clock emoji"
    /// says less than "Reminder".</summary>
    public string Name => Kind switch
    {
        TaskDetailKind.Group => "List",
        TaskDetailKind.MyDay => "In My Day",
        TaskDetailKind.Steps => "Steps",
        TaskDetailKind.Due => "Due",
        TaskDetailKind.Reminder => "Reminder",
        TaskDetailKind.Repeat => "Repeats",
        _ => "Note"
    };

    public string CssClass => $"task-item__detail task-item__detail--{Kind.ToString().ToLowerInvariant()}";
}
