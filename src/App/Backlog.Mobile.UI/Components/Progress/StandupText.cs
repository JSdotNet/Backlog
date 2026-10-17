using System.Globalization;

using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.UI.Components.PhoneStandup;
using Backlog.UI.Components.Progress;

namespace Backlog.Mobile.UI.Components.Progress;

/// <summary>
/// What the phone's Stand-up, goal and impediment pages share
/// (<c>.devbook/domain/progress/features.md#goals-on-the-phone</c>): the Progress module's
/// words mapped onto the component library's, the pull request statuses a desktop last
/// sent looked up by key, and the few lines the pages print. It decides nothing about the
/// work — the counts and the windows are the module's rules, read by
/// <see cref="Mobile.UI.Progress.PhoneStandUp"/> and <see cref="GoalItems"/>.
/// </summary>
public static class StandupText
{
    /// <summary>The address of the Stand-up page, relative to the base.</summary>
    public const string StandupHref = "standup";

    /// <summary>A goal's own page.</summary>
    public static string GoalHref(Guid id) => $"standup/goals/{id}";

    /// <summary>An impediment's own page.</summary>
    public static string ImpedimentHref(Guid id) => $"standup/impediments/{id}";

    /// <summary>The module's standing as the library draws it.</summary>
    public static StandupStanding Standing(GoalStanding standing) =>
        standing == GoalStanding.AtRisk ? StandupStanding.AtRisk : StandupStanding.OnTrack;

    /// <summary>The module's impediment state as the library draws it.</summary>
    public static StandupImpedimentState State(ImpedimentState state) => state switch
    {
        ImpedimentState.Reported => StandupImpedimentState.Reported,
        ImpedimentState.Escalated => StandupImpedimentState.Escalated,
        ImpedimentState.BeingHandled => StandupImpedimentState.BeingHandled,
        ImpedimentState.Resolved => StandupImpedimentState.Resolved,
        _ => StandupImpedimentState.New
    };

    /// <summary>The library's impediment state as the module names it.</summary>
    public static ImpedimentState State(StandupImpedimentState state) => state switch
    {
        StandupImpedimentState.Reported => ImpedimentState.Reported,
        StandupImpedimentState.Escalated => ImpedimentState.Escalated,
        StandupImpedimentState.BeingHandled => ImpedimentState.BeingHandled,
        StandupImpedimentState.Resolved => ImpedimentState.Resolved,
        _ => ImpedimentState.New
    };

    /// <summary>An impediment's history as the library's steps.</summary>
    public static IReadOnlyList<StandupImpedimentStep> History(ImpedimentDto impediment)
    {
        ArgumentNullException.ThrowIfNull(impediment);
        return [.. impediment.History.Select(step => new StandupImpedimentStep(State(step.State), step.On, step.With))];
    }

    /// <summary>
    /// A window as the stand-up prints it: one day as "16 Oct", a span inside one month as
    /// "12–16 Oct", and one across two as "28 Sep–2 Oct".
    /// </summary>
    public static string Window(IterationWindowDto window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.First == window.Last) return StandupWords.Day(window.First);

        var first = window.First.Month == window.Last.Month && window.First.Year == window.Last.Year
            ? window.First.Day.ToString(CultureInfo.InvariantCulture)
            : StandupWords.Day(window.First);

        return $"{first}–{StandupWords.Day(window.Last)}";
    }

    /// <summary>The length the switch offers it as.</summary>
    public static string Length(IterationLength length) => length switch
    {
        IterationLength.Day => "Day",
        IterationLength.TwoWeeks => "2 weeks",
        IterationLength.Month => "Month",
        _ => "Week"
    };

    /// <summary>The span the person's pace is read over, for the planned-points line:
    /// "a week", "a day", "in two weeks", "a month".</summary>
    public static string PaceSpan(IterationLength length) => length switch
    {
        IterationLength.Day => "a day",
        IterationLength.TwoWeeks => "in two weeks",
        IterationLength.Month => "a month",
        _ => "a week"
    };

    /// <summary>"1 day", "3 days".</summary>
    public static string Days(int days) => days == 1 ? "1 day" : $"{days.ToString(CultureInfo.InvariantCulture)} days";

    /// <summary>"1 working day", "3 working days".</summary>
    public static string WorkingDays(int days) =>
        days == 1 ? "1 working day" : $"{days.ToString(CultureInfo.InvariantCulture)} working days";

    /// <summary>"5 pts".</summary>
    public static string Points(int points) => $"{points.ToString(CultureInfo.InvariantCulture)} pts";

    /// <summary>The statuses a desktop last sent, by <see cref="GoalItems.Key"/>.</summary>
    public static Dictionary<string, GoalPullRequestDto> Statuses(StandUpContextDto? context)
    {
        var statuses = new Dictionary<string, GoalPullRequestDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var pull in context?.PullRequests ?? []) statuses.TryAdd(GoalItems.Key(pull.Repository, pull.Number), pull);
        return statuses;
    }

    /// <summary>The statuses in the words <see cref="GoalItems.Items"/> reads.</summary>
    public static IReadOnlyList<LinkedPullRequestDto> Linked(StandUpContextDto? context) =>
    [
        .. (context?.PullRequests ?? [])
            .Select(pull => LinkedPullRequestDto.Of(pull))
    ];

    /// <summary>What a desktop last said about <paramref name="pull"/>, or that nothing has
    /// read it yet.</summary>
    public static GoalPullRequestDto StatusOf(Dictionary<string, GoalPullRequestDto> statuses, PullRequestRefDto pull)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        ArgumentNullException.ThrowIfNull(pull);

        return statuses.TryGetValue(GoalItems.KeyOf(pull), out var status) ? status : GoalPullRequestDto.Unread(pull);
    }

    /// <summary>The pull request a stand-up item names: its own for a pull request, else the
    /// first its task links; null for a task that links none.</summary>
    public static PullRequestRefDto? PullRequestOf(StandUpItemDto item, IReadOnlyDictionary<Guid, ProgressTaskDto> work)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(work);

        if (item.Kind == GoalAttachmentKind.PullRequest && item.Repository is { } repository && item.Number is { } number)
        {
            return new PullRequestRefDto(repository, number.ToString(CultureInfo.InvariantCulture));
        }

        return item.TaskId is { } id && work.TryGetValue(id, out var task) ? task.PullRequests.FirstOrDefault() : null;
    }

    /// <summary>
    /// A plan's tag as a heading reads it — "bank-integration" as "Bank integration" — since
    /// no plan title reaches the phone: the sigil dropped, hyphens and underscores as
    /// spaces, the first letter capitalised.
    /// </summary>
    public static string PlanTitle(string tag)
    {
        var words = string.Join(' ', GoalItems.Bare(tag).Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries));
        return words.Length == 0 ? tag : char.ToUpper(words[0], CultureInfo.InvariantCulture) + words[1..];
    }

    /// <summary>A pull request's title as the phone knows it: the title of a synced task that
    /// links it, or null — the statuses a desktop sends carry no title.</summary>
    public static string? PullRequestTitle(PullRequestRefDto pull, IEnumerable<ProgressTaskDto> work)
    {
        ArgumentNullException.ThrowIfNull(pull);
        ArgumentNullException.ThrowIfNull(work);

        var key = GoalItems.KeyOf(pull);
        return work.FirstOrDefault(task => task.PullRequests.Any(linked => string.Equals(GoalItems.KeyOf(linked), key, StringComparison.OrdinalIgnoreCase)))?.Title;
    }

    /// <summary>A pull request's state as one lower-case word — "open", "draft", "merged",
    /// "closed" or "unknown" — which is also the slug of the <c>gh</c> badge its pill wears.</summary>
    public static string Word(LinkedPullRequestState state) => state switch
    {
        LinkedPullRequestState.Merged => "merged",
        LinkedPullRequestState.Open => "open",
        LinkedPullRequestState.Draft => "draft",
        LinkedPullRequestState.Closed => "closed",
        _ => "unknown"
    };

    /// <summary>
    /// What a desktop last said about a pull request, as the shared status line draws it
    /// (<see cref="PullRequestStatusModel"/>): its number, its state word, its checks and its
    /// approvals against what the base branch requires. The phone has no address for it, so
    /// the pill leads nowhere; <paramref name="title"/>, when a synced task gives one, is the
    /// pill's tooltip.
    /// </summary>
    public static PullRequestStatusModel StatusModel(GoalPullRequestDto pull, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(pull);

        return new PullRequestStatusModel(
            int.TryParse(Number(pull.Number), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0,
            Word(pull.State),
            pull.Checks is { } checks ? new PullRequestChecks(checks.Passed, checks.Failed, checks.Pending) : null,
            pull.Approvals is { } approvals ? new PullRequestApprovals(approvals.Received, approvals.Required) : null,
            title);
    }

    /// <summary>A pull request's number without its <c>#</c>.</summary>
    public static string Number(string number) => number.Trim().TrimStart('#');
}
