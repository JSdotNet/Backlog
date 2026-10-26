using Backlog.UI.Components.Goals;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What a host is promised about "Attach to goal"
/// (<c>.devbook/domain/progress/features.md#attaching-work-to-a-goal</c>): the picker lists
/// the open goals with their counts, narrows them to a search, says when there is nothing
/// to choose, hands back the goal chosen or the new goal filled in, refuses to attach to
/// the goal that holds the work already, and says before a move that one is about to happen.
/// </summary>
public sealed class GoalPickerTests : IDisposable
{
    private static readonly GoalOption Launch = new(Guid.NewGuid(), "Launch the desktop app", 3, 8, new DateOnly(2026, 11, 14));
    private static readonly GoalOption Docs = new(Guid.NewGuid(), "Docs for the beta", 0, 0, new DateOnly(2026, 12, 1));
    private static readonly GoalSubject Work = GoalSubject.ForTask(Guid.NewGuid());

    private readonly BunitContext _context = new();

    public GoalPickerTests() => _context.JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void A_goal_reads_its_title_and_how_far_it_has_come_with_its_due_date()
    {
        Assert.Equal("3/8 · due 14 Nov", Launch.Meta);

        var picker = Render();

        var options = picker.FindAll("[data-testid=goal-picker-option]");
        Assert.Equal(2, options.Count);
        Assert.Equal("Launch the desktop app", options[0].QuerySelector(".goal-picker__title")!.TextContent.Trim());
        Assert.Equal("3/8 · due 14 Nov", options[0].QuerySelector(".goal-picker__meta")!.TextContent.Trim());
        Assert.Equal("0/0 · due 1 Dec", options[1].QuerySelector(".goal-picker__meta")!.TextContent.Trim());
    }

    [Fact]
    public async Task The_search_narrows_by_title_whatever_its_case_and_says_when_it_matches_none()
    {
        var picker = Render();

        await picker.Find("[data-testid=goal-picker-search] input").InputAsync(new ChangeEventArgs { Value = "BETA" });

        var option = Assert.Single(picker.FindAll("[data-testid=goal-picker-option]"));
        Assert.Equal("Docs for the beta", option.QuerySelector(".goal-picker__title")!.TextContent.Trim());

        await picker.Find("[data-testid=goal-picker-search] input").InputAsync(new ChangeEventArgs { Value = "nothing like it" });

        Assert.Empty(picker.FindAll("[data-testid=goal-picker-option]"));
        Assert.Single(picker.FindAll("[data-testid=goal-picker-no-match]"));
    }

    [Fact]
    public void With_no_open_goals_it_says_so_and_still_offers_a_new_one()
    {
        var picker = Render(goals: []);

        Assert.Equal("No open goals yet", picker.Find("[data-testid=goal-picker-empty] .empty-state__title").TextContent.Trim());
        Assert.Empty(picker.FindAll("[data-testid=goal-picker-search]"));
        Assert.Single(picker.FindAll("[data-testid=goal-picker-new]"));
    }

    [Fact]
    public async Task Attach_hands_back_the_chosen_goal_and_waits_for_a_choice()
    {
        Guid? attached = null;
        var picker = Render(onAttach: id => attached = id);

        Assert.True(picker.Find("[data-testid=goal-picker-attach]").HasAttribute("disabled"));

        await picker.FindAll("[data-testid=goal-picker-option]")[1].ClickAsync(new MouseEventArgs());
        Assert.Equal("true", picker.FindAll("[data-testid=goal-picker-option]")[1].GetAttribute("aria-pressed"));
        Assert.False(picker.Find("[data-testid=goal-picker-attach]").HasAttribute("disabled"));

        await picker.Find("[data-testid=goal-picker-attach]").ClickAsync(new MouseEventArgs());

        Assert.Equal(Docs.Id, attached);
    }

    [Fact]
    public async Task The_goal_that_holds_the_work_is_marked_attached_and_attaching_to_it_again_is_not_offered()
    {
        var attached = false;
        var picker = Render(current: Launch, onAttach: _ => attached = true);

        var options = picker.FindAll("[data-testid=goal-picker-option]");
        Assert.NotNull(options[0].QuerySelector("[data-testid=goal-picker-attached]"));
        Assert.Null(options[1].QuerySelector("[data-testid=goal-picker-attached]"));
        Assert.Equal("true", options[0].GetAttribute("aria-pressed"));
        Assert.True(picker.Find("[data-testid=goal-picker-attach]").HasAttribute("disabled"));
        Assert.Empty(picker.FindAll("[data-testid=goal-picker-move-note]"));

        await picker.Find("[data-testid=goal-picker-attach]").ClickAsync(new MouseEventArgs());

        Assert.False(attached);
    }

    [Fact]
    public async Task Choosing_another_goal_for_work_a_goal_holds_says_it_will_move()
    {
        var picker = Render(current: Launch, subject: GoalSubject.ForPlan("release-q4"));

        await picker.FindAll("[data-testid=goal-picker-option]")[1].ClickAsync(new MouseEventArgs());

        Assert.Equal(
            "Attaching moves this plan +release-q4 from “Launch the desktop app”.",
            picker.Find("[data-testid=goal-picker-move-note]").TextContent.Trim());
    }

    [Fact]
    public async Task New_goal_hands_back_its_title_and_due_date_once_both_are_filled()
    {
        GoalDraft? created = null;
        var attached = false;
        var picker = Render(onAttach: _ => attached = true, onCreate: draft => created = draft);

        await picker.Find("[data-testid=goal-picker-new]").ClickAsync(new MouseEventArgs());
        Assert.Empty(picker.FindAll("[data-testid=goal-picker-option]"));

        await picker.Find("[data-testid=goal-picker-new-title] input").InputAsync(new ChangeEventArgs { Value = "  Ship the beta  " });
        Assert.True(picker.Find("[data-testid=goal-picker-attach]").HasAttribute("disabled"));

        await picker.Find("[data-testid=goal-picker-new-due] input").InputAsync(new ChangeEventArgs { Value = "2026-12-18" });
        Assert.False(picker.Find("[data-testid=goal-picker-attach]").HasAttribute("disabled"));

        await picker.Find("[data-testid=goal-picker-attach]").ClickAsync(new MouseEventArgs());

        Assert.Equal(new GoalDraft("Ship the beta", new DateOnly(2026, 12, 18)), created);
        Assert.False(attached);
    }

    [Fact]
    public async Task A_new_goal_for_work_a_goal_holds_says_it_will_move()
    {
        var picker = Render(current: Launch);

        await picker.Find("[data-testid=goal-picker-new]").ClickAsync(new MouseEventArgs());

        Assert.Equal(
            "Attaching moves this task from “Launch the desktop app”.",
            picker.Find("[data-testid=goal-picker-move-note]").TextContent.Trim());
    }

    [Fact]
    public async Task Cancel_closes_and_a_reopening_starts_afresh()
    {
        var open = true;
        var picker = Render(openChanged: value => open = value);
        await picker.Find("[data-testid=goal-picker-search] input").InputAsync(new ChangeEventArgs { Value = "beta" });
        await picker.FindAll("[data-testid=goal-picker-option]")[0].ClickAsync(new MouseEventArgs());

        await picker.Find("[data-testid=goal-picker-cancel]").ClickAsync(new MouseEventArgs());
        Assert.False(open);

        picker.Render(parameters => parameters.Add(p => p.Open, false));
        Assert.Empty(picker.FindAll("[data-testid=goal-picker]"));

        picker.Render(parameters => parameters.Add(p => p.Open, true));
        Assert.Equal(2, picker.FindAll("[data-testid=goal-picker-option]").Count);
        Assert.All(picker.FindAll("[data-testid=goal-picker-option]"), option => Assert.Equal("false", option.GetAttribute("aria-pressed")));
    }

    [Fact]
    public void A_refusal_is_shown_in_the_modules_words()
    {
        var picker = Render(error: "That goal is reached.");

        Assert.Equal("That goal is reached.", picker.Find("[data-testid=goal-picker-error]").TextContent.Trim());
    }

    private IRenderedComponent<GoalPicker> Render(
        IReadOnlyList<GoalOption>? goals = null,
        GoalOption? current = null,
        GoalSubject? subject = null,
        string? error = null,
        Action<Guid>? onAttach = null,
        Action<GoalDraft>? onCreate = null,
        Action<bool>? openChanged = null) =>
        _context.Render<GoalPicker>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.OpenChanged, openChanged ?? (_ => { }))
            .Add(p => p.Subject, subject ?? Work)
            .Add(p => p.Goals, goals ?? [Launch, Docs])
            .Add(p => p.CurrentGoal, current)
            .Add(p => p.Error, error)
            .Add(p => p.SearchDebounceMilliseconds, 0)
            .Add(p => p.OnAttach, onAttach ?? (_ => { }))
            .Add(p => p.OnCreate, onCreate ?? (_ => { })));

    public void Dispose() => _context.Dispose();
}
