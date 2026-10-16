namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A bar's one in-place action, and a bar drawn done. The action is a button of its own at
/// the bar's end — named for the whole act, because the icon is all it draws — that raises
/// <see cref="RoadmapTimeline.OnBarAction"/> and never opens the bar. It is drawn only where
/// the host listens for it.
/// </summary>
public sealed class RoadmapBarActionTests
{
    private static readonly RoadmapWindow Q1 = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

    private static readonly IReadOnlyList<RoadmapGroup> Plan =
    [
        new("delivery", "Delivery", [new RoadmapRow("build", "Build")])
    ];

    private static DateOnly On(int month, int day) => new(2026, month, day);

    private static readonly IReadOnlyList<RoadmapBar> Work =
    [
        new("launch", "build", "Launch", On(1, 5), On(1, 9),
            Action: new RoadmapBarAction(RoadmapBarActionKind.Complete, "Complete Launch")),
        new("sync", "build", "Sync", On(1, 12), On(1, 16),
            Action: new RoadmapBarAction(RoadmapBarActionKind.Open, "Open Sync in Tasks")),
        new("plain", "build", "Plain", On(1, 19), On(1, 23)),
        new("shipped", "build", "Shipped", On(2, 2), On(2, 6), Detail: "Completed 6 Feb 2026", Locked: true, Done: true)
    ];

    private readonly List<string> _acted = [];
    private readonly List<string> _selected = [];

    private IRenderedComponent<RoadmapTimeline> Chart(BunitContext context, bool listening = true)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<RoadmapTimeline>(parameters =>
        {
            parameters
                .Add(timeline => timeline.Groups, Plan)
                .Add(timeline => timeline.Bars, Work)
                .Add(timeline => timeline.Window, Q1)
                .Add(timeline => timeline.OnBarSelected, (string id) => _selected.Add(id))
                .Add(timeline => timeline.TestId, "rm");

            if (listening) parameters.Add(timeline => timeline.OnBarAction, (string id) => _acted.Add(id));
        });
    }

    [Fact]
    public void A_bar_with_an_action_draws_it_as_a_button_named_for_the_whole_act()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        var complete = view.Find("[data-roadmap-bar='launch'] [data-roadmap-action]");
        Assert.Equal("BUTTON", complete.TagName);
        Assert.Equal("complete", complete.GetAttribute("data-roadmap-action"));
        Assert.Equal("Complete Launch", complete.GetAttribute("aria-label"));
        Assert.Equal("Complete Launch", complete.GetAttribute("title"));
        Assert.Null(complete.GetAttribute("tabindex"));
        Assert.Null(complete.GetAttribute("data-roadmap-grip"));

        var open = view.Find("[data-roadmap-bar='sync'] [data-roadmap-action]");
        Assert.Equal("open", open.GetAttribute("data-roadmap-action"));
        Assert.Equal("Open Sync in Tasks", open.GetAttribute("aria-label"));

        Assert.Contains("roadmap-bar--has-action", view.Find("[data-roadmap-bar='launch']").ClassList);
    }

    [Fact]
    public void A_bar_without_an_action_adds_no_button_and_no_tab_stop()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        var plain = view.Find("[data-roadmap-bar='plain']");
        Assert.Null(plain.QuerySelector("[data-roadmap-action]"));
        Assert.DoesNotContain("roadmap-bar--has-action", plain.ClassList);
    }

    [Fact]
    public void Using_the_action_raises_it_for_the_bar_and_never_opens_the_bar()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        view.Find("[data-roadmap-bar='launch'] [data-roadmap-action]").Click();

        Assert.Equal(["launch"], _acted);
        Assert.Empty(_selected);
    }

    /// <summary>The action usually takes itself away — a completed bar has none left — so
    /// once the host has answered, the focus is put on the bar it belonged to rather than
    /// left to fall to the page.</summary>
    [Fact]
    public void Using_the_action_puts_the_focus_on_the_bar_once_the_host_has_answered()
    {
        using var context = new BunitContext();
        var view = Chart(context);
        var body = view.Find("[data-roadmap-bar='launch'] .roadmap-bar__body").Id;

        view.Find("[data-roadmap-bar='launch'] [data-roadmap-action]").Click();

        view.WaitForAssertion(() => Assert.Contains(
            context.JSInterop.Invocations,
            invocation => invocation.Identifier == "backlogFocus" && Equals(invocation.Arguments[0], body)));
    }

    /// <summary>While the host answers, the action shows busy — a spinner named for the act,
    /// still in the tab order — and a second press is dropped.</summary>
    [Fact]
    public void A_busy_action_shows_a_spinner_and_drops_a_press()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var view = context.Render<RoadmapTimeline>(parameters => parameters
            .Add(timeline => timeline.Groups, Plan)
            .Add(timeline => timeline.Bars, Work)
            .Add(timeline => timeline.Window, Q1)
            .Add(timeline => timeline.BusyActionId, "launch")
            .Add(timeline => timeline.OnBarAction, (string id) => _acted.Add(id)));

        var action = view.Find("[data-roadmap-bar='launch'] [data-roadmap-action]");
        Assert.Equal("true", action.GetAttribute("aria-busy"));
        Assert.False(action.HasAttribute("disabled"));
        Assert.NotNull(action.QuerySelector(".spinner__ring"));

        action.Click();

        Assert.Empty(_acted);
        Assert.Null(view.Find("[data-roadmap-bar='sync'] [data-roadmap-action]").GetAttribute("aria-busy"));
    }

    [Fact]
    public void No_action_is_drawn_where_nothing_listens_for_it()
    {
        using var context = new BunitContext();
        var view = Chart(context, listening: false);

        Assert.Empty(view.FindAll("[data-roadmap-action]"));
        Assert.DoesNotContain("roadmap-bar--has-action", view.Find("[data-roadmap-bar='launch']").ClassList);
    }

    [Fact]
    public void A_done_bar_wears_the_done_style_and_a_tick_a_screen_reader_skips()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        var shipped = view.Find("[data-roadmap-bar='shipped']");
        Assert.Contains("roadmap-bar--done", shipped.ClassList);
        Assert.Equal("true", shipped.QuerySelector(".roadmap-bar__done")!.GetAttribute("aria-hidden"));
        Assert.Contains("Completed 6 Feb 2026", shipped.QuerySelector(".roadmap-bar__body .sr-only")!.TextContent, StringComparison.Ordinal);

        var plain = view.Find("[data-roadmap-bar='plain']");
        Assert.DoesNotContain("roadmap-bar--done", plain.ClassList);
        Assert.Null(plain.QuerySelector(".roadmap-bar__done"));
    }
}
