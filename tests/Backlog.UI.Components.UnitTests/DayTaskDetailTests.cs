using Backlog.UI.Components.Day;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// One task opened from the phone's day, on its own: the chips, Focus, the
/// steps as a checklist, the task's text, and the footer's two edits — in each
/// state the storybook shows (timed, untimed, every step done) and read-only.
/// </summary>
public sealed class DayTaskDetailTests
{
    private static readonly IReadOnlyList<DayStep> Steps =
    [
        new(Guid.Parse("00000000-0000-0000-0000-000000000001"), "List the conflict cases", true),
        new(Guid.Parse("00000000-0000-0000-0000-000000000002"), "Cover the offline-edit case", false),
        new(Guid.Parse("00000000-0000-0000-0000-000000000003"), "Run the suite", false)
    ];

    [Fact]
    public void A_timed_task_shows_its_area_time_and_points_and_the_way_into_Focus()
    {
        using var context = new BunitContext();

        var detail = context.Render<DayTaskDetail>(parameters => parameters
            .Add(p => p.Title, "Write sync conflict tests")
            .Add(p => p.Area, "Sync")
            .Add(p => p.TimeLabel, "Today · 10:45–11:30")
            .Add(p => p.Effort, 3)
            .Add(p => p.FocusHref, "tasks/1/focus")
            .Add(p => p.Steps, Steps)
            .Add(p => p.TestId, "detail"));

        Assert.Equal("Write sync conflict tests", detail.Find("h1[data-testid='detail-title']").TextContent);
        Assert.Equal("Area: Sync", detail.Find("[data-testid='detail-area']").TextContent.Trim());
        Assert.Equal("When: Today · 10:45–11:30", detail.Find("[data-testid='detail-time']").TextContent);
        Assert.Equal("Effort: 3 points", detail.Find("[data-testid='detail-effort']").TextContent);

        var focus = detail.Find("[data-testid='detail-focus']");
        Assert.Equal("tasks/1/focus", focus.GetAttribute("href"));
        Assert.Contains("Focus on this", focus.TextContent, StringComparison.Ordinal);

        var back = detail.Find("[data-testid='detail-back']");
        Assert.Equal(string.Empty, back.GetAttribute("href"));
        Assert.Equal("Today", back.TextContent.Trim());
    }

    [Fact]
    public void An_untimed_task_has_no_time_chip_and_no_Focus()
    {
        using var context = new BunitContext();

        var detail = context.Render<DayTaskDetail>(parameters => parameters
            .Add(p => p.Title, "Reply to the design feedback")
            .Add(p => p.Effort, 1)
            .Add(p => p.Steps, Steps)
            .Add(p => p.TestId, "detail"));

        Assert.Empty(detail.FindAll("[data-testid='detail-time']"));
        Assert.Empty(detail.FindAll("[data-testid='detail-focus']"));
        Assert.Empty(detail.FindAll("[data-testid='detail-area']"));
        Assert.Equal("Effort: 1 point", detail.Find("[data-testid='detail-effort']").TextContent);
    }

    [Fact]
    public void Steps_count_what_is_done_and_strike_the_done_ones_through()
    {
        using var context = new BunitContext();

        var detail = context.Render<DayTaskDetail>(parameters => parameters
            .Add(p => p.Title, "Write sync conflict tests")
            .Add(p => p.Steps, Steps)
            .Add(p => p.OnStepToggled, (DayStep _) => { })
            .Add(p => p.TestId, "detail"));

        Assert.Equal("1 of 3", detail.Find("[data-testid='detail-steps-count']").TextContent);

        var rows = detail.FindAll("[data-testid='detail-step']");
        Assert.Equal(3, rows.Count);
        Assert.Equal("true", rows[0].GetAttribute("data-done"));
        Assert.Contains("task-detail__step--done", rows[0].ClassList);
        Assert.Equal("false", rows[1].GetAttribute("data-done"));

        var boxes = detail.FindAll("[data-testid='detail-step-box'] input[type='checkbox']");
        Assert.True(boxes[0].HasAttribute("checked"));
        Assert.False(boxes[1].HasAttribute("checked"));
        Assert.All(boxes, box => Assert.False(box.HasAttribute("disabled")));
        Assert.Equal("Cover the offline-edit case", detail.FindAll("[data-testid='detail-step-box'] .checkbox__label")[1].TextContent);
    }

    [Fact]
    public void With_every_step_done_the_count_is_full()
    {
        using var context = new BunitContext();
        IReadOnlyList<DayStep> done = [.. Steps.Select(step => step with { Done = true })];

        var detail = context.Render<DayTaskDetail>(parameters => parameters
            .Add(p => p.Title, "Write sync conflict tests")
            .Add(p => p.Steps, done)
            .Add(p => p.TestId, "detail"));

        Assert.Equal("3 of 3", detail.Find("[data-testid='detail-steps-count']").TextContent);
        Assert.All(detail.FindAll("[data-testid='detail-step']"), row => Assert.Equal("true", row.GetAttribute("data-done")));
    }

    [Fact]
    public void Ticking_a_step_hands_it_back_with_its_new_state()
    {
        using var context = new BunitContext();
        var toggled = new List<DayStep>();

        var detail = context.Render<DayTaskDetail>(parameters => parameters
            .Add(p => p.Title, "Write sync conflict tests")
            .Add(p => p.Steps, Steps)
            .Add(p => p.OnStepToggled, (DayStep step) => toggled.Add(step))
            .Add(p => p.TestId, "detail"));

        detail.FindAll("[data-testid='detail-step-box'] input")[1].Change(true);
        detail.FindAll("[data-testid='detail-step-box'] input")[0].Change(false);

        Assert.Equal(
            [Steps[1] with { Done = true }, Steps[0] with { Done = false }],
            toggled);
    }

    [Fact]
    public void The_footer_moves_the_task_to_tomorrow_or_marks_it_done()
    {
        using var context = new BunitContext();
        var pressed = new List<string>();

        var detail = context.Render<DayTaskDetail>(parameters => parameters
            .Add(p => p.Title, "Write sync conflict tests")
            .Add(p => p.OnMoveToTomorrow, () => pressed.Add("tomorrow"))
            .Add(p => p.OnMarkDone, () => pressed.Add("done"))
            .Add(p => p.TestId, "detail"));

        Assert.Equal("Move to tomorrow", detail.Find("[data-testid='detail-tomorrow']").TextContent.Trim());
        Assert.Equal("Mark done", detail.Find("[data-testid='detail-done']").TextContent.Trim());

        detail.Find("[data-testid='detail-tomorrow']").Click();
        detail.Find("[data-testid='detail-done']").Click();

        Assert.Equal(["tomorrow", "done"], pressed);
    }

    [Fact]
    public void Read_only_the_boxes_cannot_be_pressed_and_there_is_no_footer()
    {
        using var context = new BunitContext();

        var detail = context.Render<DayTaskDetail>(parameters => parameters
            .Add(p => p.Title, "Book the offsite venue")
            .Add(p => p.Steps, Steps)
            .Add(p => p.TestId, "detail"));

        Assert.All(
            detail.FindAll("[data-testid='detail-step-box'] input"),
            box => Assert.True(box.HasAttribute("disabled")));
        Assert.Empty(detail.FindAll("[data-testid='detail-footer']"));
    }

    [Fact]
    public void While_an_edit_runs_the_steps_and_the_footer_wait()
    {
        using var context = new BunitContext();

        var detail = context.Render<DayTaskDetail>(parameters => parameters
            .Add(p => p.Title, "Write sync conflict tests")
            .Add(p => p.Steps, Steps)
            .Add(p => p.OnStepToggled, (DayStep _) => { })
            .Add(p => p.OnMarkDone, () => { })
            .Add(p => p.Busy, true)
            .Add(p => p.TestId, "detail"));

        Assert.All(
            detail.FindAll("[data-testid='detail-step-box'] input"),
            box => Assert.True(box.HasAttribute("disabled")));
        Assert.True(detail.Find("[data-testid='detail-done']").HasAttribute("disabled"));
    }

    [Fact]
    public void The_body_is_rendered_and_a_task_without_steps_or_body_draws_neither_section()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var withBody = context.Render<DayTaskDetail>(parameters => parameters
            .Add(p => p.Title, "Write sync conflict tests")
            .Add(p => p.Body, "See the **conflict cases** first.")
            .Add(p => p.TestId, "detail"));

        var body = withBody.Find("[data-testid='detail-body']");
        Assert.Equal("conflict cases", body.QuerySelector("strong")!.TextContent);
        Assert.Empty(body.QuerySelectorAll("textarea"));

        var bare = context.Render<DayTaskDetail>(parameters => parameters
            .Add(p => p.Title, "Call the venue")
            .Add(p => p.TestId, "bare"));

        Assert.Empty(bare.FindAll("[data-testid='bare-steps']"));
        Assert.Empty(bare.FindAll("[data-testid='bare-body']"));
        Assert.Empty(bare.FindAll("[data-testid='bare-chips']"));
    }

    [Fact]
    public void Inside_another_page_the_title_takes_a_lower_heading()
    {
        using var context = new BunitContext();

        var detail = context.Render<DayTaskDetail>(parameters => parameters
            .Add(p => p.Title, "Write sync conflict tests")
            .Add(p => p.HeadingLevel, 3)
            .Add(p => p.Steps, Steps)
            .Add(p => p.TestId, "detail"));

        Assert.Empty(detail.FindAll("h1"));
        Assert.Equal("Write sync conflict tests", detail.Find("h3[data-testid='detail-title']").TextContent);
        Assert.Equal("Steps", detail.Find("[data-testid='detail-steps'] h4").TextContent);
    }
}
