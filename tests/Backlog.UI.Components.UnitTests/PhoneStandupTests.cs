using Backlog.UI.Components.PhoneStandup;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The phone stand-up's parts: each draws a picture a screen reader hears in words — the
/// goal bar's counts, the spark's weeks — and the state list is a radio group that moves
/// an impediment forward only (<c>.devbook/design/color-scheme.md#impediment-state-tones</c>).
/// </summary>
public sealed class PhoneStandupTests
{
    private static readonly StandupImpedimentStep[] Escalated =
    [
        new(StandupImpedimentState.New, new DateOnly(2026, 10, 14), null),
        new(StandupImpedimentState.Reported, new DateOnly(2026, 10, 15), "bank support"),
        new(StandupImpedimentState.Escalated, new DateOnly(2026, 10, 16), "account manager")
    ];

    [Fact]
    public void The_goal_bar_is_an_image_that_says_its_counts_and_draws_no_empty_part()
    {
        using var context = new BunitContext();

        var bar = context.Render<GoalStackedBar>(parameters => parameters
            .Add(p => p.Done, 9).Add(p => p.Doing, 3).Add(p => p.Blocked, 0).Add(p => p.ToDo, 2).Add(p => p.Height, 10));

        var root = bar.Find(".goal-bar");
        Assert.Equal("img", root.GetAttribute("role"));
        Assert.Equal("9 of 14 done, 3 in progress, 2 not started", root.GetAttribute("aria-label"));
        Assert.Equal("height: 10px", root.GetAttribute("style"));
        Assert.Equal(
            ["goal-bar__part--done", "goal-bar__part--doing", "goal-bar__part--to-do"],
            bar.FindAll(".goal-bar__part").Select(part => part.ClassList.Last()));
        Assert.Equal("flex-grow: 9", bar.Find(".goal-bar__part--done").GetAttribute("style"));
    }

    [Fact]
    public void A_goal_counting_nothing_says_so()
    {
        using var context = new BunitContext();

        var bar = context.Render<GoalStackedBar>();

        Assert.Equal("Nothing counted yet", bar.Find(".goal-bar").GetAttribute("aria-label"));
        Assert.Empty(bar.FindAll(".goal-bar__part"));
    }

    [Fact]
    public void The_spark_names_every_week_and_lights_the_current_one()
    {
        using var context = new BunitContext();

        var spark = context.Render<WeekSpark>(parameters => parameters.Add(p => p.Counts, [1, 2, 0, 3]));

        Assert.Equal("Done per week: 1, 2, 0, 3", spark.Find(".week-spark").GetAttribute("aria-label"));
        var bars = spark.FindAll(".week-spark__bar");
        Assert.Equal(4, bars.Count);
        Assert.Equal(["false", "false", "false", "true"], bars.Select(bar => bar.ClassList.Contains("week-spark__bar--current") ? "true" : "false"));
        Assert.Equal("height: 2px", bars[2].GetAttribute("style"));
        Assert.Equal("height: 21px", bars[3].GetAttribute("style"));
    }

    [Fact]
    public void The_goal_card_is_one_link_with_its_badge_bar_and_numbers()
    {
        using var context = new BunitContext();

        var card = context.Render<StandupGoalCard>(parameters => parameters
            .Add(p => p.Title, "Customers get paid faster")
            .Add(p => p.Standing, StandupStanding.AtRisk)
            .Add(p => p.Done, 9).Add(p => p.Doing, 3).Add(p => p.Blocked, 1).Add(p => p.ToDo, 2)
            .Add(p => p.DoneInWindow, 3)
            .Add(p => p.DueDate, new DateOnly(2026, 10, 30))
            .Add(p => p.Href, "standup/goals/1")
            .Add(p => p.TestId, "card"));

        var link = card.Find("a.standup-goal");
        Assert.Equal("standup/goals/1", link.GetAttribute("href"));
        Assert.Equal("At risk", card.Find("[data-testid='card-standing']").TextContent.Trim());
        Assert.Contains("badge--standup-at-risk", card.Find("[data-testid='card-standing']").ClassList);
        Assert.Equal("9 of 15 done, 3 in progress, 1 blocked, 2 not started", card.Find("[data-testid='card-bar']").GetAttribute("aria-label"));
        Assert.Equal("9/15 · +3", card.Find("[data-testid='card-done']").TextContent.Trim());
        Assert.Equal("due 30 Oct", card.Find("[data-testid='card-due']").TextContent);
    }

    [Theory]
    [InlineData(StandupImpedimentState.New, "New", "badge--standup-impediment-new")]
    [InlineData(StandupImpedimentState.Reported, "Reported", "badge--standup-impediment-reported")]
    [InlineData(StandupImpedimentState.Escalated, "Escalated", "badge--standup-impediment-escalated")]
    [InlineData(StandupImpedimentState.BeingHandled, "Being handled", "badge--standup-impediment-being-handled")]
    [InlineData(StandupImpedimentState.Resolved, "Resolved", "badge--standup-impediment-resolved")]
    public void An_impediment_badge_prints_its_state_and_hides_its_mark(StandupImpedimentState state, string word, string tone)
    {
        using var context = new BunitContext();

        var badge = context.Render<StandupImpedimentBadge>(parameters => parameters.Add(p => p.State, state));

        var root = badge.Find(".badge");
        Assert.Contains(tone, root.ClassList);
        Assert.Equal(word, root.TextContent.Trim());
        Assert.All(root.Children, mark => Assert.Equal("true", mark.GetAttribute("aria-hidden")));
        Assert.Equal(state is StandupImpedimentState.New or StandupImpedimentState.Escalated ? 1 : 0, root.Children.Length);
    }

    [Fact]
    public void The_work_list_groups_by_goal_skips_empty_groups_and_puts_detail_where_asked()
    {
        using var context = new BunitContext();
        StandupWorkGroup<string>[] groups =
        [
            new("Customers get paid faster", ["Invoice PDF export", "Reminder emails"]),
            new("Devbook search ships", []),
            new(null, ["VAT rate per line item"])
        ];

        var list = context.Render<StandupWorkList<string>>(parameters => parameters
            .Add(p => p.Groups, groups)
            .Add(p => p.ItemTitle, item => item)
            .Add(p => p.HasDetail, item => item.StartsWith('I'))
            .Add(p => p.Detail, item => builder => builder.AddContent(0, $"detail of {item}"))
            .Add(p => p.TestId, "list"));

        Assert.Equal(["Customers get paid faster", "No goal"], list.FindAll("[data-testid='list-goal']").Select(goal => goal.TextContent));
        Assert.Equal(
            ["Invoice PDF export", "Reminder emails", "VAT rate per line item"],
            list.FindAll("[data-testid='list-title']").Select(title => title.TextContent));
        Assert.Equal(["detail of Invoice PDF export"], list.FindAll(".standup-work__detail").Select(detail => detail.TextContent));
    }

    [Fact]
    public void The_state_list_is_a_radio_group_that_ticks_the_past_and_rings_the_future()
    {
        using var context = new BunitContext();

        var steps = RenderSteps(context, StandupImpedimentState.Escalated, _ => { });

        Assert.Equal("radiogroup", steps.Find("[data-testid='steps']").GetAttribute("role"));
        var radios = steps.FindAll("[role='radio']");
        Assert.Equal(
            ["New", "Reported", "Escalated", "Being handled", "Resolved"],
            radios.Select(radio => radio.QuerySelector(".impediment-steps__label")!.FirstChild!.TextContent.Trim()));
        Assert.Equal(["false", "false", "true", "false", "false"], radios.Select(radio => radio.GetAttribute("aria-checked")));
        Assert.Equal(
            ["passed", "passed", "current", "open", "open"],
            radios.Select(radio => radio.QuerySelector(".impediment-steps__marker")!.GetAttribute("data-phase")));
        Assert.All(radios, radio => Assert.Equal("true", radio.QuerySelector(".impediment-steps__marker")!.GetAttribute("aria-hidden")));
        Assert.Equal("Wed 14", radios[0].QuerySelector(".impediment-steps__day")!.TextContent.Trim());
        Assert.Equal(" · account manager", radios[2].QuerySelector(".impediment-steps__with")!.TextContent);
        Assert.Null(radios[3].QuerySelector(".impediment-steps__day"));
        Assert.Equal([true, true, false, false, false], radios.Select(radio => radio.HasAttribute("disabled")));
    }

    [Fact]
    public void The_arrows_move_the_focus_only_and_a_tap_takes_the_state()
    {
        using var context = new BunitContext();
        var chosen = new List<StandupImpedimentState>();

        var steps = RenderSteps(context, StandupImpedimentState.Escalated, chosen.Add);

        // Up from Escalated would wrap to Resolved; it moves the focus there and resolves nothing.
        steps.Find("[data-testid='steps-escalated']").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        steps.Find("[data-testid='steps-escalated']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Empty(chosen);
        Assert.Equal("true", steps.Find("[data-testid='steps-escalated']").GetAttribute("aria-checked"));

        steps.Find("[data-testid='steps-being-handled']").Click();
        Assert.Equal([StandupImpedimentState.BeingHandled], chosen);
    }

    [Fact]
    public void A_new_impediment_offers_only_the_moves_the_domain_allows()
    {
        using var context = new BunitContext();

        var steps = RenderSteps(context, StandupImpedimentState.New, _ => { }, [Escalated[0]]);

        Assert.Equal([false, false, true, true, false], steps.FindAll("[role='radio']").Select(radio => radio.HasAttribute("disabled")));
    }

    [Fact]
    public void A_resolved_impediment_takes_no_other_state()
    {
        using var context = new BunitContext();

        var steps = RenderSteps(context, StandupImpedimentState.Resolved, _ => { });

        Assert.Equal([true, true, true, true, false], steps.FindAll("[role='radio']").Select(radio => radio.HasAttribute("disabled")));
        Assert.Equal("open", steps.Find("[data-testid='steps-being-handled'] .impediment-steps__marker").GetAttribute("data-phase"));
    }

    /// <summary>The domain's moves (ImpedimentStates.Allows), written out: the library
    /// references no module, so its host hands the rule in.</summary>
    private static bool Allows(StandupImpedimentState from, StandupImpedimentState to) =>
        (from, to) switch
        {
            (StandupImpedimentState.Resolved, _) => false,
            (_, StandupImpedimentState.Resolved) => true,
            (StandupImpedimentState.New, StandupImpedimentState.Reported) => true,
            (StandupImpedimentState.Reported, StandupImpedimentState.Escalated or StandupImpedimentState.BeingHandled) => true,
            (StandupImpedimentState.Escalated, StandupImpedimentState.BeingHandled) => true,
            _ => false,
        };

    private static IRenderedComponent<ImpedimentStateSteps> RenderSteps(
        BunitContext context,
        StandupImpedimentState current,
        Action<StandupImpedimentState> chosen,
        IReadOnlyList<StandupImpedimentStep>? history = null) =>
        context.Render<ImpedimentStateSteps>(parameters => parameters
            .Add(p => p.Current, current)
            .Add(p => p.History, history ?? Escalated)
            .Add(p => p.CanMoveTo, state => Allows(current, state))
            .Add(p => p.OnSelect, chosen)
            .Add(p => p.TestId, "steps"));
}
