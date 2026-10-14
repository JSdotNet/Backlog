using System.Globalization;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.UI.Components.Progress;

namespace Backlog.Modules.Progress.UI.InProgress;

/// <summary>
/// Writes the sprint view's Since yesterday strip
/// (<c>.devbook/domain/progress/features.md#the-daily-strip</c>) from one sprint reading:
/// every sentence a fixed template filled with the reading's members, dates and counts,
/// never written by a model. Pure, so its tests need no module behind it.
/// <para>
/// "Since yesterday" is since the previous working day — Friday on a Monday — up to the
/// day of the reading, both inclusive. A member's done, started and joined days and a
/// pull request's opened day are the dates that place it there; a member with no such
/// date is never news. A pull request's opened day is the day it was created, so a draft
/// marked ready for review later is not reported as opened for review then.
/// </para>
/// </summary>
public static class SprintDailyMapper
{
    /// <summary>The strip for <paramref name="progress"/>, a sprint of
    /// <paramref name="series"/> — or null when it is not drawn: the series does not show
    /// in the daily brief, the sprint is not the open one, or the reading has no day.</summary>
    public static SprintDailyModel? ToModel(SprintProgressDto progress, SprintSeriesDto series)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(series);

        if (!series.ShowInDailyBrief || !progress.IsOpen || progress.ReadOn is not { } today) return null;

        var since = PreviousWorkingDay(today);
        bool Recent(DateOnly? day) => day is { } on && on >= since && on <= today;

        var days = Math.Max(0, progress.WorkingDays);
        var elapsed = Math.Clamp(progress.WorkingDaysElapsed, 0, days);
        var left = days - elapsed;
        var goalIds = progress.GoalTaskIds.ToHashSet();
        var landing = elapsed == 0 ? 0 : (int)Math.Round(progress.DonePerWorkingDay * left, MidpointRounding.AwayFromZero);
        var goalLanding = elapsed == 0 ? 0 : (int)Math.Round((double)progress.Goal.Done / elapsed * left, MidpointRounding.AwayFromZero);

        var blockedGoal = progress.Members
            .Where(member => member.State is SprintWorkState.Blocked && goalIds.Contains(member.TaskId))
            .Select(member => member.Title)
            .ToList();
        var goalTotal = progress.Goal.Total;
        var needed = Math.Max(0, (int)Math.Ceiling(goalTotal * progress.GoalShare / 100.0) - progress.Goal.Done);
        var atRisk = !progress.GoalMet && goalTotal > 0
            && (blockedGoal.Count > 0 || (elapsed > 0 && (left == 0 || needed > goalLanding)));

        var done = progress.Members.Where(member => member.State is SprintWorkState.Done && Recent(member.DoneOn)).ToList();
        var sinceLines = SinceYesterday(progress, done, Recent, since, today);

        return new SprintDailyModel(
            Label(progress, today, elapsed, days),
            Headline(progress, done.Count, since, today, atRisk),
            Summary(progress, left),
            sinceLines,
            OnToday(progress),
            Blocked(progress),
            $"{GoalSentence(progress, blockedGoal, atRisk)} {AllWorkSentence(progress, elapsed, days, left, landing)}",
            atRisk);
    }

    /// <summary>The working day before <paramref name="day"/>: the day before, or Friday
    /// from a Saturday, a Sunday or a Monday.</summary>
    public static DateOnly PreviousWorkingDay(DateOnly day)
    {
        var previous = day.AddDays(-1);
        while (previous.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) previous = previous.AddDays(-1);
        return previous;
    }

    private static string Label(SprintProgressDto progress, DateOnly today, int elapsed, int days) =>
        elapsed == 0
            ? $"Daily · {Day(today)} · {progress.Name}, starts {Day(progress.Start)}"
            : $"Daily · {Day(today)} · {progress.Name}, day {Number(elapsed)} of {Number(days)}";

    private static string Summary(SprintProgressDto progress, int left) =>
        $"{progress.SeriesName} · goal {Number(progress.Goal.Done)} of {Number(progress.Goal.Total)} · all work {Number(progress.AllWork.Done)} of {Number(progress.AllWork.Total)} · "
        + (left == 0 ? "the last working day" : $"{Count(left, "working day", "working days")} left");

    /// <summary>"2 items landed yesterday; Openspec has been blocked for 2 working days."</summary>
    private static string Headline(SprintProgressDto progress, int landed, DateOnly since, DateOnly today, bool atRisk)
    {
        var when = When(since, today);
        var first = landed == 0 ? $"Nothing landed {when}" : $"{Count(landed, "item", "items")} landed {when}";

        var blocked = progress.Members
            .Where(member => member.State is SprintWorkState.Blocked)
            .OrderBy(member => member.BlockedSince ?? DateOnly.MaxValue)
            .ToList();

        string? second;
        if (blocked.Count > 0)
        {
            var longest = blocked[0];
            second = longest.BlockedSince is { } on
                ? WorkingDaysAfter(on, today) is var stuck and > 0
                    ? $"{longest.Title} has been blocked for {Count(stuck, "working day", "working days")}"
                    : $"{longest.Title} was blocked today"
                : $"{longest.Title} is blocked";

            var more = blocked.Count - 1;
            if (more > 0) second += $" and {Count(more, "more task is", "more tasks are")} blocked";
        }
        else
        {
            second = progress.Goal.Total == 0
                ? null
                : progress.GoalMet ? "the goal is met" : atRisk ? "the goal is at risk" : "the goal is on track";
        }

        return second is null ? $"{first}." : $"{first}; {second}.";
    }

    private static IReadOnlyList<SprintDailyLine> SinceYesterday(
        SprintProgressDto progress,
        IReadOnlyList<SprintMemberDto> done,
        Func<DateOnly?, bool> recent,
        DateOnly since,
        DateOnly today)
    {
        var lines = new List<SprintDailyLine>();
        var unfinished = progress.Members.Where(member => member.State is not SprintWorkState.Done).ToList();

        if (done.Count > 0)
        {
            lines.Add(new SprintDailyLine(
                SprintDailyLineKind.Done,
                $"Done: {Join(done.Select(member => member.Title))}",
                Chips(done.SelectMany(member => member.PullRequests.Where(pull => pull.State is LinkedPullRequestState.Merged)))));
        }

        var opened = unfinished
            .Select(member => (member, pulls: member.PullRequests.Where(pull => pull.State is LinkedPullRequestState.Open && recent(pull.OpenedOn)).ToList()))
            .Where(pair => pair.pulls.Count > 0)
            .ToList();
        if (opened.Count > 0)
        {
            lines.Add(new SprintDailyLine(
                SprintDailyLineKind.OpenedForReview,
                $"Opened for review: {Join(opened.Select(pair => pair.member.Title))}",
                Chips(opened.SelectMany(pair => pair.pulls))));
        }

        var started = unfinished.Where(member => recent(member.StartedOn)).ToList();
        if (started.Count > 0)
        {
            lines.Add(SprintDailyLine.Of(SprintDailyLineKind.Started, $"Started: {Join(started.Select(member => member.Title))}"));
        }

        var added = progress.Members.Where(member => member.AddedAfterStart && recent(member.JoinedOn)).ToList();
        if (added.Count > 0)
        {
            lines.Add(SprintDailyLine.Of(SprintDailyLineKind.Added, $"Added to the sprint: {Join(added.Select(member => member.Title))}"));
        }

        if (lines.Count == 0)
        {
            lines.Add(SprintDailyLine.Of(SprintDailyLineKind.Quiet, $"Nothing moved {When(since, today)}."));
        }

        return lines;
    }

    /// <summary>Every member in progress, then every member in progress or not started
    /// with an open pull request — the one waiting on review rather than on its author. A
    /// blocked member is under Blocked instead.</summary>
    private static IReadOnlyList<SprintDailyLine> OnToday(SprintProgressDto progress)
    {
        var waiting = progress.Members
            .Where(member => member.State is SprintWorkState.InProgress or SprintWorkState.NotStarted
                && member.PullRequests.Any(pull => pull.State is LinkedPullRequestState.Open))
            .ToList();
        var waitingIds = waiting.Select(member => member.TaskId).ToHashSet();

        var lines = new List<SprintDailyLine>();
        lines.AddRange(progress.Members
            .Where(member => member.State is SprintWorkState.InProgress && !waitingIds.Contains(member.TaskId))
            .Select(member => new SprintDailyLine(
                SprintDailyLineKind.InProgress,
                member.Title,
                [.. member.PullRequests.Where(pull => pull.State is LinkedPullRequestState.Draft or LinkedPullRequestState.Open).Select(Chip)])));
        lines.AddRange(waiting.Select(member => new SprintDailyLine(
            SprintDailyLineKind.WaitingOnReview,
            $"Review waiting: {member.Title}",
            [.. member.PullRequests.Where(pull => pull.State is LinkedPullRequestState.Open).Select(Chip)])));

        if (lines.Count == 0) lines.Add(SprintDailyLine.Of(SprintDailyLineKind.Quiet, "Nothing is in progress."));
        return lines;
    }

    /// <summary>The blocked members with the day they were marked, then every member
    /// carrying an open question, in its own words.</summary>
    private static IReadOnlyList<SprintDailyLine> Blocked(SprintProgressDto progress) =>
    [
        .. progress.Members
            .Where(member => member.State is SprintWorkState.Blocked)
            .Select(member => SprintDailyLine.Of(
                SprintDailyLineKind.Blocked,
                member.BlockedSince is { } on ? $"{member.Title} · blocked since {Day(on)}" : $"{member.Title} · blocked")),
        .. progress.Members
            .Where(member => member.OpenQuestion)
            .Select(member => SprintDailyLine.Of(
                SprintDailyLineKind.OpenQuestion,
                string.IsNullOrWhiteSpace(member.Note) ? $"{member.Title} · open question" : $"{member.Title} · {member.Note.Trim()}"))
    ];

    /// <summary>
    /// "Goal: 5 tasks open, Openspec blocked among them, so the goal is at risk." The goal
    /// is at risk while it is not met and a goal task is blocked, the last working day has
    /// come, or the goal tasks still needed outnumber what the goal's own pace so far — goal
    /// tasks done per working day elapsed — lands in the working days left.
    /// </summary>
    private static string GoalSentence(SprintProgressDto progress, IReadOnlyList<string> blockedGoal, bool atRisk)
    {
        var goal = progress.Goal;
        if (goal.Total == 0) return "Goal: no goal tasks.";
        if (progress.GoalMet) return $"Goal: met, {Number(goal.Done)} of {Number(goal.Total)} done.";

        var sentence = $"Goal: {Count(goal.Total - goal.Done, "task", "tasks")} open";
        if (blockedGoal.Count > 0) sentence += $", {Join(blockedGoal)} blocked among them";
        return sentence + (atRisk ? ", so the goal is at risk." : ", on track.");
    }

    /// <summary>"All work: 7 behind the even pace; about 19 of 29 land by Friday." The even
    /// pace is every member done by the last working day at the same number each day; the
    /// landing is the pace so far carried over the working days left.</summary>
    private static string AllWorkSentence(SprintProgressDto progress, int elapsed, int days, int left, int landing)
    {
        var all = progress.AllWork;
        if (all.Total == 0) return "All work: nothing in the sprint yet.";
        if (elapsed == 0 || days == 0) return $"All work: {Count(all.Total, "task", "tasks")}, starting {Day(progress.Start)}.";

        var expected = (int)Math.Round((double)all.Total * elapsed / days, MidpointRounding.AwayFromZero);
        var gap = all.Done - expected;
        var pace = gap switch
        {
            0 => "All work: on the even pace",
            > 0 => $"All work: {Number(gap)} ahead of the even pace",
            _ => $"All work: {Number(-gap)} behind the even pace"
        };

        return left == 0
            ? $"{pace}; {Number(all.Done)} of {Number(all.Total)} done."
            : $"{pace}; about {Number(Math.Min(all.Total, all.Done + landing))} of {Number(all.Total)} land by {LastWorkingDay(progress).ToString("dddd", CultureInfo.InvariantCulture)}.";
    }

    /// <summary>Each pull request once: two members often link the one pull request that
    /// finished both.</summary>
    private static IReadOnlyList<SprintBoardPullRequest> Chips(IEnumerable<LinkedPullRequestDto> pulls) =>
        [.. pulls
            .DistinctBy(pull => (pull.Repository.Trim().ToUpperInvariant(), pull.Number.Trim()))
            .Select(Chip)];

    private static SprintBoardPullRequest Chip(LinkedPullRequestDto pull) =>
        new(pull.Repository, pull.Number, pull.State switch
        {
            LinkedPullRequestState.Open => SprintPullRequestState.Open,
            LinkedPullRequestState.Draft => SprintPullRequestState.Draft,
            LinkedPullRequestState.Merged => SprintPullRequestState.Merged,
            LinkedPullRequestState.Closed => SprintPullRequestState.Closed,
            _ => SprintPullRequestState.Unknown
        });

    /// <summary>"since yesterday", or "since Friday" when the previous working day was
    /// not yesterday. "Since", because today counts too.</summary>
    private static string When(DateOnly since, DateOnly today) =>
        since == today.AddDays(-1) ? "since yesterday" : $"since {since.ToString("dddd", CultureInfo.InvariantCulture)}";

    /// <summary>Monday to Friday after <paramref name="from"/> up to <paramref name="to"/>.</summary>
    private static int WorkingDaysAfter(DateOnly from, DateOnly to) =>
        SprintBoardMapper.WorkingDays(from.AddDays(1), to).Count();

    private static DateOnly LastWorkingDay(SprintProgressDto progress) =>
        SprintBoardMapper.WorkingDays(progress.Start, progress.End).DefaultIfEmpty(progress.End).Last();

    private static string Day(DateOnly day) => day.ToString("ddd d MMM", CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Count(int count, string one, string many) => SprintBoardRules.Count(count, one, many);

    private static string Join(IEnumerable<string> parts)
    {
        var list = parts.ToList();
        return list.Count switch
        {
            0 => string.Empty,
            1 => list[0],
            _ => $"{string.Join(", ", list.Take(list.Count - 1))} and {list[^1]}"
        };
    }
}
