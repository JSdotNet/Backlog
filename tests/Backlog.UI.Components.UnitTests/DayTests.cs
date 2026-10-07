using Backlog.UI.Components.Day;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The parts of the phone's Today screen on their own: the day's progress, the
/// card on now, and a row that ticks and opens as two targets.
/// </summary>
public sealed class DayTests
{
    [Fact]
    public void Progress_says_the_count_in_words_and_carries_it_on_the_bar()
    {
        using var context = new BunitContext();

        var progress = context.Render<DayProgress>(parameters => parameters
            .Add(p => p.Done, 3)
            .Add(p => p.Total, 8)
            .Add(p => p.TestId, "progress"));

        Assert.Equal("3 of 8 done", progress.Find("[data-testid='progress-label']").TextContent.Trim());
        var bar = progress.Find("[role='progressbar']");
        Assert.Equal("3 of 8 done", bar.GetAttribute("aria-valuetext"));
        Assert.Equal("width: 37.5%", progress.Find(".day-progress__fill").GetAttribute("style"));
    }

    [Fact]
    public void Progress_with_nothing_picked_is_an_empty_bar()
    {
        using var context = new BunitContext();

        var progress = context.Render<DayProgress>(parameters => parameters
            .Add(p => p.Done, 0)
            .Add(p => p.Total, 0));

        Assert.Equal("width: 0%", progress.Find(".day-progress__fill").GetAttribute("style"));
        Assert.Empty(progress.FindAll("[role='progressbar']"));
    }

    [Fact]
    public void A_card_with_every_step_done_says_so_and_a_card_with_none_draws_no_bar()
    {
        using var context = new BunitContext();

        var done = context.Render<NowCard>(parameters => parameters
            .Add(p => p.Kicker, "Now · until 11:30")
            .Add(p => p.Title, "Write sync conflict tests")
            .Add(p => p.StepsDone, 3)
            .Add(p => p.StepsTotal, 3)
            .Add(p => p.DetailsHref, "tasks/1")
            .Add(p => p.FocusHref, "tasks/1/focus")
            .Add(p => p.TestId, "card"));

        Assert.Equal("3 of 3 steps done", done.Find("[data-testid='card-step']").TextContent);
        Assert.Empty(done.FindAll("[data-testid='card-next']"));
        Assert.Equal("Now · until 11:30", done.Find("section").GetAttribute("aria-label"));

        var none = context.Render<NowCard>(parameters => parameters
            .Add(p => p.Kicker, "Next · 14:00")
            .Add(p => p.Title, "Plan sprint goals")
            .Add(p => p.DetailsHref, "tasks/2")
            .Add(p => p.FocusHref, "tasks/2/focus")
            .Add(p => p.TestId, "card"));

        Assert.Empty(none.FindAll("[data-testid='card-steps']"));
        Assert.Empty(none.FindAll("[data-testid='card-step']"));
    }

    [Fact]
    public void A_row_raises_the_state_asked_for_and_opens_through_its_link()
    {
        using var context = new BunitContext();
        bool? asked = null;

        var row = context.Render<DayTaskRow>(parameters => parameters
            .Add(p => p.Title, "Triage the inbox")
            .Add(p => p.Done, true)
            .Add(p => p.Href, "tasks/7")
            .Add(p => p.OnToggle, (bool done) => asked = done)
            .Add(p => p.TestId, "row"));

        var tick = row.Find("[data-testid='row-tick']");
        Assert.Equal("Mark not done: Triage the inbox", tick.GetAttribute("aria-label"));
        Assert.Null(tick.GetAttribute("aria-pressed"));
        Assert.Equal("tasks/7", row.Find("a[data-testid='row-open']").GetAttribute("href"));

        tick.Click();

        Assert.False(asked);
    }

    [Fact]
    public void A_row_nobody_listens_to_draws_its_circle_disabled()
    {
        using var context = new BunitContext();

        var row = context.Render<DayTaskRow>(parameters => parameters
            .Add(p => p.Title, "Triage the inbox")
            .Add(p => p.Href, "tasks/7")
            .Add(p => p.TestId, "row"));

        Assert.True(row.Find("[data-testid='row-tick']").HasAttribute("disabled"));
    }
}
