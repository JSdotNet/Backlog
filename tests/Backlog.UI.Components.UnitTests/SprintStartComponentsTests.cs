using System.Globalization;
using Backlog.UI.Components.Progress;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What a host is promised about starting a series' first sprint and wording the open
/// sprint's goal (<c>.devbook/domain/progress/features.md#setting-up-a-sprint-series</c>,
/// <c>#setting-the-sprint-goal</c>): the start day the dialog offers, the dialog handing
/// back the day and a trimmed goal, the card's Start action only where a switched-on
/// series has no sprint, the board's Edit goal only where the host offers it, and the
/// sprint view's not-started state.
/// </summary>
public sealed class SprintStartComponentsTests : IDisposable
{
    private readonly BunitContext _context = new();

    public SprintStartComponentsTests() => _context.JSInterop.Mode = JSRuntimeMode.Loose;

    // ---- The start day ---------------------------------------------------------------

    [Theory]
    [InlineData("2026-10-12", DayOfWeek.Monday, "2026-10-12")] // today is a start day
    [InlineData("2026-10-15", DayOfWeek.Monday, "2026-10-19")] // a Thursday: the next Monday
    [InlineData("2026-10-17", DayOfWeek.Sunday, "2026-10-18")] // a Saturday: tomorrow
    [InlineData("2026-10-14", DayOfWeek.Tuesday, "2026-10-20")] // a Wednesday: six days on
    public void The_default_start_is_the_next_start_weekday_or_today_when_today_is_one(string today, DayOfWeek weekday, string expected) =>
        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture), SprintStartRules.DefaultStart(DateOnly.Parse(today, CultureInfo.InvariantCulture), weekday));

    // ---- The start dialog ------------------------------------------------------------

    [Fact]
    public void The_start_dialog_names_the_sprint_and_offers_the_default_day()
    {
        var dialog = RenderStart(new DateOnly(2026, 10, 19));

        Assert.Contains("Start Sprint 1", dialog.Find("[role=dialog]").TextContent);
        Assert.Equal("2026-10-19", dialog.Find("[data-testid=sprint-start-day] input").GetAttribute("value"));
        Assert.Contains("Sprints of Fincent team start on a Monday.", dialog.Find("[data-testid=sprint-start-hint]").TextContent);
    }

    [Fact]
    public async Task Start_hands_back_the_day_and_the_goal_trimmed()
    {
        SprintStart? started = null;
        var dialog = RenderStart(new DateOnly(2026, 10, 19), start => started = start);

        await dialog.Find("[data-testid=sprint-start-goal] input").InputAsync(new ChangeEventArgs { Value = "  Ship the ledger  " });
        await dialog.Find("[data-testid=sprint-start-day] input").InputAsync(new ChangeEventArgs { Value = "2026-10-26" });
        await dialog.Find("[data-testid=sprint-start-confirm]").ClickAsync(new MouseEventArgs());

        Assert.Equal(new SprintStart(new DateOnly(2026, 10, 26), "Ship the ledger"), started);
    }

    [Fact]
    public async Task A_day_that_is_not_the_start_weekday_is_held_back_with_the_reason()
    {
        SprintStart? started = null;
        var dialog = RenderStart(new DateOnly(2026, 10, 19), start => started = start);

        await dialog.Find("[data-testid=sprint-start-day] input").InputAsync(new ChangeEventArgs { Value = "2026-10-21" });
        await dialog.Find("[data-testid=sprint-start-confirm]").ClickAsync(new MouseEventArgs());

        Assert.Null(started);
        Assert.Contains("Pick a Monday", dialog.Find("[data-testid=sprint-start-day]").TextContent);
    }

    [Fact]
    public void The_hosts_refusal_shows_in_the_dialog()
    {
        var dialog = _context.Render<SprintStartDialog>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.SprintName, "Sprint 1")
            .Add(p => p.SeriesName, "Fincent team")
            .Add(p => p.StartWeekday, DayOfWeek.Monday)
            .Add(p => p.DefaultStart, new DateOnly(2026, 10, 19))
            .Add(p => p.Error, "Progress is switched off."));

        Assert.Equal("Progress is switched off.", dialog.Find("[data-testid=sprint-start-error]").TextContent.Trim());
    }

    // ---- The goal dialog -------------------------------------------------------------

    [Fact]
    public async Task The_goal_dialog_starts_from_the_current_goal_and_hands_back_the_new_one_trimmed()
    {
        string? saved = null;
        var dialog = _context.Render<SprintGoalDialog>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.SprintName, "Sprint 42")
            .Add(p => p.Current, "Ship the ledger")
            .Add(p => p.OnSave, (string goal) => saved = goal));

        Assert.Equal("Ship the ledger", dialog.Find("[data-testid=sprint-goal-text] input").GetAttribute("value"));

        await dialog.Find("[data-testid=sprint-goal-text] input").InputAsync(new ChangeEventArgs { Value = " Ship the ledger and the brief " });
        await dialog.Find("[data-testid=sprint-goal-save]").ClickAsync(new MouseEventArgs());

        Assert.Equal("Ship the ledger and the brief", saved);
    }

    // ---- The series card -------------------------------------------------------------

    [Fact]
    public async Task A_switched_on_series_with_no_sprint_offers_to_start_its_first()
    {
        var pressed = false;
        var card = _context.Render<SprintSeriesCard>(parameters => parameters
            .Add(p => p.Model, Card(running: null))
            .Add(p => p.OnStartSprint, () => pressed = true));

        var start = card.Find("[data-testid=sprint-core-start]");
        Assert.Equal("Start Sprint 1…", start.TextContent.Trim());

        await start.ClickAsync(new MouseEventArgs());

        Assert.True(pressed);
    }

    [Theory]
    [InlineData(true, "Sprint 42 runs Mon 5 Oct to Fri 16 Oct")]
    [InlineData(false, null)]
    public void A_running_or_switched_off_series_offers_no_start(bool isOn, string? running)
    {
        var card = _context.Render<SprintSeriesCard>(parameters => parameters
            .Add(p => p.Model, Card(running) with { IsOn = isOn })
            .Add(p => p.OnStartSprint, () => { }));

        Assert.Empty(card.FindAll("[data-testid=sprint-core-start]"));
    }

    // ---- The board ---------------------------------------------------------------------

    [Fact]
    public async Task The_goal_card_offers_edit_goal_only_when_the_host_does()
    {
        var reading = _context.Render<SprintBoard>(parameters => parameters.Add(p => p.Model, Board("Ship the ledger")));
        Assert.Empty(reading.FindAll("[data-testid=sprint-board-goal-edit]"));

        var pressed = false;
        var editable = _context.Render<SprintBoard>(parameters => parameters
            .Add(p => p.Model, Board("Ship the ledger"))
            .Add(p => p.OnEditGoal, () => pressed = true));
        var edit = editable.Find("[data-testid=sprint-board-goal-edit]");
        Assert.Equal("Edit goal", edit.TextContent.Trim());

        await edit.ClickAsync(new MouseEventArgs());

        Assert.True(pressed);
    }

    [Fact]
    public void A_sprint_with_no_goal_offers_set_goal()
    {
        var board = _context.Render<SprintBoard>(parameters => parameters
            .Add(p => p.Model, Board(string.Empty))
            .Add(p => p.OnEditGoal, () => { }));

        Assert.Equal("No goal set for this sprint.", board.Find("[data-testid=sprint-board-goal-text]").TextContent);
        Assert.Equal("Set goal", board.Find("[data-testid=sprint-board-goal-edit]").TextContent.Trim());
    }

    [Fact]
    public void A_board_with_nothing_to_read_draws_the_hosts_empty_state_instead_of_a_spinner()
    {
        var board = _context.Render<SprintBoard>(parameters => parameters
            .Add(p => p.Empty, (RenderFragment)(builder => builder.AddMarkupContent(0, "<p data-testid=\"not-started\">No sprint has started yet.</p>"))));

        Assert.Single(board.FindAll("[data-testid=not-started]"));
        Assert.Empty(board.FindAll("[data-testid=sprint-board-reading]"));
    }

    private IRenderedComponent<SprintStartDialog> RenderStart(DateOnly defaultStart, Action<SprintStart>? onStart = null) =>
        _context.Render<SprintStartDialog>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.SprintName, "Sprint 1")
            .Add(p => p.SeriesName, "Fincent team")
            .Add(p => p.StartWeekday, DayOfWeek.Monday)
            .Add(p => p.DefaultStart, defaultStart)
            .Add(p => p.OnStart, (SprintStart start) => onStart?.Invoke(start)));

    private static SprintSeriesCardModel Card(string? running) =>
        new(
            "core",
            "Fincent team",
            IsOn: true,
            running,
            ["innovadis-dev/Fincent"],
            2,
            DayOfWeek.Monday,
            "Sprint {n}",
            NextNumber: running is null ? 1 : 43,
            SprintGoalSettings.Default,
            ShowInDaily: false,
            ShowInWeekly: false);

    private static SprintBoardModel Board(string goal)
    {
        SprintBoardRow[] rows =
        [
            new("1", "Opdrachtbrief", SprintRowState.Done, "rekeningen", "fincent", true, false, [], [], null, false)
        ];

        return new SprintBoardModel(
            "Fincent", "Sprint 42", "Mon 5 Oct – Sun 18 Oct", ["fincent"], false, false, goal,
            new SprintBoardCounts(1, 0, 0, 0), "+rekeningen", false, SprintBoardCounts.Of(rows),
            "Day 7 of 10 · 3 working days left", [0, 1], 10, "Goal met.", ["rekeningen"], rows);
    }

    public void Dispose() => _context.Dispose();
}
