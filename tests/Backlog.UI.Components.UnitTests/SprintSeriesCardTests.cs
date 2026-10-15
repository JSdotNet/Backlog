using Backlog.UI.Components.Progress;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What a host is promised about a sprint series' card on Settings › Sprints: the
/// switch, the repositories with only the free ones offered, the cadence, the name
/// with the next one shown, the goal rules — a rule naming nothing is held back — and
/// the briefs.
/// </summary>
public sealed class SprintSeriesCardTests
{
    private static SprintSeriesCardModel Series(
        bool isOn = true,
        string[]? repositories = null,
        SprintGoalSettings? goal = null,
        string pattern = "Sprint {n}") =>
        new(
            "core",
            "Fincent team",
            isOn,
            "Sprint 42 runs Mon 5 Oct to Fri 16 Oct",
            repositories ?? ["innovadis-dev/Fincent", "JSdotNet/devbook-marketplace"],
            2,
            DayOfWeek.Monday,
            pattern,
            43,
            goal ?? SprintGoalSettings.Default,
            ShowInDaily: true,
            ShowInWeekly: false);

    [Fact]
    public void Draws_the_series_its_switch_its_repositories_and_the_next_sprint_name()
    {
        using var context = new BunitContext();

        var card = context.Render<SprintSeriesCard>(parameters => parameters.Add(c => c.Model, Series()));

        Assert.Equal("Fincent team", card.Find("h3").TextContent);
        Assert.Equal("true", card.Find("[role='switch']").GetAttribute("aria-checked"));
        Assert.Equal(2, card.FindAll("[data-testid='sprint-core-repository']").Count);
        Assert.Equal("next: Sprint 43", card.Find("[data-testid='sprint-core-next']").TextContent);
        Assert.Contains(
            "A repository belongs to one sprint. Add repository offers only those in no sprint.",
            card.Markup,
            StringComparison.Ordinal);
        Assert.Equal("true", card.Find("[data-testid='sprint-core-length-2']").GetAttribute("aria-checked"));
        Assert.Equal("Monday", card.Find("select#sprint-core-start").GetAttribute("value"));
    }

    [Fact]
    public void The_switch_raises_the_new_state()
    {
        using var context = new BunitContext();
        bool? switched = null;

        var card = context.Render<SprintSeriesCard>(parameters => parameters
            .Add(c => c.Model, Series())
            .Add(c => c.OnSwitch, value => switched = value));
        card.Find("[role='switch']").Click();

        Assert.False(switched);
    }

    [Fact]
    public void Add_repository_offers_only_the_free_repositories_and_hands_the_pick_over()
    {
        using var context = new BunitContext();
        string? added = null;

        var card = context.Render<SprintSeriesCard>(parameters => parameters
            .Add(c => c.Model, Series())
            .Add(c => c.FreeRepositories, ["JSdotNet/Backlog"])
            .Add(c => c.OnAddRepository, alias => added = alias));
        var select = card.Find("[data-testid='sprint-core-add-repository'] select");

        Assert.Equal(["", "JSdotNet/Backlog"], select.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
        select.Change("JSdotNet/Backlog");

        Assert.Equal("JSdotNet/Backlog", added);
    }

    [Fact]
    public void With_no_free_repository_add_repository_is_disabled()
    {
        using var context = new BunitContext();

        var card = context.Render<SprintSeriesCard>(parameters => parameters.Add(c => c.Model, Series()));

        Assert.True(card.Find("[data-testid='sprint-core-add-repository'] select").HasAttribute("disabled"));
    }

    [Fact]
    public void A_repository_chip_removes_its_repository_but_the_last_one_cannot_be_removed()
    {
        using var context = new BunitContext();
        string? removed = null;

        var card = context.Render<SprintSeriesCard>(parameters => parameters
            .Add(c => c.Model, Series())
            .Add(c => c.OnRemoveRepository, alias => removed = alias));
        card.Find("[aria-label='Remove innovadis-dev/Fincent']").Click();

        Assert.Equal("innovadis-dev/Fincent", removed);

        var single = context.Render<SprintSeriesCard>(parameters => parameters.Add(c => c.Model, Series(repositories: ["JSdotNet/Backlog"])));
        Assert.True(single.Find("[aria-label='Remove JSdotNet/Backlog']").HasAttribute("disabled"));
    }

    [Fact]
    public void Length_and_start_weekday_raise_their_choices()
    {
        using var context = new BunitContext();
        int? weeks = null;
        DayOfWeek? weekday = null;

        var card = context.Render<SprintSeriesCard>(parameters => parameters
            .Add(c => c.Model, Series())
            .Add(c => c.OnLengthChanged, value => weeks = value)
            .Add(c => c.OnStartWeekdayChanged, value => weekday = value));
        card.Find("[data-testid='sprint-core-length-1']").Click();
        card.Find("select#sprint-core-start").Change("Wednesday");

        Assert.Equal(1, weeks);
        Assert.Equal(DayOfWeek.Wednesday, weekday);
    }

    [Fact]
    public void A_committed_pattern_is_raised_and_a_pattern_without_its_number_is_held_back()
    {
        using var context = new BunitContext();
        var raised = new List<string>();

        var card = context.Render<SprintSeriesCard>(parameters => parameters
            .Add(c => c.Model, Series())
            .Add(c => c.OnNamePatternChanged, value => raised.Add(value)));
        var field = card.Find("[data-testid='sprint-core-name']");

        field.Input("Fincent {n}");
        Assert.Equal("next: Fincent 43", card.Find("[data-testid='sprint-core-next']").TextContent);
        field.Change("Fincent {n}");
        card.Find("[data-testid='sprint-core-name']").Change("Fincent");

        Assert.Equal(["Fincent {n}"], raised);
        Assert.Contains("{n}", card.Find("[data-testid='sprint-core-next']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Ticking_a_rule_that_names_nothing_holds_it_back_until_a_plan_is_added()
    {
        using var context = new BunitContext();
        var raised = new List<SprintGoalSettings>();

        var card = context.Render<SprintSeriesCard>(parameters => parameters
            .Add(c => c.Model, Series())
            .Add(c => c.OnGoalChanged, goal => raised.Add(goal)));
        card.Find("[data-testid='sprint-core-goal-plans'] input").Change(true);

        Assert.False(raised.Single().InPlans);
        Assert.NotEmpty(card.FindAll("[data-testid='sprint-core-add-plan']"));

        card.Find("[data-testid='sprint-core-add-plan']").Change("+progress-sprints");

        Assert.True(raised[^1].InPlans);
        Assert.Equal(["progress-sprints"], raised[^1].Plans);
        Assert.True(raised[^1].Everything);
    }

    [Fact]
    public void A_tag_rule_counts_once_it_names_a_tag_and_unticks_when_its_last_tag_goes()
    {
        using var context = new BunitContext();
        var raised = new List<SprintGoalSettings>();
        var goal = SprintGoalSettings.Default with { Tagged = true, Tags = ["release"] };

        var card = context.Render<SprintSeriesCard>(parameters => parameters
            .Add(c => c.Model, Series(goal: goal))
            .Add(c => c.OnGoalChanged, value => raised.Add(value)));
        card.Find("[aria-label='Remove release']").Click();

        Assert.False(raised.Single().Tagged);
        Assert.Empty(raised.Single().Tags);
    }

    [Fact]
    public void Goal_met_by_a_share_raises_the_share_and_back_to_every_goal_task_raises_100()
    {
        using var context = new BunitContext();
        var raised = new List<SprintGoalSettings>();

        var card = context.Render<SprintSeriesCard>(parameters => parameters
            .Add(c => c.Model, Series())
            .Add(c => c.OnGoalChanged, value => raised.Add(value)));
        card.Find("select#sprint-core-met").Change("share");
        card.Find("[data-testid='sprint-core-share']").Change("70");
        card.Find("select#sprint-core-met").Change("every");

        Assert.Equal([80, 70, 100], raised.Select(goal => goal.Share));
    }

    [Fact]
    public void Done_means_is_said_as_the_rule_it_is()
    {
        using var context = new BunitContext();

        var card = context.Render<SprintSeriesCard>(parameters => parameters.Add(c => c.Model, Series()));

        var done = card.Find("[data-testid='sprint-core-done']").TextContent;
        Assert.Contains("set to Done", done, StringComparison.Ordinal);
        Assert.Contains("every pull request linked to it is merged", done, StringComparison.Ordinal);
    }

    [Fact]
    public void Show_it_in_raises_both_briefs()
    {
        using var context = new BunitContext();
        (bool Daily, bool Weekly)? raised = null;

        var card = context.Render<SprintSeriesCard>(parameters => parameters
            .Add(c => c.Model, Series())
            .Add(c => c.OnShowInChanged, value => raised = value));
        card.Find("[data-testid='sprint-core-weekly'] input").Change(true);

        Assert.Equal((true, true), raised);
    }

    [Fact]
    public void A_series_switched_off_says_so_on_the_card()
    {
        using var context = new BunitContext();

        var card = context.Render<SprintSeriesCard>(parameters => parameters.Add(c => c.Model, Series(isOn: false)));

        Assert.Contains("sprint-card--off", card.Find("section").ClassName, StringComparison.Ordinal);
        Assert.Equal("false", card.Find("[role='switch']").GetAttribute("aria-checked"));
    }

    [Fact]
    public void A_half_typed_pattern_survives_the_host_drawing_the_same_series_again()
    {
        using var context = new BunitContext();
        var card = context.Render<SprintSeriesCard>(parameters => parameters.Add(c => c.Model, Series()));

        card.Find("[data-testid='sprint-core-name']").Input("Fincent {");
        card.Render(parameters => parameters.Add(c => c.Model, Series()));

        Assert.Equal("Fincent {", card.Find("[data-testid='sprint-core-name']").GetAttribute("value"));
    }

    [Fact]
    public void A_pattern_held_back_stays_with_its_reason_when_something_else_on_the_card_is_saved()
    {
        using var context = new BunitContext();
        var card = context.Render<SprintSeriesCard>(parameters => parameters.Add(c => c.Model, Series()));

        card.Find("[data-testid='sprint-core-name']").Change("Fincent");
        card.Render(parameters => parameters.Add(c => c.Model, Series() with { Weeks = 1 }));

        Assert.Equal("Fincent", card.Find("[data-testid='sprint-core-name']").GetAttribute("value"));
        Assert.Contains("{n}", card.Find("[data-testid='sprint-core-next']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_brief_ticked_before_the_first_is_saved_keeps_the_first()
    {
        using var context = new BunitContext();
        var raised = new List<(bool Daily, bool Weekly)>();
        var model = Series() with { ShowInDaily = false, ShowInWeekly = false };

        var card = context.Render<SprintSeriesCard>(parameters => parameters
            .Add(c => c.Model, model)
            .Add(c => c.OnShowInChanged, value => raised.Add(value)));
        card.Find("[data-testid='sprint-core-daily'] input").Change(true);
        card.Find("[data-testid='sprint-core-weekly'] input").Change(true);

        Assert.Equal([(true, false), (true, true)], raised);
    }

    [Fact]
    public void A_share_out_of_range_is_held_back_with_the_reason()
    {
        using var context = new BunitContext();
        var raised = new List<SprintGoalSettings>();
        var card = context.Render<SprintSeriesCard>(parameters => parameters
            .Add(c => c.Model, Series(goal: SprintGoalSettings.Default with { Share = 80 }))
            .Add(c => c.OnGoalChanged, value => raised.Add(value)));

        card.Find("[data-testid='sprint-core-share']").Change("100");

        Assert.Empty(raised);
        Assert.NotEmpty(card.FindAll("[data-testid='sprint-core-share-hint']"));
    }

    [Theory]
    [InlineData("Sprint {n}", 4, "Sprint 4")]
    [InlineData("  Core {n} ", 12, "Core 12")]
    [InlineData("Sprint", 4, null)]
    [InlineData("{n}", 4, null)]
    [InlineData("{n} and {n}", 4, null)]
    public void The_next_name_follows_the_pattern_rule(string pattern, int number, string? expected)
    {
        Assert.Equal(expected, SprintSeriesCardModel.NextName(pattern, number));
    }

    // --- Delete series… (.devbook/domain/progress/features.md#deleting-a-series)

    [Fact]
    public void A_card_whose_host_cannot_delete_offers_no_delete()
    {
        using var context = new BunitContext();

        var card = context.Render<SprintSeriesCard>(parameters => parameters.Add(c => c.Model, Series()));

        Assert.Empty(card.FindAll("[data-testid='sprint-core-delete']"));
    }

    [Fact]
    public async Task Delete_asks_first_naming_the_sprints_and_the_repositories_it_frees()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var deleted = 0;
        var card = context.Render<SprintSeriesCard>(parameters => parameters
            .Add(c => c.Model, Series())
            .Add(c => c.OnDelete, () => deleted++));

        await card.Find("[data-testid='sprint-core-delete']").ClickAsync(new MouseEventArgs());

        Assert.Equal(0, deleted);
        Assert.Equal("Delete Fincent team?", card.Find(".modal__title").TextContent.Trim());
        Assert.Equal(
            "Its 42 sprints are deleted with it, with their goals, notes and outcomes. innovadis-dev/Fincent, JSdotNet/devbook-marketplace go back to no sprint. No task changes, and it is deleted on every desktop you have paired. To keep the sprints, switch the series off instead.",
            card.Find(".confirm-dialog__message").TextContent);
        Assert.Equal("Delete series", card.Find("[data-testid='sprint-core-delete-confirm']").TextContent.Trim());
    }

    [Fact]
    public async Task Confirming_raises_delete_once()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var deleted = 0;
        var card = context.Render<SprintSeriesCard>(parameters => parameters
            .Add(c => c.Model, Series())
            .Add(c => c.OnDelete, () => deleted++));

        await card.Find("[data-testid='sprint-core-delete']").ClickAsync(new MouseEventArgs());
        await card.Find("[data-testid='sprint-core-delete-confirm']").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, deleted);
        Assert.Empty(card.FindAll(".confirm-dialog__message"));
    }

    [Fact]
    public async Task Cancelling_raises_nothing()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var deleted = 0;
        var card = context.Render<SprintSeriesCard>(parameters => parameters
            .Add(c => c.Model, Series())
            .Add(c => c.OnDelete, () => deleted++));

        await card.Find("[data-testid='sprint-core-delete']").ClickAsync(new MouseEventArgs());
        await card.Find("[data-testid='sprint-core-delete-cancel']").ClickAsync(new MouseEventArgs());

        Assert.Equal(0, deleted);
        Assert.Empty(card.FindAll(".confirm-dialog__message"));
    }

    [Fact]
    public async Task A_series_with_no_sprint_and_one_repository_says_so()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var card = context.Render<SprintSeriesCard>(parameters => parameters
            .Add(c => c.Model, Series(repositories: ["backlog"]) with { Running = null, NextNumber = 1 })
            .Add(c => c.OnDelete, () => { }));

        await card.Find("[data-testid='sprint-core-delete']").ClickAsync(new MouseEventArgs());

        Assert.StartsWith(
            "It has no sprint yet. backlog goes back to no sprint. No task changes,",
            card.Find(".confirm-dialog__message").TextContent,
            StringComparison.Ordinal);
    }
}
