using System.Globalization;

using Backlog.UI.Components.Day;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// One timed task worked on now, on its own: the countdown as it is handed in —
/// running, an hour or more, and over — the ring, the current step and the
/// step list, and the primary action, which is Step done while a step is left
/// and Complete task once none is.
/// </summary>
public sealed class DayFocusTests
{
    private static readonly IReadOnlyList<DayStep> Steps =
    [
        new(Guid.Parse("00000000-0000-0000-0000-000000000001"), "List the conflict cases", true),
        new(Guid.Parse("00000000-0000-0000-0000-000000000002"), "Cover the offline-edit case", false),
        new(Guid.Parse("00000000-0000-0000-0000-000000000003"), "Run the suite", false)
    ];

    private static readonly double Circumference = 2 * Math.PI * 88;

    [Fact]
    public void A_running_block_shows_the_time_left_and_the_current_step()
    {
        using var context = new BunitContext();

        var focus = Render(context, TimeSpan.FromSeconds(28 * 60 + 40), left: 0.5);

        Assert.Equal("Write sync conflict tests", focus.Find("h1[data-testid='focus-title']").TextContent);
        Assert.Contains("until 11:30", focus.Find("[data-testid='focus-kicker']").TextContent, StringComparison.Ordinal);
        Assert.Contains("Sync", focus.Find("[data-testid='focus-kicker']").TextContent, StringComparison.Ordinal);

        Assert.Equal("28:40", focus.Find("[data-testid='focus-time']").TextContent);
        Assert.Equal("timer", focus.Find("[data-testid='focus-time']").GetAttribute("role"));
        Assert.Contains("left in this block", focus.Find("[data-testid='focus-ring']").TextContent, StringComparison.Ordinal);
        Assert.Equal(Circumference / 2, Offset(focus), precision: 1);

        Assert.Equal("Step 2 of 3 · now", focus.Find("[data-testid='focus-step-number']").TextContent);
        Assert.Equal("Cover the offline-edit case", focus.Find("[data-testid='focus-step-title']").TextContent);
        Assert.Equal(
            ["done", "current", "todo"],
            focus.FindAll("[data-testid='focus-step']").Select(step => step.GetAttribute("data-state")));
        Assert.Equal(
            ["Done: List the conflict cases", "Now: Cover the offline-edit case", "To do: Run the suite"],
            focus.FindAll("[data-testid='focus-step']").Select(step => step.TextContent.Trim()));
    }

    [Theory]
    [InlineData(45 * 60, "45:00")]
    [InlineData(0.4, "00:01")]
    [InlineData(3600 + 5 * 60, "1:05:00")]
    public void Time_left_rounds_up_to_the_second(double seconds, string expected)
    {
        using var context = new BunitContext();

        var focus = Render(context, TimeSpan.FromSeconds(seconds), left: 1);

        Assert.Equal(expected, focus.Find("[data-testid='focus-time']").TextContent);
    }

    [Theory]
    [InlineData(0, "Over by 00:00")]
    [InlineData(-192.7, "Over by 03:12")]
    public void Over_time_reads_how_far_over_and_empties_the_ring(double seconds, string expected)
    {
        using var context = new BunitContext();

        var focus = Render(context, TimeSpan.FromSeconds(seconds), left: 0);

        Assert.Equal(expected, focus.Find("[data-testid='focus-time']").TextContent);
        Assert.Equal("true", focus.Find("[data-testid='focus-ring']").GetAttribute("data-over"));
        Assert.DoesNotContain("left in this block", focus.Find("[data-testid='focus-ring']").TextContent, StringComparison.Ordinal);
        Assert.Equal(Circumference, Offset(focus), precision: 1);
    }

    [Fact]
    public void Step_done_hands_back_the_current_step_ticked()
    {
        using var context = new BunitContext();
        DayStep? done = null;

        var focus = context.Render<DayFocus>(parameters => parameters
            .Add(p => p.Title, "Write sync conflict tests")
            .Add(p => p.Until, "11:30")
            .Add(p => p.Remaining, TimeSpan.FromMinutes(10))
            .Add(p => p.Steps, Steps)
            .Add(p => p.OnStepDone, step => done = step)
            .Add(p => p.TestId, "focus"));

        focus.Find("[data-testid='focus-step-done']").Click();

        Assert.Equal(Steps[1] with { Done = true }, done);
    }

    [Fact]
    public void With_every_step_done_the_primary_action_completes_the_task()
    {
        using var context = new BunitContext();
        var completed = false;

        var focus = context.Render<DayFocus>(parameters => parameters
            .Add(p => p.Title, "Write sync conflict tests")
            .Add(p => p.Until, "11:30")
            .Add(p => p.Remaining, TimeSpan.FromMinutes(10))
            .Add(p => p.Steps, [.. Steps.Select(step => step with { Done = true })])
            .Add(p => p.OnComplete, () => completed = true)
            .Add(p => p.TestId, "focus"));

        Assert.Equal("All 3 steps done", focus.Find("[data-testid='focus-step-number']").TextContent);
        Assert.Empty(focus.FindAll("[data-testid='focus-step-done']"));

        focus.Find("[data-testid='focus-complete']").Click();

        Assert.True(completed);
    }

    [Fact]
    public void Without_steps_there_is_no_step_section_and_Complete_task_is_offered()
    {
        using var context = new BunitContext();

        var focus = Render(context, TimeSpan.FromMinutes(5), left: 0.2, steps: []);

        Assert.Empty(focus.FindAll("[data-testid='focus-current']"));
        Assert.Empty(focus.FindAll("[data-testid='focus-steps']"));
        Assert.NotNull(focus.Find("[data-testid='focus-complete']"));
    }

    [Fact]
    public void Busy_holds_the_primary_action_and_there_is_no_pause()
    {
        using var context = new BunitContext();

        var focus = context.Render<DayFocus>(parameters => parameters
            .Add(p => p.Title, "Write sync conflict tests")
            .Add(p => p.Until, "11:30")
            .Add(p => p.Steps, Steps)
            .Add(p => p.OnStepDone, _ => { })
            .Add(p => p.Busy, true)
            .Add(p => p.TestId, "focus"));

        Assert.True(focus.Find("[data-testid='focus-step-done']").HasAttribute("disabled"));
        Assert.DoesNotContain("Pause", focus.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_links_lead_where_the_host_says()
    {
        using var context = new BunitContext();

        var focus = Render(context, TimeSpan.FromMinutes(5), left: 0.2);

        Assert.Equal(string.Empty, focus.Find("[data-testid='focus-back']").GetAttribute("href"));
        Assert.Equal("Today", focus.Find("[data-testid='focus-back']").TextContent.Trim());
        Assert.Equal("tasks/1", focus.Find("[data-testid='focus-details']").GetAttribute("href"));
        Assert.Equal("capture?from=tasks%2F1%2Ffocus", focus.Find("[data-testid='focus-capture']").GetAttribute("href"));
        Assert.Contains("Capture a thought", focus.Find("[data-testid='focus-capture']").TextContent, StringComparison.Ordinal);
    }

    private static IRenderedComponent<DayFocus> Render(BunitContext context, TimeSpan remaining, double left, IReadOnlyList<DayStep>? steps = null) =>
        context.Render<DayFocus>(parameters => parameters
            .Add(p => p.Title, "Write sync conflict tests")
            .Add(p => p.Area, "Sync")
            .Add(p => p.Until, "11:30")
            .Add(p => p.Remaining, remaining)
            .Add(p => p.Left, left)
            .Add(p => p.Steps, steps ?? Steps)
            .Add(p => p.OnStepDone, _ => { })
            .Add(p => p.OnComplete, () => { })
            .Add(p => p.DetailsHref, "tasks/1")
            .Add(p => p.CaptureHref, "capture?from=tasks%2F1%2Ffocus")
            .Add(p => p.TestId, "focus"));

    private static double Offset(IRenderedComponent<DayFocus> focus) =>
        double.Parse(focus.Find("[data-testid='focus-ring-fill']").GetAttribute("stroke-dashoffset")!, CultureInfo.InvariantCulture);
}
