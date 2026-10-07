using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone's three edits as rewrites of the task's entry text: each writes what
/// the desktop's own control writes, reads the fields back off the text, and
/// leaves everything else in the document as it was.
/// </summary>
public sealed class TaskEntryRewriteTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = new(2026, 10, 7);

    private static readonly Guid Pack = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Book = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static TaskPayload Trip(string status = "ready", DateOnly? completedOn = null) =>
        TestTasks.Task(
            "Plan the trip",
            Now,
            inMyDayOn: Today,
            status: status,
            contentMd: "Before Friday.\n\n## Pack\n\n## Book the train",
            subItems:
            [
                new SubItemPayload(Pack, "Pack", "pending", null, 0),
                new SubItemPayload(Book, "Book the train", "pending", null, 1),
            ]).Task with { CompletedOn = completedOn, Tags = ["travel"], DueOn = Today.AddDays(2) };

    [Fact]
    public void Done_ticks_the_task_today_and_makes_it_done_with_its_steps()
    {
        var edited = TaskEntryRewrite.MarkDone(Trip(), Today);

        Assert.NotNull(edited);
        Assert.Equal(Today, edited.CompletedOn);
        Assert.Equal("done", edited.Status);
        Assert.All(edited.SubItems, step => Assert.Equal("done", step.Status));
        Assert.Contains("!done", edited.ContentMd);
        Assert.StartsWith("Before Friday.", edited.ContentMd);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("archived")]
    public void Done_on_a_task_already_over_ticks_it_and_keeps_its_status(string status)
    {
        var task = Trip(status);

        var edited = TaskEntryRewrite.MarkDone(task, Today);

        Assert.NotNull(edited);
        Assert.Equal(Today, edited.CompletedOn);
        Assert.Equal(status, edited.Status);
        Assert.Equal(task.ContentMd, edited.ContentMd);
        Assert.Equal(task.SubItems, edited.SubItems);
    }

    [Fact]
    public void Done_on_a_task_already_ticked_today_changes_nothing()
    {
        Assert.Null(TaskEntryRewrite.MarkDone(Trip("done", completedOn: Today), Today));
    }

    [Fact]
    public void Undone_clears_the_tick_and_leaves_the_status_alone()
    {
        var task = Trip("done", completedOn: Today);

        var edited = TaskEntryRewrite.MarkUndone(task);

        Assert.NotNull(edited);
        Assert.Null(edited.CompletedOn);
        Assert.Equal("done", edited.Status);
        Assert.Equal(task.ContentMd, edited.ContentMd);
    }

    [Fact]
    public void Undone_on_a_task_not_ticked_changes_nothing()
    {
        Assert.Null(TaskEntryRewrite.MarkUndone(Trip()));
    }

    [Fact]
    public void Ticking_a_step_rewrites_that_step_in_the_body_and_in_the_list()
    {
        var edited = TaskEntryRewrite.SetStepDone(Trip(), Book, done: true);

        Assert.NotNull(edited);
        Assert.Equal(["pending", "done"], edited.SubItems.Select(step => step.Status));
        Assert.Equal([false, true], Steps(edited.ContentMd));
        Assert.Equal("ready", edited.Status);
        Assert.Null(edited.CompletedOn);
    }

    [Fact]
    public void Unticking_a_step_rewrites_it_back()
    {
        var ticked = TaskEntryRewrite.SetStepDone(Trip(), Pack, done: true)!;

        var edited = TaskEntryRewrite.SetStepDone(ticked, Pack, done: false);

        Assert.NotNull(edited);
        Assert.Equal(["pending", "pending"], edited.SubItems.Select(step => step.Status));
        Assert.Equal([false, false], Steps(edited.ContentMd));
    }

    [Fact]
    public void A_step_with_a_checkbox_has_its_marker_flipped()
    {
        var task = Trip() with { ContentMd = "## [ ] Pack\n\n## [ ] Book the train" };

        var edited = TaskEntryRewrite.SetStepDone(task, Pack, done: true);

        Assert.Equal("## [x] Pack\n\n## [ ] Book the train", edited!.ContentMd);
        Assert.Equal("done", edited.SubItems[0].Status);
    }

    [Fact]
    public void A_step_the_text_does_not_carry_is_still_set_in_the_list()
    {
        var task = Trip() with { ContentMd = string.Empty };

        var edited = TaskEntryRewrite.SetStepDone(task, Book, done: true);

        Assert.NotNull(edited);
        Assert.Equal(["pending", "done"], edited.SubItems.Select(step => step.Status));
        Assert.Equal(string.Empty, edited.ContentMd);
    }

    [Fact]
    public void A_step_already_so_or_not_on_the_task_changes_nothing()
    {
        Assert.Null(TaskEntryRewrite.SetStepDone(Trip(), Pack, done: false));
        Assert.Null(TaskEntryRewrite.SetStepDone(Trip(), Guid.NewGuid(), done: true));
    }

    [Fact]
    public void Move_to_tomorrow_picks_the_task_for_the_next_day_and_nothing_else()
    {
        var task = Trip();

        var edited = TaskEntryRewrite.MoveToTomorrow(task, Today);

        Assert.NotNull(edited);
        Assert.Equal(Today.AddDays(1), edited.InMyDayOn);

        // Record equality, so every other member — the body, the steps, the
        // due date, the tags — is the very value it was.
        Assert.Equal(task with { InMyDayOn = Today.AddDays(1) }, edited);
    }

    [Fact]
    public void Move_to_tomorrow_of_a_task_already_picked_for_tomorrow_changes_nothing()
    {
        Assert.Null(TaskEntryRewrite.MoveToTomorrow(Trip() with { InMyDayOn = Today.AddDays(1) }, Today));
    }

    [Fact]
    public void The_entry_text_carries_the_title_the_edited_tokens_and_the_body()
    {
        var text = TaskEntryRewrite.ToEntryText(Trip("in_progress", completedOn: Today));

        Assert.Equal(
            "# Plan the trip\n`!in-progress` `myday:2026-10-07` `completed:2026-10-07`\n\nBefore Friday.\n\n## Pack\n\n## Book the train\n",
            text);
    }

    [Fact]
    public void A_status_this_build_has_no_name_for_is_kept_by_an_edit_that_does_not_set_one()
    {
        var edited = TaskEntryRewrite.MoveToTomorrow(Trip("waiting_on_someone"), Today);

        Assert.Equal("waiting_on_someone", edited!.Status);
        Assert.Equal(Today.AddDays(1), edited.InMyDayOn);
    }

    private static IReadOnlyList<bool> Steps(string contentMd) =>
        [.. Backlog.Modules.Tasks.Abstractions.EntryTextParser.Parse("# t\n`!ready`\n\n" + contentMd).SubItems.Select(step => step.Done)];
}
