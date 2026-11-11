using Backlog.Modules.Progress.UI.WeeklyReview;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The weekly review's checks (<c>.devbook/domain/progress/features.md#the-weekly-review</c>),
/// the ones the <c>weekly-review</c> skill reports, over inputs in Progress's own words:
/// which line each finding makes, in which stage, and with which action in place.
/// </summary>
public sealed class WeeklyReviewChecklistBuilderTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    private static readonly ReviewBrainAreas Para = new("_inbox", "projects", "someday", "areas", HasArchive: true);

    private static ReviewBrain Brain(
        IReadOnlyList<ReviewNote>? notes = null,
        IReadOnlyList<ReviewDueNote>? due = null,
        IReadOnlyList<ReviewDueNote>? unreadable = null,
        IReadOnlyList<ReviewNote>? toArchive = null,
        ReviewBrainAreas? areas = null) =>
        new(areas ?? Para, notes ?? [], due ?? [], unreadable ?? [], toArchive ?? []);

    private static WeeklyReviewInputs Inputs(
        IReadOnlyList<ReviewInboxItem>? inbox = null,
        ReviewBrain? brain = null,
        IReadOnlyList<ReviewEntry>? entries = null,
        IReadOnlyList<string>? planTags = null,
        IReadOnlyList<ReviewGoal>? goals = null) =>
        new(inbox ?? [], brain ?? Brain(), entries ?? [], planTags ?? [], goals ?? []);

    [Fact]
    public void Nothing_to_review_leaves_every_stage_done()
    {
        var checklist = WeeklyReviewChecklistBuilder.Build(Inputs(), Today);

        Assert.All(WeeklyReviewChecklist.Stages, stage => Assert.True(checklist.IsDone(stage)));
        Assert.Empty(checklist.Unavailable);
    }

    [Fact]
    public void Get_clear_lists_the_queue_the_brains_inbox_and_the_deferred_items_due_this_week()
    {
        var waiting = new ReviewInboxItem(Guid.NewGuid(), "Call the plumber");
        var dueThisWeek = new ReviewInboxItem(Guid.NewGuid(), "Renew passport", Deferred: true, DeferredUntil: Today.AddDays(6));
        var later = new ReviewInboxItem(Guid.NewGuid(), "Plan holiday", Deferred: true, DeferredUntil: Today.AddDays(7));

        var checklist = WeeklyReviewChecklistBuilder.Build(
            Inputs(
                inbox: [waiting, dueThisWeek, later],
                brain: Brain(notes: [new("_inbox/idea.md", "Idea"), new("resources/ref.md", "Ref")])),
            Today);

        var lines = checklist.LinesOf(WeeklyReviewStage.GetClear);
        Assert.Equal(["Call the plumber", "_inbox/idea.md", "Renew passport"], lines.Select(line => line.Subject));
        Assert.Equal(WeeklyReviewActionKind.OpenInbox, lines[0].Actions.Single().Kind);
        Assert.Equal(new WeeklyReviewAction(WeeklyReviewActionKind.OpenNote, "Open the note", "_inbox/idea.md"), lines[1].Actions.Single());
        Assert.Equal(WeeklyReviewActionKind.OpenInbox, lines[2].Actions.Single().Kind);
        Assert.False(checklist.IsDone(WeeklyReviewStage.GetClear));
    }

    [Fact]
    public void A_plan_without_a_next_action_offers_to_mark_the_step_that_can_move()
    {
        var held = new ReviewEntry(Guid.NewGuid(), "Wait on legal", ReviewEntryStatus.Draft, "launch", WaitingFor: "Legal");
        var free = new ReviewEntry(Guid.NewGuid(), "Write the brief", ReviewEntryStatus.Draft, "launch");
        var moving = new ReviewEntry(Guid.NewGuid(), "Build it", ReviewEntryStatus.InProgress, "other");

        var checklist = WeeklyReviewChecklistBuilder.Build(
            Inputs(entries: [held, free, moving], planTags: ["+launch", "other"]),
            Today);

        var line = Assert.Single(checklist.LinesOf(WeeklyReviewStage.GetCurrent));
        Assert.Equal("+launch", line.Subject);
        Assert.Equal(WeeklyReviewActionKind.MarkNextAction, line.Actions[0].Kind);
        Assert.Equal(free.Id.ToString("D"), line.Actions[0].Target);
        Assert.True(line.Actions[0].Writes);
    }

    [Fact]
    public void A_plan_whose_every_step_is_held_opens_what_holds_it()
    {
        var blocked = new ReviewEntry(Guid.NewGuid(), "Blocked step", ReviewEntryStatus.Ready, "launch", Blocked: true);

        var line = Assert.Single(WeeklyReviewChecklistBuilder
            .Build(Inputs(entries: [blocked], planTags: ["launch"]), Today)
            .LinesOf(WeeklyReviewStage.GetCurrent));

        Assert.Equal(new WeeklyReviewAction(WeeklyReviewActionKind.OpenTask, "Open the task", blocked.Id.ToString("D")), line.Actions.Single());
    }

    [Fact]
    public void A_roadmap_plan_with_no_entry_asks_for_its_first_step()
    {
        var line = Assert.Single(WeeklyReviewChecklistBuilder
            .Build(Inputs(planTags: ["+empty"]), Today)
            .LinesOf(WeeklyReviewStage.GetCurrent));

        Assert.Equal(new WeeklyReviewAction(WeeklyReviewActionKind.OpenPlan, "Open the plan", "empty"), line.Actions.Single());
    }

    [Fact]
    public void A_project_note_no_plan_carries_offers_the_note_and_the_archive_and_a_finished_one_the_archive()
    {
        var orphan = new ReviewNote("projects/orphan.md", "Orphan", RoadmapTags: ["gone"]);
        var planned = new ReviewNote("projects/planned.md", "Planned", RoadmapTags: ["+launch"]);
        var finished = new ReviewNote("projects/done.md", "Done");
        var entry = new ReviewEntry(Guid.NewGuid(), "Step", ReviewEntryStatus.Ready, "launch");

        var lines = WeeklyReviewChecklistBuilder
            .Build(Inputs(brain: Brain(notes: [orphan, planned, finished], toArchive: [finished]), entries: [entry]), Today)
            .LinesOf(WeeklyReviewStage.GetCurrent);

        Assert.Equal(["projects/orphan.md", "projects/done.md"], lines.Select(line => line.Subject));
        Assert.Equal([WeeklyReviewActionKind.OpenNote, WeeklyReviewActionKind.ArchiveNote], lines[0].Actions.Select(action => action.Kind));
        Assert.Equal(WeeklyReviewActionKind.ArchiveNote, lines[1].Actions[0].Kind);
        Assert.Equal("projects/done.md", lines[1].Actions[0].Target);
    }

    [Fact]
    public void A_brain_without_an_archive_area_offers_no_move()
    {
        var orphan = new ReviewNote("projects/orphan.md", "Orphan");

        var line = Assert.Single(WeeklyReviewChecklistBuilder
            .Build(Inputs(brain: Brain(notes: [orphan], areas: Para with { HasArchive = false })), Today)
            .LinesOf(WeeklyReviewStage.GetCurrent));

        Assert.Equal(WeeklyReviewActionKind.OpenNote, line.Actions.Single().Kind);
    }

    [Fact]
    public void Late_follow_ups_overdue_entries_and_goals_off_track_open_where_they_are_answered()
    {
        var chase = new ReviewEntry(Guid.NewGuid(), "Quote", ReviewEntryStatus.Ready, WaitingFor: "Anna", FollowUpOn: Today.AddDays(-1));
        var overdue = new ReviewEntry(Guid.NewGuid(), "Taxes", ReviewEntryStatus.Ready, DueOn: Today.AddDays(-2));
        var notYet = new ReviewEntry(Guid.NewGuid(), "Later", ReviewEntryStatus.Ready, DueOn: Today);
        var finished = new ReviewEntry(Guid.NewGuid(), "Old", ReviewEntryStatus.Finished, DueOn: Today.AddDays(-9));
        var lateGoal = new ReviewGoal(Guid.NewGuid(), "Ship", Today.AddDays(-1), Open: 2, Blocked: 0);
        var blockedGoal = new ReviewGoal(Guid.NewGuid(), "Grow", null, Open: 1, Blocked: 1);
        var doneGoal = new ReviewGoal(Guid.NewGuid(), "Done", Today.AddDays(-5), Open: 0, Blocked: 0);

        var lines = WeeklyReviewChecklistBuilder
            .Build(Inputs(entries: [chase, overdue, notYet, finished], goals: [lateGoal, blockedGoal, doneGoal]), Today)
            .LinesOf(WeeklyReviewStage.GetCurrent);

        Assert.Equal(["Quote", "Taxes", "Ship", "Grow"], lines.Select(line => line.Subject));
        Assert.Contains("Chase Anna", lines[0].Check);
        Assert.Equal(WeeklyReviewActionKind.OpenTask, lines[1].Actions.Single().Kind);
        Assert.Equal(WeeklyReviewActionKind.OpenGoals, lines[2].Actions.Single().Kind);
    }

    [Fact]
    public void Get_creative_lists_someday_notes_due_reviews_bad_review_values_and_quiet_areas()
    {
        var recentTask = Guid.NewGuid();
        var staleTask = Guid.NewGuid();
        var busy = new ReviewNote("areas/health.md", "Health", TaskIds: [recentTask]);
        var quiet = new ReviewNote("areas/home.md", "Home", TaskIds: [staleTask]);

        var lines = WeeklyReviewChecklistBuilder
            .Build(
                Inputs(
                    brain: Brain(
                        notes: [new("someday/boat.md", "Boat"), busy, quiet],
                        due: [new("resources/ref.md", "Ref", "2026-11-01")],
                        unreadable: [new("resources/odd.md", "Odd", "soon")]),
                    entries:
                    [
                        new(recentTask, "Run", ReviewEntryStatus.Finished, LastActivity: Today.AddDays(-30)),
                        new(staleTask, "Paint", ReviewEntryStatus.Finished, LastActivity: Today.AddDays(-31)),
                    ]),
                Today)
            .LinesOf(WeeklyReviewStage.GetCreative);

        Assert.Equal(["someday/boat.md", "resources/ref.md", "resources/odd.md", "areas/home.md"], lines.Select(line => line.Subject));
        Assert.All(lines, line => Assert.Equal(WeeklyReviewActionKind.OpenNote, line.Actions.Single().Kind));
    }

    [Fact]
    public void A_missing_brain_or_goals_is_said_and_the_rest_goes_on()
    {
        var checklist = WeeklyReviewChecklistBuilder.Build(
            new WeeklyReviewInputs([new ReviewInboxItem(Guid.NewGuid(), "Item")], null, [], [], null),
            Today);

        Assert.Equal(2, checklist.Unavailable.Count);
        Assert.Contains(checklist.Unavailable, note => note.StartsWith("Brain unavailable", StringComparison.Ordinal));
        Assert.Contains(checklist.Unavailable, note => note.StartsWith("Goals not available", StringComparison.Ordinal));
        Assert.Single(checklist.LinesOf(WeeklyReviewStage.GetClear));
        Assert.True(checklist.IsDone(WeeklyReviewStage.GetCreative));
    }
}
