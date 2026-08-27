namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The counter is the reason the pager exists, so most of what is pinned here is
/// what it says and when it declines to say anything at all.
/// </summary>
public sealed class RecordPagerTests
{
    private static IRenderedComponent<RecordPager> Render(
        BunitContext context,
        int index,
        int total,
        Action? onPrevious = null,
        Action? onNext = null) =>
        context.Render<RecordPager>(parameters =>
        {
            parameters.Add(p => p.Index, index);
            parameters.Add(p => p.Total, total);
            parameters.Add(p => p.AriaLabel, "Chapters");

            if (onPrevious is not null) parameters.Add(p => p.OnPrevious, EventCallback.Factory.Create(new object(), onPrevious));
            if (onNext is not null) parameters.Add(p => p.OnNext, EventCallback.Factory.Create(new object(), onNext));
        });

    /// <summary>One-based, because a reader counts from one and an index does
    /// not.</summary>
    [Fact]
    public void The_counter_reads_one_based()
    {
        using var context = new BunitContext();

        var pager = Render(context, index: 2, total: 12);

        Assert.Equal("3", pager.Find(".record-pager__index").TextContent.Trim());
        Assert.Equal("12", pager.Find(".record-pager__total").TextContent.Trim());
    }

    /// <summary>"0 of 0" is a counter reporting on a sequence that does not exist.
    /// An absent fact is left out rather than printed as a placeholder.</summary>
    [Fact]
    public void An_empty_sequence_renders_nothing_at_all()
    {
        using var context = new BunitContext();

        var pager = Render(context, index: 0, total: 0);

        Assert.Empty(pager.FindAll(".record-pager"));
    }

    /// <summary>The ends disable rather than wrap: reaching the last record should
    /// say that it is the last, not send the reader back to the first.</summary>
    [Fact]
    public void The_first_record_cannot_go_back()
    {
        using var context = new BunitContext();

        var pager = Render(context, index: 0, total: 3);
        var steps = pager.FindAll(".record-pager__step");

        Assert.True(steps[0].HasAttribute("disabled"));
        Assert.False(steps[1].HasAttribute("disabled"));
    }

    [Fact]
    public void The_last_record_cannot_go_on()
    {
        using var context = new BunitContext();

        var pager = Render(context, index: 2, total: 3);
        var steps = pager.FindAll(".record-pager__step");

        Assert.False(steps[0].HasAttribute("disabled"));
        Assert.True(steps[1].HasAttribute("disabled"));
    }

    [Fact]
    public void Both_steps_report_when_there_is_somewhere_to_go()
    {
        using var context = new BunitContext();
        var back = 0;
        var on = 0;

        var pager = Render(context, index: 1, total: 3, onPrevious: () => back++, onNext: () => on++);
        var steps = pager.FindAll(".record-pager__step");

        steps[0].Click();
        steps[1].Click();

        Assert.Equal(1, back);
        Assert.Equal(1, on);
    }

    /// <summary>A disabled arrow is disabled in fact, not only in appearance.</summary>
    [Fact]
    public void A_step_at_the_end_reports_nothing()
    {
        using var context = new BunitContext();
        var back = 0;

        var pager = Render(context, index: 0, total: 3, onPrevious: () => back++);
        pager.FindAll(".record-pager__step")[0].Click();

        Assert.Equal(0, back);
    }

    /// <summary>Two unlabelled arrows and a number is what a screen reader hears
    /// without this.</summary>
    [Fact]
    public void The_sequence_and_its_steps_are_named()
    {
        using var context = new BunitContext();

        var pager = context.Render<RecordPager>(parameters => parameters
            .Add(p => p.Index, 1)
            .Add(p => p.Total, 4)
            .Add(p => p.AriaLabel, "Technologies")
            .Add(p => p.PreviousLabel, "Previous technology")
            .Add(p => p.NextLabel, "Next technology"));

        Assert.Equal("Technologies", pager.Find("nav").GetAttribute("aria-label"));

        var steps = pager.FindAll(".record-pager__step");

        Assert.Equal("Previous technology", steps[0].GetAttribute("aria-label"));
        Assert.Equal("Next technology", steps[1].GetAttribute("aria-label"));
    }

    /// <summary>The keys belong to whatever binds them. No hints, no hint strip.</summary>
    [Fact]
    public void The_key_hints_are_the_callers_and_are_optional()
    {
        using var context = new BunitContext();

        Assert.Empty(Render(context, index: 0, total: 3).FindAll(".record-pager__hint"));

        var hinted = context.Render<RecordPager>(parameters => parameters
            .Add(p => p.Index, 0)
            .Add(p => p.Total, 3)
            .Add(p => p.Hints, [new RecordPagerHint("Esc", "close")]));

        Assert.Equal("Esc", hinted.Find(".record-pager__keys").TextContent.Trim());
        Assert.Equal("close", hinted.Find(".record-pager__action").TextContent.Trim());
    }
}
