namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The ids the Tasks module's views register in the surface catalog, and the query
/// keys they accept (local ADR 0042 §1, §6). Tasks, Board and Calendar are three
/// views of one task list, so the module registers all three.
/// </summary>
public static class TasksSurfaceQuery
{
    /// <summary>The task list and its details.</summary>
    public const string SurfaceId = "tasks";

    /// <summary>The filtered tasks in columns of cards.</summary>
    public const string BoardId = "board";

    /// <summary>The filtered tasks on a month or a week by their due date.</summary>
    public const string CalendarId = "calendar";

    /// <summary><c>plan=&lt;tag&gt;</c>: open on that plan chip, with or without its
    /// <c>+</c> (local ADR 0042 §6). Tasks and Board.</summary>
    public const string Plan = "plan";

    /// <summary><c>entry=&lt;id&gt;</c>: open that entry in its editor, widening the
    /// window's scope to the entry's repository when it is outside it (local ADR 0042
    /// §6). Tasks only.</summary>
    public const string Entry = "entry";

    /// <summary><c>linked=&lt;id&gt;</c>, beside <see cref="Plan"/>: the task the roadmap
    /// item behind the plan links, listed whether or not it carries the tag — what the
    /// roadmap's "Open in Tasks" asks for (<see cref="TasksDesktopState.ShowPlan"/>).</summary>
    public const string Linked = "linked";

    /// <summary>The keys the Tasks view registers.</summary>
    public static IReadOnlyList<string> TasksKeys { get; } = [Plan, Linked, Entry];

    /// <summary>The keys the Board registers.</summary>
    public static IReadOnlyList<string> BoardKeys { get; } = [Plan, Linked];

    /// <summary>The query that opens <paramref name="planTag"/>'s work, listing
    /// <paramref name="linkedTaskId"/> beside it when given.</summary>
    public static IReadOnlyDictionary<string, string> ForPlan(string planTag, Guid? linkedTaskId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planTag);

        var query = new Dictionary<string, string>(StringComparer.Ordinal) { [Plan] = planTag };
        if (linkedTaskId is { } linked) query[Linked] = linked.ToString();
        return query;
    }

    /// <summary>The query that opens the entry <paramref name="entryId"/>.</summary>
    public static IReadOnlyDictionary<string, string> ForEntry(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);

        return new Dictionary<string, string>(StringComparer.Ordinal) { [Entry] = entryId };
    }
}
