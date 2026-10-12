using System.Globalization;

using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Mobile.UI.Tasks;

/// <summary>
/// The phone's three edits to a task, each made the way the desktop makes it: by
/// rewriting the task's entry text with the same <see cref="EntryTextParser"/>
/// rewrite the desktop's control uses, and reading the fields back off the
/// rewritten text.
/// <para>
/// The text is the task's source of truth on the desktop — its next save parses
/// the text and syncs the fields from it — so an edit that changed a field and
/// left the text saying otherwise would be undone there. Steps live in
/// <see cref="TaskPayload.ContentMd"/>, which is why a step tick rewrites the
/// body, and the sub-item list follows the parsed text by position, as the
/// desktop's save does.
/// </para>
/// <para>
/// Pure: no clock, no store. Each method answers with the edited document, or
/// null when the edit changes nothing — a task already done, a step already in
/// the state asked for — so nothing is queued for it.
/// </para>
/// </summary>
public static class TaskEntryRewrite
{
    private const string Done = "done";
    private const string Archived = "archived";

    /// <summary>
    /// Ticks the task off on <paramref name="today"/>, as the desktop's checkbox
    /// does: a task not yet done or archived also becomes <c>done</c>, its steps
    /// with it; one already done or archived keeps its status and is only
    /// ticked.
    /// </summary>
    public static TaskPayload? MarkDone(TaskPayload task, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(task);

        var raw = ToEntryText(task);
        var ticked = EntryTextParser.WithCompletedOn(raw, today);

        var rewritten = IsOver(task.Status)
            ? ticked
            : EntryTextParser.WithStatus(ticked, EntryStatus.Done, cascadeSubItems: true);

        return Apply(task, raw, rewritten);
    }

    /// <summary>Unticks the task: clears <c>completed_on</c> and leaves the status
    /// alone, the desktop's untick.</summary>
    public static TaskPayload? MarkUndone(TaskPayload task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var raw = ToEntryText(task);
        return Apply(task, raw, EntryTextParser.WithCompletedOn(raw, null));
    }

    /// <summary>
    /// Ticks or unticks one step, named by its id in <see cref="TaskPayload.SubItems"/>.
    /// The step's place in that list, by order, is its place in the text.
    /// </summary>
    public static TaskPayload? SetStepDone(TaskPayload task, Guid stepId, bool done)
    {
        ArgumentNullException.ThrowIfNull(task);

        var steps = task.SubItems.OrderBy(step => step.Order).ToList();
        var index = steps.FindIndex(step => step.Id == stepId);
        if (index < 0) return null;

        var raw = ToEntryText(task);
        var edited = Apply(task, raw, EntryTextParser.WithSubItemDone(raw, index, done)) ?? task;

        // The list is set as asked even where the text had no step at that
        // place to rewrite — a step the desktop added without text — so the
        // edit never quietly does nothing.
        var wanted = done ? Done : SubItemPending;
        var current = edited.SubItems.First(step => step.Id == stepId);
        if (!string.Equals(current.Status, wanted, StringComparison.OrdinalIgnoreCase))
        {
            edited = edited with
            {
                SubItems = [.. edited.SubItems.Select(step => step.Id == stepId ? step with { Status = wanted } : step)]
            };
        }

        return ReferenceEquals(edited, task) ? null : edited;
    }

    /// <summary>
    /// Picks the task for the day after <paramref name="today"/>, taking it out of
    /// today's My Day.
    /// <para>
    /// The move drops the task's agenda time, on the domain's rule
    /// (<c>.devbook/domain/tasks/domain.md#agenda-time</c>): the rewrite takes
    /// <c>at:</c> and <c>for:</c> off the text with the old date, and the
    /// document's two fields are cleared beside it. Both are needed because a
    /// synced task is rebuilt from the incoming document, so the rule never
    /// fires on the replica or the desktop — the document has to say so itself.
    /// </para>
    /// </summary>
    public static TaskPayload? MoveToTomorrow(TaskPayload task, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(task);

        var raw = ToEntryText(task);
        return Apply(task, raw, EntryTextParser.WithMyDay(raw, today.AddDays(1))) is { } moved
            ? moved with { AgendaAt = null, AgendaMinutes = null }
            : null;
    }

    /// <summary>
    /// The entry text the edits rewrite: the title, a metadata line carrying the
    /// tokens the edits read or write — status, the My Day pick and its agenda
    /// time, the tick — and the body. Not the whole canonical line: the fields it leaves off are
    /// never read back, so they cannot be lost.
    /// </summary>
    public static string ToEntryText(TaskPayload task)
    {
        ArgumentNullException.ThrowIfNull(task);

        // A status token this build has no name for is left off rather than
        // guessed: the status is then read back as unset, which keeps the
        // document's own token, and only a done edit writes one.
        var tokens = new List<string>();
        if (TryStatus(task.Status) is { } status) tokens.Add($"`!{EntryTextParser.StatusToken(status)}`");
        if (task.InMyDayOn is { } inMyDayOn)
        {
            tokens.Add($"`myday:{EntryTextParser.DateToken(inMyDayOn)}`");

            // Only beside the date, as the desktop writes it: an agenda time has
            // no day of its own.
            if (AgendaTime.FromWire(task.AgendaAt, task.AgendaMinutes) is { } agenda)
            {
                tokens.AddRange(EntryTextParser.AgendaTokens(agenda).Select(token => $"`{token}`"));
            }
        }

        if (task.CompletedOn is { } completedOn) tokens.Add($"`completed:{EntryTextParser.DateToken(completedOn)}`");

        // A line of one token at least, or the parse would read the body's first
        // line as the metadata.
        var meta = tokens.Count > 0 ? string.Join(' ', tokens) : "`task`";

        var body = (task.ContentMd ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        var title = string.IsNullOrWhiteSpace(task.Title) ? "Untitled" : task.Title.ReplaceLineEndings(" ");

        return body.Length == 0
            ? string.Create(CultureInfo.InvariantCulture, $"# {title}\n{meta}\n")
            : string.Create(CultureInfo.InvariantCulture, $"# {title}\n{meta}\n\n{body}\n");
    }

    private const string SubItemPending = "pending";

    /// <summary>The fields an edit can move, read back off the rewritten text.
    /// The body is taken only when the rewrite changed it, so a metadata-only
    /// edit leaves <see cref="TaskPayload.ContentMd"/> byte for byte. The agenda
    /// time's two fields are never read back: only a move changes it, and
    /// <see cref="MoveToTomorrow"/> clears them itself.</summary>
    private static TaskPayload? Apply(TaskPayload task, string raw, string rewritten)
    {
        if (string.Equals(raw, rewritten, StringComparison.Ordinal)) return null;

        var before = EntryTextParser.Parse(raw);
        var after = EntryTextParser.Parse(rewritten);
        var bodyChanged = !string.Equals(before.Body, after.Body, StringComparison.Ordinal);

        return task with
        {
            Status = after.Status is { } status ? EnumMap.ToWire(status) : task.Status,
            InMyDayOn = after.InMyDayOn,
            CompletedOn = after.CompletedOn,
            ContentMd = bodyChanged ? after.Body : task.ContentMd,
            SubItems = bodyChanged ? SyncSteps(task.SubItems, after.SubItems) : task.SubItems,
        };
    }

    /// <summary>The steps' statuses as the rewritten text says them, by position —
    /// the desktop save's own sync. Titles, notes and ids are kept; a step the
    /// text has no place for is left as it was.</summary>
    private static IReadOnlyList<SubItemPayload> SyncSteps(
        IReadOnlyList<SubItemPayload> steps,
        IReadOnlyList<EntryTextParser.ParsedSubItem> parsed)
    {
        var ordered = steps.OrderBy(step => step.Order).ToList();

        return
        [
            .. steps.Select(step =>
            {
                var index = ordered.IndexOf(step);
                if (index >= parsed.Count) return step;

                var status = parsed[index].Done ? Done : SubItemPending;
                return string.Equals(step.Status, status, StringComparison.OrdinalIgnoreCase) ? step : step with { Status = status };
            })
        ];
    }

    private static EntryStatus? TryStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;

        try
        {
            return EnumMap.ParseStatus(status);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsOver(string status) =>
        string.Equals(status, Done, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, Archived, StringComparison.OrdinalIgnoreCase);
}
