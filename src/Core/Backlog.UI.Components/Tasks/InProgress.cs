using Backlog.UI.Components.Integrations;

namespace Backlog.UI.Components.Tasks;

/// <summary>
/// One task as the In progress view draws it: its facts on the left — status, plan,
/// repository, sub-items, started, due, effort — and the sessions and pull requests
/// linked to it beside them.
/// <para>
/// Every fact is the host's string, already formatted, because the library holds no
/// clock and no calendar: "Started Oct 1", "Due Oct 8", "5 pts". A fact the task does
/// not have is null and is not drawn.
/// </para>
/// </summary>
/// <param name="Id">The host's id for the task, handed back when its title is pressed.</param>
/// <param name="Colour">The repository identity hue, or null — the host's answer,
/// null while its repository colours are hidden.</param>
/// <param name="Finished">Done today; drawn only while "Include finished today" is on,
/// in a section of its own.</param>
public sealed record InProgressTask(
    string Id,
    string Title,
    IReadOnlyList<WorkSession> Sessions,
    IReadOnlyList<WorkPullRequest> PullRequests,
    string Status = "In progress",
    string? Plan = null,
    string? Repository = null,
    int? Colour = null,
    string? SubItems = null,
    string? Started = null,
    string? Due = null,
    bool Overdue = false,
    string? Effort = null,
    bool Finished = false)
{
    /// <summary>Whether anything linked to the task asks for the person — see
    /// <see cref="WorkStates.NeedsYou(WorkSession)"/> and
    /// <see cref="WorkStates.NeedsYou(WorkPullRequest)"/>.</summary>
    public bool NeedsYou => Sessions.Any(WorkStates.NeedsYou) || PullRequests.Any(WorkStates.NeedsYou);
}

/// <summary>
/// A running session or an open pull request no task links: what the "Not linked to
/// a task" section lists, each with "Link to a task…". Exactly one of the two is set.
/// </summary>
/// <param name="Repository">Where it lives, as the host names a repository, or null.</param>
/// <param name="Colour">The repository identity hue, or null.</param>
public sealed record InProgressLoose(
    WorkSession? Session = null,
    WorkPullRequest? PullRequest = null,
    string? Repository = null,
    int? Colour = null)
{
    /// <summary>The host's key for the item: the session id, or the pull request key.</summary>
    public string Key => Session?.Id ?? PullRequest?.Key ?? string.Empty;
}

/// <summary>Which of the view's sections a group of tasks is.</summary>
public enum InProgressSectionKind
{
    /// <summary>A linked session is quiet or waiting, or a linked pull request has
    /// a failing check or requested changes.</summary>
    NeedsYou,

    /// <summary>Everything in progress that does not need the person.</summary>
    Moving,

    /// <summary>Every task in progress in the host's order, while "Needs you first"
    /// is off.</summary>
    All,

    /// <summary>Done today, while "Include finished today" is on.</summary>
    FinishedToday
}

/// <summary>One section of the In progress view, in the order it is drawn.</summary>
public sealed record InProgressSection(InProgressSectionKind Kind, string Name, string Hint, IReadOnlyList<InProgressTask> Tasks);

/// <summary>
/// The section rules of the In progress view, apart from any rendering so the host
/// and its tests read the same answer.
/// <list type="bullet">
/// <item>With "Needs you first" on: <b>Needs you</b> — a task whose linked session
/// is quiet or waiting, or whose linked pull request has a failing check or requested
/// changes — then <b>Moving</b>, the rest.</item>
/// <item>With it off: one <b>In progress</b> section, in the host's order.</item>
/// <item>With "Include finished today" on, the tasks done today follow in
/// <b>Finished today</b>; off, they are left out.</item>
/// </list>
/// Each section keeps the host's order within it, and a section with no task in it is
/// left out — except Needs you and Moving, which say "nothing" in words when the
/// other has tasks, so a reader can tell an empty Needs you from a missing one.
/// The "Not linked to a task" section is not a task section and the view draws it
/// itself, last.
/// </summary>
public static class InProgressSections
{
    public const string NeedsYouName = "Needs you";
    public const string MovingName = "Moving";
    public const string AllName = "In progress";
    public const string FinishedTodayName = "Finished today";
    public const string LooseName = "Not linked to a task";

    public static IReadOnlyList<InProgressSection> Arrange(
        IEnumerable<InProgressTask> tasks,
        bool needsYouFirst,
        bool includeFinishedToday)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var all = tasks.ToList();
        var open = all.Where(task => !task.Finished).ToList();
        var sections = new List<InProgressSection>();

        if (needsYouFirst)
        {
            var needs = open.Where(task => task.NeedsYou).ToList();
            var moving = open.Where(task => !task.NeedsYou).ToList();

            if (open.Count > 0)
            {
                sections.Add(new(InProgressSectionKind.NeedsYou, NeedsYouName,
                    "A session has gone quiet, or a pull request has a failing check or requested changes.", needs));
                sections.Add(new(InProgressSectionKind.Moving, MovingName,
                    "Running, or waiting on nobody.", moving));
            }
        }
        else if (open.Count > 0)
        {
            sections.Add(new(InProgressSectionKind.All, AllName, "In the order the task list keeps them.", open));
        }

        if (includeFinishedToday)
        {
            var finished = all.Where(task => task.Finished).ToList();
            if (finished.Count > 0)
            {
                sections.Add(new(InProgressSectionKind.FinishedToday, FinishedTodayName, "Done today.", finished));
            }
        }

        return sections;
    }
}
