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
/// <param name="Body">The rest of the task, in markdown, when the title is not
/// all of it — a prompt, a brief, the paragraph the one line is a summary of.
/// <para>
/// Markdown read by the parser this product already has, rather than a second
/// vocabulary belonging to this component. A task body is prose written by the
/// same person, in the same editor, as every other body here, and a row that
/// read it differently would be a row you have to write for.
/// </para>
/// <para>
/// Null is the ordinary case and costs nothing: a row without one renders
/// exactly the markup it rendered before bodies existed.
/// </para></param>
/// <param name="DependsOn">The ids this task waits on, in no particular order.
/// <para>
/// A list rather than one predecessor. A step that cannot start until two other
/// things are finished is the ordinary case, and asking which of the two is the
/// real predecessor is a question with no answer.
/// </para>
/// <para>
/// Only the ids are here. What they work out to — ready, blocked, in a cycle —
/// is <see cref="TaskChain"/>'s, because a row cannot see its siblings and so
/// cannot know whether the things it named are finished.
/// </para></param>
/// <param name="Status">Where the task has got to, as the word to show —
/// "Ready", "In progress". Already formatted, the same bargain
/// <paramref name="Due"/> makes: what a status is called, and in what language,
/// belongs to the host.
/// <para>
/// A badge beside the title rather than a part of the metadata line, because a
/// status is not the same kind of fact as a due date. The line below the title
/// says when the task happens and how far through it is; a status says what the
/// task currently <em>is</em>, which is what the title is doing — so it reads on
/// the title's line, exactly where <see cref="TaskPanel"/> puts it. One shape for
/// one fact in both places.
/// </para>
/// <para>
/// Null leaves the badge off. Not every list of tasks has a lifecycle worth
/// showing — a checklist of sub-items has none at all — and a row that drew an
/// empty badge would be claiming a state nobody set.
/// </para></param>
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
    IReadOnlyList<string>? Tags = null,
    string? Body = null,
    IReadOnlyList<string>? DependsOn = null,
    string? Status = null)
{
    public bool HasSteps => StepCount > 0;

    /// <summary>Whether there is more to this task than its title.</summary>
    public bool HasBody => !string.IsNullOrWhiteSpace(Body);

    public IReadOnlyList<string> TagList => Tags ?? [];

    /// <summary>The ids it waits on, or none. Never null, so a caller deriving a
    /// chain does not have to ask twice whether a row declared anything.</summary>
    public IReadOnlyList<string> DependsOnList => DependsOn ?? [];

    /// <summary>
    /// The metadata line, in the order a reader asks for it: where it came from,
    /// how far through it is, then when it has to happen.
    /// <para>
    /// Each part carries the glyph that says what kind of fact it is, because
    /// "Friday" and "09:00" side by side are two dates until something says one
    /// is a deadline and the other an alarm. Everything absent is left out
    /// rather than filled in.
    /// </para>
    /// <para>
    /// A row with a <see cref="Body"/> carries the note glyph whether or not it
    /// was asked to. That glyph already meant "there is more text here than the
    /// title", which is exactly what a body is — and a folded row that gave no
    /// sign of one would be a row whose disclosure is the only thing saying it
    /// is worth opening. Reusing the mark rather than minting a second one also
    /// keeps the line honest: to a reader those are the same fact, and two
    /// glyphs for it would read as two.
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
            Note || HasBody ? new TaskDetail(TaskDetailKind.Note, "Note") : null
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
    Note,

    /// <summary>What the row is waiting on, named. Derived from the list rather
    /// than set on the row, because no row can see the others.</summary>
    Blocked,

    /// <summary>The row is in a dependency cycle, so nothing will ever unblock
    /// it. Said out loud rather than left to be worked out from a chain that
    /// never advances.</summary>
    Cycle
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
        TaskDetailKind.Blocked => "⏳",
        // Deliberately not the repeat glyph. A chain that loops and a task that
        // recurs are opposite facts, and one mark for both would say a broken
        // chain is a schedule.
        TaskDetailKind.Cycle => "↻",
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
        TaskDetailKind.Blocked => "Waiting for",
        TaskDetailKind.Cycle => "Cycle",
        _ => "Note"
    };

    public string CssClass => $"task-item__detail task-item__detail--{Kind.ToString().ToLowerInvariant()}";
}
