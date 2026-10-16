using System.Globalization;
using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.UI.Parts;
using Backlog.UI.Components.Metrics;

namespace Backlog.Modules.Dashboard.UI;

/// <summary>
/// The figures the dashboard's parts draw, as the lines
/// <see cref="DashboardAiContentSource"/> sends ahead of the pull requests: one line
/// per insight, named after the part that shows it.
/// </summary>
/// <remarks>
/// <para>
/// Worded the way the parts word them, through the same formatters — the tab headlines
/// (<see cref="DashboardHeadlines"/>), the durations and hours
/// (<see cref="DashboardFormat"/>), the outlooks (<see cref="PlanOutlookFormat"/>) — so
/// an answer quoting "38.0h inside office hours" quotes what the reader can see, not a
/// second rounding of it. Invariant throughout, for the reason those classes give.
/// </para>
/// <para>
/// A label then its value, separated by semicolons, rather than prose. The assistant
/// reads these; a sentence would only be one more thing for it to parse, and the
/// labels are the tiles' own, so the reader and the assistant name a figure alike.
/// </para>
/// <para>
/// Lists inside a line — the late roadmap items, the models, the Needs-you items — stop
/// at a handful and say how many more there were. A figure line is charged before any
/// pull request and is never dropped for one, so its length is a cost every question
/// pays; a list that grew with the backlog would crowd out the records the question
/// may actually be about.
/// </para>
/// </remarks>
internal static class DashboardAiFigures
{
    /// <summary>How many names a list inside one line carries before it says "and N
    /// more".</summary>
    internal const int ListLimit = 5;

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>"Hours worked: unavailable: No working week is set." — the part's own
    /// status, in its place in the order, so the assistant can tell a figure that is
    /// missing from a figure that is zero.</summary>
    internal static string Unavailable(string name, InsightAvailability availability) =>
        $"{name}: unavailable: {(string.IsNullOrWhiteSpace(availability.Reason) ? "no reading" : availability.Reason.Trim())}";

    /// <summary>The Productivity tab's Throughput part.</summary>
    internal static string Throughput(ProductivityHeadline headline, DashboardScope scope)
    {
        var parts = new List<string>
        {
            $"pull requests merged {Count(headline.PullRequestsMerged)} ({DashboardFormat.PerWeek(headline.PullRequestsMerged, scope.Weeks)})",
            $"issues closed {Count(headline.IssuesClosed)}",
            headline.PullRequestsReviewed == 0
                ? "no pull request was reviewed"
                : $"came back after review {MetricFormat.Percent(headline.ReworkRate, 0)} of the reviewed",
            headline.MedianReviewTurnaround is { } turnaround
                ? $"first review arrived in {DashboardFormat.Duration(turnaround)} (median)"
                : "no first review to time"
        };

        if (headline.MedianCommitsPerPullRequest is { } commits)
        {
            parts.Add($"commits per pull request {Count(commits)} (median)");
        }

        // A listing that stopped early makes every count above a floor, and the part
        // says so in its note; the line says it too, or the assistant reports a floor
        // as the total.
        if (!headline.Complete) parts.Add("these are floors: not every repository could be read whole");

        return "Throughput: " + string.Join("; ", parts);
    }

    /// <summary>The Productivity tab's Score part: the two scores, out of the
    /// hundred they are drawn against, or why one was not scored.</summary>
    internal static string Score(ProductivityScoreInsight score)
    {
        if (!score.HasInputs) return "Score: nothing to score in this window";

        var volume = score.Volume.Inputs.Count == 0
            ? "volume not scored (no history to set a target from)"
            : $"volume {MetricFormat.Score(score.Volume.Value)} of 100";
        var quality = score.Quality.Inputs.Count == 0
            ? "quality not scored (nothing reviewed or sized)"
            : $"quality {MetricFormat.Score(score.Quality.Value)} of 100";

        var target = score.Target is { } bar
            ? $"; volume target {DashboardFormat.Whole(bar.FullMarks)} merged pull requests, from {bar.MergedPullRequests.ToString(Culture)} merged {DashboardFormat.Day(bar.From)} – {DashboardFormat.Day(bar.To)}"
            : string.Empty;

        return $"Score: {volume}; {quality}{target}";
    }

    /// <summary>The Productivity tab's Rework part, its tiles in their order.</summary>
    internal static string Rework(ReworkInsight rework)
    {
        var churned = rework.PullRequestsReviewed == 0
            ? "pull requests that churned: none reviewed"
            : $"pull requests that churned {Count(rework.PullRequestsWithChurn)} of {Count(rework.PullRequestsReviewed)} reviewed ({MetricFormat.Percent(rework.Rate, 0)})";

        return "Rework after review: " + string.Join("; ",
            churned,
            $"commits after first review {Count(rework.CommitsAfterFirstReview)}",
            $"force pushes {Count(rework.ForcePushesAfterFirstReview)}",
            $"files touched again {Count(rework.FilesRetouched)}",
            $"review rounds {Count(rework.ReviewRounds)}",
            $"changes requested {Count(rework.ChangesRequested)}",
            $"conflicted syncs {Count(rework.ConflictedSyncMerges)} of {Count(rework.SyncMerges)} sync merges");
    }

    /// <summary>The Tasks tab's throughput tiles.</summary>
    internal static string Tasks(TaskThroughputInsight throughput, DashboardScope scope) =>
        "Tasks: " + string.Join("; ",
            $"tasks completed {Count(throughput.Completed)} ({DashboardFormat.PerWeek(throughput.Completed, scope.Weeks)})",
            $"story points done {MetricFormat.Compact(throughput.Effort)}",
            $"completed without an estimate {Count(throughput.Unestimated)}");

    /// <summary>The Tasks tab's plan tiles and the roadmap's outlook: the headline,
    /// the pace, the items by outlook, and the late ones by name.</summary>
    internal static string Plan(PlanInsight plan)
    {
        if (!plan.RoadmapEnabled) return "Plan: the roadmap is switched off";

        var parts = new List<string>
        {
            DashboardHeadlines.Tasks(plan) ?? string.Empty,
            plan.PaceInUse is { } pace
                ? $"pace in use {MetricFormat.Score(pace)} pts / week"
                : "no pace in use",
            $"items in window {Count(plan.Items.Count)}{Outlooks(plan)}",
            plan.WeeksStillToDo is { } weeks
                ? $"still to do {Count(plan.StillToDo)} pts (about {MetricFormat.Score(weeks)} weeks at this pace)"
                : $"still to do {Count(plan.StillToDo)} pts"
        };

        var late = plan.Items.Where(item => PlanOutlookFormat.IsLate(item.Outlook)).Select(item => item.Item.Title.Trim()).ToList();
        if (late.Count > 0) parts.Add("behind: " + Capped(late));

        return "Plan: " + string.Join("; ", parts.Where(part => part.Length > 0));
    }

    /// <summary>The Sessions tab's headline and the figures beside it.</summary>
    internal static string Sessions(AssistantSessionsInsight sessions)
    {
        var parts = new List<string> { DashboardHeadlines.Sessions(sessions) ?? string.Empty };

        if (sessions.Waiting > TimeSpan.Zero) parts.Add($"waiting on you {DashboardFormat.Duration(sessions.Waiting)}");
        if (sessions.PromptsPerSession is { } prompts) parts.Add($"prompts per session {MetricFormat.Score(prompts)}");
        if (sessions.MostSessionsAtOnce is { } peak) parts.Add($"most sessions at once {Count(peak.Peak)}");
        if (sessions.LimitHits.Total > 0) parts.Add($"usage limits hit {Count(sessions.LimitHits.Total)}");
        if (sessions.Capped) parts.Add("more sessions exist than were read");

        return "Assistant sessions: " + string.Join("; ", parts.Where(part => part.Length > 0));
    }

    /// <summary>The Sessions tab's Hours worked tiles.</summary>
    internal static string HoursWorked(HoursWorkedInsight hours) =>
        "Hours worked: " + string.Join("; ",
            $"worked {DashboardFormat.Hours(hours.Actual)}",
            $"inside office hours {DashboardFormat.Hours(hours.Inside)}",
            $"outside office hours {DashboardFormat.Hours(hours.Outside)}",
            $"planned {DashboardFormat.Hours(hours.Planned)}");

    /// <summary>The Devbook tab: the units' last verdicts and the drift issues open.
    /// Neither follows the window, which the line says, as the tab's header does.</summary>
    internal static string Drift(DriftInsight drift)
    {
        var parts = new List<string>
        {
            DashboardHeadlines.Devbook(drift) ?? string.Empty,
            $"drift issues open {Count(drift.Issues.Count)}"
        };

        if (drift.SyncFailed.Count > 0) parts.Add($"sync failed on {Count(drift.SyncFailed.Count)}");
        if (drift.Issues.Count > 0) parts.Add("open: " + Capped([.. drift.Issues.Select(issue => issue.Subject)]));
        parts.Add("the last verdict a sweep left, whatever the window");

        return "Devbook drift: " + string.Join("; ", parts.Where(part => part.Length > 0));
    }

    /// <summary>
    /// The Cost tab's month: what each provider has spent and where it is heading,
    /// and any it projects past its budget.
    /// <para>
    /// The month is named on the line because spend does not take the dashboard's
    /// scope at all — no provider reports it per repository or per machine, and it is
    /// read for the calendar month whatever window is chosen. Without that, an
    /// assistant asked about "the last four weeks" would answer from a month's spend
    /// as though it were the window's.
    /// </para>
    /// </summary>
    internal static string SpendThisMonthName(string month) =>
        $"Spend this month ({month}, the calendar month; not narrowed by repository, machine or window)";

    /// <inheritdoc cref="SpendThisMonthName"/>
    internal static string SpendThisMonth(SpendProjectionInsight projection, string month)
    {
        var name = SpendThisMonthName(month);
        if (projection.Providers.Count == 0) return $"{name}: no provider reported spend";

        var parts = new List<string> { DashboardHeadlines.Cost(projection) ?? string.Empty };

        foreach (var provider in projection.Providers.Where(provider => provider.IsProjectedPastBudget))
        {
            parts.Add($"{provider.Provider.DisplayName()} is projected past its {provider.Budget!.Format()} budget");
        }

        if (projection.Providers.Any(provider => provider.IsEstimate)) parts.Add("some of it is an estimate, not a charge");

        return $"{name}: " + string.Join("; ", parts.Where(part => part.Length > 0));
    }

    /// <summary>"Spend by model (September 2026)": each model with its spend and
    /// tokens, in the part's order.</summary>
    internal static string SpendByModelName(string month) => $"Spend by model ({month})";

    /// <inheritdoc cref="SpendByModelName"/>
    internal static string SpendByModel(SpendByModelInsight byModel, string month)
    {
        var name = SpendByModelName(month);
        if (byModel.Rows.Count == 0) return $"{name}: no model recorded spend";

        var rows = byModel.Rows.Select(row =>
        {
            var figures = new List<string>();
            if (row.Cost is { } cost) figures.Add(cost.Format());
            if (row.Tokens is { } tokens) figures.Add($"{MetricFormat.Compact(tokens)} tokens");

            return figures.Count == 0 ? row.Name : $"{row.Name} {string.Join(", ", figures)}";
        }).ToList();

        return $"{name}: " + Capped(rows);
    }

    /// <summary>The Overview's Needs-you list: each item as its title and detail, or
    /// the part's own "Nothing needs you."; a rule that could not be checked is said,
    /// so an empty list is not read as an all-clear it is not.</summary>
    internal static string NeedsYou(NeedsYouInsight needsYou)
    {
        var items = needsYou.IsEmpty
            ? NeedsYouInsight.NothingNeedsYou
            : Capped([.. needsYou.Items.Select(item => $"{item.Title} — {item.Detail}")]);

        return needsYou.Skipped.Count == 0
            ? $"Needs you: {items}"
            : $"Needs you: {items}; not checked: {Capped(needsYou.Skipped)}";
    }

    private static string Outlooks(PlanInsight plan)
    {
        if (plan.Items.Count == 0) return string.Empty;

        // The enum's order rather than the count's, so two readings of one plan list
        // their outlooks the same way round.
        var counts = plan.Items
            .GroupBy(item => item.Outlook)
            .OrderBy(group => group.Key)
            .Select(group => $"{Count(group.Count())} {PlanOutlookFormat.Word(group.Key)}");

        return $" ({string.Join(", ", counts)})";
    }

    private static string Capped(IReadOnlyList<string> names) =>
        names.Count <= ListLimit
            ? string.Join(", ", names)
            : $"{string.Join(", ", names.Take(ListLimit))} and {Count(names.Count - ListLimit)} more";

    private static string Count(int value) => value.ToString("N0", Culture);
}
