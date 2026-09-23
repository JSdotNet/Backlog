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
/// <param name="Kind">What kind of thing this is — a prompt, a task, an idea, a test — as
/// the glyph that marks it and the word that names it. Already formatted, on the
/// same bargain as <paramref name="Status"/>: the vocabulary is the host's, and so
/// is the glyph, because this library ships no icon set and cannot know what a
/// "prompt" looks like to the product that has them.
/// <para>
/// Drawn as the glyph alone, with the word for anything that cannot see it. Every
/// other fact on the line shows its words because "Friday" is the fact and the
/// glyph only says what kind of fact it is; here the glyph <em>is</em> the fact —
/// three kinds, three marks, learned once — and the word beside it would say the
/// same thing twice on every row of a long list. The word is still there, in the
/// tooltip and in a visually-hidden span, so a screen reader hears "Type: prompt".
/// </para>
/// <para>
/// On the metadata line rather than beside the title, and that is the decision
/// worth stating, because <paramref name="Status"/> went the other way. A status
/// is what the task currently is and changes as work moves; a kind is what it was
/// filed as and does not. The panel this row opens into says the classification
/// on the strip below its heading, so the line below the title is the row's copy
/// of the same place. It is the first thing on that line, on every row, ahead of
/// what the task belongs to and of every fact about progress and time — see
/// <see cref="Details"/> for why the position never varies.
/// </para>
/// <para>
/// Null leaves it off. A checklist of steps has no kinds, and a row that marked
/// every step as a task would be a line about nothing.
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
    string? Status = null,
    TaskKind? Kind = null)
{
    public bool HasSteps => StepCount > 0;

    /// <summary>Whether there is more to this task than its title.</summary>
    public bool HasBody => !string.IsNullOrWhiteSpace(Body);

    public IReadOnlyList<string> TagList => Tags ?? [];

    /// <summary>The ids it waits on, or none. Never null, so a caller deriving a
    /// chain does not have to ask twice whether a row declared anything.</summary>
    public IReadOnlyList<string> DependsOnList => DependsOn ?? [];

    /// <summary>
    /// The metadata line, in the order a reader asks for it. Without a kind:
    /// where it came from, how far through it is, then when it has to happen.
    /// With one: what it is and what is under it — the mark, the step count, the
    /// note, one statement — then where it came from and when it has to happen.
    /// <para>
    /// The kind leads, and leads unconditionally — <see cref="TaskItem"/> keeps it
    /// ahead even of the chain's "waiting for", which otherwise opens the line.
    /// A mark is only a mark if it is always in the same place: a reader scanning
    /// a column for the prompts finds the glyph at the line's left edge on every
    /// row, or has to read every row to find it.
    /// </para>
    /// <para>
    /// The mark also stands in for the glyphs of the facts about what is
    /// <em>under</em> the title — the step count and the note. On a row with a
    /// kind those follow the mark directly, as words alone, "✨ 2 of 5 Note",
    /// because "prompt, with two of five steps done and a note" is one statement
    /// about one thing and three glyphs in a row made it read as three; the
    /// note moves up from the end of the line to say it beside the mark rather
    /// than stranded after a date. A due date or a wait is a different kind of
    /// fact and keeps its own glyph; "Friday" still needs something to say it is
    /// a deadline. A row with no kind — a step in a checklist — draws the
    /// content glyphs, in the places it always had them.
    /// </para>
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
    public IReadOnlyList<TaskDetail> Details
    {
        get
        {
            var steps = HasSteps ? new TaskDetail(TaskDetailKind.Steps, $"{StepsDone} of {StepCount}") { LedByKind = Kind is not null } : null;
            var note = Note || HasBody ? new TaskDetail(TaskDetailKind.Note, "Note") { LedByKind = Kind is not null } : null;

            // With a kind, the mark and the facts about what is under the title
            // are one statement and open the line together; without one, the
            // content facts keep the places they always had.
            TaskDetail?[] details = Kind is not null
                ?
                [
                    new TaskDetail(TaskDetailKind.Kind, Kind.Name) { KindGlyph = Kind.Glyph },
                    steps,
                    note,
                    Group is null ? null : new TaskDetail(TaskDetailKind.Group, Group),
                    InMyDay ? new TaskDetail(TaskDetailKind.MyDay, "My Day") : null,
                    Due is null ? null : new TaskDetail(TaskDetailKind.Due, Due),
                    Reminder is null ? null : new TaskDetail(TaskDetailKind.Reminder, Reminder),
                    Repeats ? new TaskDetail(TaskDetailKind.Repeat, RepeatLabel ?? "Repeats") : null
                ]
                :
                [
                    Group is null ? null : new TaskDetail(TaskDetailKind.Group, Group),
                    InMyDay ? new TaskDetail(TaskDetailKind.MyDay, "My Day") : null,
                    steps,
                    Due is null ? null : new TaskDetail(TaskDetailKind.Due, Due),
                    Reminder is null ? null : new TaskDetail(TaskDetailKind.Reminder, Reminder),
                    Repeats ? new TaskDetail(TaskDetailKind.Repeat, RepeatLabel ?? "Repeats") : null,
                    note
                ];

            return
            [
                .. details
                    .Where(detail => detail is not null && !string.IsNullOrWhiteSpace(detail.Text))
                    .Select(detail => detail!)
            ];
        }
    }

    /// <summary>How often it recurs, already said — "Weekly", "Every weekday".
    /// Null falls back to saying only that it does.</summary>
    public string? RepeatLabel { get; init; }
}

/// <summary>What kind of fact a metadata part is. The glyph follows from it, so
/// a caller cannot hand the row a date wearing an alarm clock.</summary>
public enum TaskDetailKind
{
    Group,

    /// <summary>What kind of thing the task is, in the host's glyph and word. The
    /// one detail drawn as its glyph alone, and the one whose glyph the host
    /// supplies — see <see cref="TaskKind"/>.</summary>
    Kind,

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
    /// everywhere.
    /// <para>
    /// Decided by the kind of fact, so a caller cannot hand the row a date
    /// wearing an alarm clock. The one exception is <see cref="TaskDetailKind.Kind"/>,
    /// whose values are the host's and so whose glyphs must be too: it reads
    /// <see cref="KindGlyph"/>, and that property means nothing on any other
    /// kind.
    /// </para></summary>
    public string Glyph => Kind switch
    {
        TaskDetailKind.Kind => KindGlyph ?? string.Empty,
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
        TaskDetailKind.Kind => "Type",
        TaskDetailKind.MyDay => "In My Day",
        TaskDetailKind.Steps => "Steps",
        TaskDetailKind.Due => "Due",
        TaskDetailKind.Reminder => "Reminder",
        TaskDetailKind.Repeat => "Repeats",
        TaskDetailKind.Blocked => "Waiting for",
        TaskDetailKind.Cycle => "Cycle",
        _ => "Note"
    };

    /// <summary>The glyph a <see cref="TaskDetailKind.Kind"/> detail draws, handed
    /// in with the row's <see cref="TaskKind"/>. Ignored by every other kind.</summary>
    public string? KindGlyph { get; init; }

    /// <summary>Whether the words are for the tooltip and the screen reader only.
    /// True for the kind alone: its glyph is the fact, and the word would repeat
    /// it on every row. Every other detail shows its words because the glyph only
    /// says what kind of fact they are.</summary>
    public bool GlyphOnly => Kind is TaskDetailKind.Kind;

    /// <summary>Whether this detail's glyph is spoken for by the kind mark ahead
    /// of it. Set by <see cref="TaskRow.Details"/> on the facts about what is
    /// under the title — steps, a note — when the row has a kind: they then read
    /// as words alone after the mark, and the name is still said for a screen
    /// reader. Never set on a fact about time or a wait, whose glyph is what tells
    /// "Friday" from "09:00".</summary>
    public bool LedByKind { get; init; }

    /// <summary>Whether the row draws this detail's glyph: it has one, and nothing
    /// ahead of it on the line has taken its place.</summary>
    public bool DrawsGlyph => Glyph.Length > 0 && !LedByKind;

    /// <summary>What hovering says. The name of the fact, as for every detail — and
    /// for one drawn as a glyph alone, the value too, because a tooltip reading
    /// "Type" over a mark the reader could not place would be the question
    /// asked back.</summary>
    public string Title => GlyphOnly ? $"{Name}: {Text}" : Name;

    public string CssClass => $"task-item__detail task-item__detail--{Kind.ToString().ToLowerInvariant()}";
}

/// <summary>
/// What kind of thing a task is, as the host draws and names it — the mark on
/// the row and the word behind it.
/// </summary>
/// <param name="Glyph">The mark. A text glyph, on the same terms as every other
/// glyph on the metadata line: this library ships no icon font.</param>
/// <param name="Name">The word — "prompt", "task", "idea", "test" — already in the host's
/// own casing and language. Said in the tooltip and to a screen reader, never
/// drawn beside the glyph.</param>
public sealed record TaskKind(string Glyph, string Name);
