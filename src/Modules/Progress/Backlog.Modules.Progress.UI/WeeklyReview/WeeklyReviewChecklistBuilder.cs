using System.Globalization;

namespace Backlog.Modules.Progress.UI.WeeklyReview;

/// <summary>
/// The weekly review's checks (<c>.devbook/domain/progress/features.md#the-weekly-review</c>):
/// the ones the <c>weekly-review</c> skill reports, stage by stage, each line with the
/// actions that answer it. Pure: what it reads is <see cref="WeeklyReviewInputs"/> and the
/// day, so a reading is the same for the same inputs.
/// </summary>
public static class WeeklyReviewChecklistBuilder
{
    /// <summary>How far back an area note's tasks count as recent activity.</summary>
    public const int QuietAreaDays = 30;

    /// <summary>How far ahead a deferred item counts as due this week: today and the six
    /// days after.</summary>
    public const int WeekDays = 7;

    /// <summary>The check on a deferred item with no review date, worded as the
    /// <c>weekly-review</c> skill words it.</summary>
    public const string NoReviewDate = "No review date: look at it again.";

    public static WeeklyReviewChecklist Build(WeeklyReviewInputs inputs, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var lines = new List<WeeklyReviewLine>();
        var unavailable = new List<string>();

        if (inputs.Brain is null) unavailable.Add("Brain unavailable: the Brain's notes are left out of the review.");
        if (inputs.Goals is null) unavailable.Add("Goals not available: the goals are left out of the review.");

        GetClear(inputs, today, lines);
        GetCurrent(inputs, today, lines);
        GetCreative(inputs, today, lines);

        return new WeeklyReviewChecklist(lines, unavailable);
    }

    private static void GetClear(WeeklyReviewInputs inputs, DateOnly today, List<WeeklyReviewLine> lines)
    {
        foreach (var item in inputs.Inbox.Where(item => !item.Deferred))
        {
            lines.Add(new($"inbox:{item.Id:D}", WeeklyReviewStage.GetClear, item.Title, "Clarify it in triage.", [OpenInbox]));
        }

        if (inputs.Brain is { } brain)
        {
            foreach (var note in brain.Notes.Where(note => note.IsIn(brain.Areas.Inbox)))
            {
                lines.Add(new($"brain-inbox:{note.Path}", WeeklyReviewStage.GetClear, note.Path, "File it in its area.", [OpenNote(note)]));
            }
        }

        // A deferral with no date never comes back by itself, so every review asks about it,
        // as the weekly-review skill does: the two read the same deferred items.
        var weekEnd = today.AddDays(WeekDays - 1);
        foreach (var item in inputs.Inbox.Where(item => item.Deferred && (item.DeferredUntil is not { } until || until <= weekEnd)))
        {
            lines.Add(new(
                $"deferred:{item.Id:D}",
                WeeklyReviewStage.GetClear,
                item.Title,
                item.DeferredUntil is { } day ? $"Deferred to {Day(day)}: look at it again." : NoReviewDate,
                [OpenInbox]));
        }
    }


    private static void GetCurrent(WeeklyReviewInputs inputs, DateOnly today, List<WeeklyReviewLine> lines)
    {
        var open = inputs.Entries.Where(entry => entry.IsOpen).ToList();

        // Each plan with open entries and no next action: mark the step that can move,
        // or say nothing can and open what holds it.
        foreach (var plan in open
            .Where(entry => entry.PlanTag is { Length: > 0 })
            .GroupBy(entry => Bare(entry.PlanTag!), StringComparer.OrdinalIgnoreCase))
        {
            if (plan.Any(entry => entry.IsNextAction)) continue;

            var candidate = plan.FirstOrDefault(entry => entry.Status == ReviewEntryStatus.Draft && entry.CanMove);
            lines.Add(candidate is not null
                ? new(
                    $"plan:{plan.Key}",
                    WeeklyReviewStage.GetCurrent,
                    $"+{plan.Key}",
                    $"No next action. Make \"{candidate.Title}\" ready.",
                    [new(WeeklyReviewActionKind.MarkNextAction, "Mark next action", candidate.Id.ToString("D")), OpenTask(candidate)])
                : new(
                    $"plan:{plan.Key}",
                    WeeklyReviewStage.GetCurrent,
                    $"+{plan.Key}",
                    "No next action, and every open step is held. Clear what holds it.",
                    [OpenTask(plan.First())]));
        }

        var tagged = inputs.Entries
            .Where(entry => entry.PlanTag is { Length: > 0 })
            .Select(entry => Bare(entry.PlanTag!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var planTags = inputs.PlanTags.Select(Bare).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var tag in planTags.Where(tag => !tagged.Contains(tag)))
        {
            lines.Add(new($"empty-plan:{tag}", WeeklyReviewStage.GetCurrent, $"+{tag}", "The plan holds no entry. Add its first step.",
                [new(WeeklyReviewActionKind.OpenPlan, "Open the plan", tag)]));
        }

        if (inputs.Brain is { } brain)
        {
            // The roadmap's plans, and any tag an entry carries as its plan: a source
            // that reads plans from the entries alone still knows them.
            var known = planTags.Concat(tagged).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var archiving = brain.ToArchive.Select(note => note.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var note in brain.Notes.Where(note => note.IsIn(brain.Areas.Projects) && !archiving.Contains(note.Path)))
            {
                if ((note.RoadmapTags ?? []).Any(tag => known.Contains(Bare(tag)))) continue;

                List<WeeklyReviewAction> actions = [OpenNote(note)];
                if (brain.Areas.HasArchive) actions.Add(Archive(note));
                lines.Add(new($"orphan-project:{note.Path}", WeeklyReviewStage.GetCurrent, note.Path,
                    "No plan carries it. Plan this project, or move it to the archive or someday.", actions));
            }

            foreach (var note in brain.ToArchive)
            {
                lines.Add(new($"to-archive:{note.Path}", WeeklyReviewStage.GetCurrent, note.Path,
                    "Its plan is finished. Move it to the archive.", [Archive(note), OpenNote(note)]));
            }
        }

        foreach (var entry in open.Where(entry => entry.WaitingFor is not null && entry.FollowUpOn is { } on && on < today))
        {
            lines.Add(new($"follow-up:{entry.Id:D}", WeeklyReviewStage.GetCurrent, entry.Title,
                $"Follow up was due {Day(entry.FollowUpOn!.Value)}. Chase {entry.WaitingFor}.", [OpenTask(entry)]));
        }

        foreach (var entry in open.Where(entry => entry.DueOn is { } due && due < today))
        {
            lines.Add(new($"overdue:{entry.Id:D}", WeeklyReviewStage.GetCurrent, entry.Title,
                $"Due {Day(entry.DueOn!.Value)}. Do it, re-date it or drop it.", [OpenTask(entry)]));
        }

        foreach (var goal in inputs.Goals ?? [])
        {
            if (goal.Open <= 0) continue;

            var late = goal.DueDate is { } due && due < today;
            if (!late && goal.Blocked <= 0) continue;

            var why = late
                ? $"Due {Day(goal.DueDate!.Value)} with {goal.Open} open."
                : $"{goal.Blocked} blocked.";
            lines.Add(new($"goal:{goal.Id:D}", WeeklyReviewStage.GetCurrent, goal.Title,
                $"{why} Read its badge in the Goals view and re-plan.", [new(WeeklyReviewActionKind.OpenGoals, "Open Goals")]));
        }
    }

    private static void GetCreative(WeeklyReviewInputs inputs, DateOnly today, List<WeeklyReviewLine> lines)
    {
        if (inputs.Brain is not { } brain) return;

        foreach (var note in brain.Notes.Where(note => note.IsIn(brain.Areas.Someday)))
        {
            lines.Add(new($"someday:{note.Path}", WeeklyReviewStage.GetCreative, note.Path,
                "Promote it to a task or a plan, or drop it.", [OpenNote(note)]));
        }

        foreach (var due in brain.Due)
        {
            lines.Add(new($"review:{due.Path}", WeeklyReviewStage.GetCreative, due.Path,
                $"Its review date has come ({due.Detail}). Look at it again.", [OpenNote(due.Path)]));
        }

        foreach (var warning in brain.Unreadable)
        {
            lines.Add(new($"review-warning:{warning.Path}", WeeklyReviewStage.GetCreative, warning.Path,
                $"Its review value \"{warning.Detail}\" is no date. Fix it.", [OpenNote(warning.Path)]));
        }

        var since = today.AddDays(-QuietAreaDays);
        var recent = inputs.Entries
            .Where(entry => entry.LastActivity is { } day && day >= since)
            .Select(entry => entry.Id)
            .ToHashSet();

        foreach (var note in brain.Notes.Where(note => note.IsIn(brain.Areas.Areas)))
        {
            if ((note.TaskIds ?? []).Any(recent.Contains)) continue;

            lines.Add(new($"quiet-area:{note.Path}", WeeklyReviewStage.GetCreative, note.Path,
                $"No task of it moved in {QuietAreaDays} days. Add the next task, or confirm the standard holds.", [OpenNote(note)]));
        }
    }

    private static readonly WeeklyReviewAction OpenInbox = new(WeeklyReviewActionKind.OpenInbox, "Open the Inbox");

    private static WeeklyReviewAction OpenNote(ReviewNote note) => OpenNote(note.Path);

    private static WeeklyReviewAction OpenNote(string path) => new(WeeklyReviewActionKind.OpenNote, "Open the note", path);

    private static WeeklyReviewAction Archive(ReviewNote note) => new(WeeklyReviewActionKind.ArchiveNote, "Move to archive", note.Path);

    private static WeeklyReviewAction OpenTask(ReviewEntry entry) => new(WeeklyReviewActionKind.OpenTask, "Open the task", entry.Id.ToString("D"));

    private static string Bare(string tag) => tag.Trim().TrimStart('+').Trim();

    private static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
