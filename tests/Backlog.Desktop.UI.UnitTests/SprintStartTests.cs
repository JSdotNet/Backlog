using Backlog.Modules.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Extensions;
using Backlog.Modules.Progress.UI;
using Backlog.Modules.Progress.UI.InProgress;
using Backlog.SharedKernel;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Starting a series' first sprint and wording the open sprint's goal, over the real
/// Progress module and an in-memory store
/// (<c>.devbook/domain/progress/features.md#setting-up-a-sprint-series</c>,
/// <c>#setting-the-sprint-goal</c>): Start Sprint 1 on a series' Settings › Sprints card
/// and on the empty sprint view, each one StartSprintAsync from the start day the dialog
/// offers; and Edit goal on the open sprint's goal card, one SetSprintGoalAsync. A
/// series created on the settings page is started and its goal edited with nothing put
/// in the store by hand.
/// </summary>
public sealed class SprintStartTests : IDisposable
{
    /// <summary>A Thursday: the dialog offers the Monday after.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly BunitContext _context = new();
    private readonly Store _store = new();
    private readonly Switch _features = new() { On = true };

    public SprintStartTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton<IAppFeatureSettings>(_features);
        _context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        _context.Services.AddSingleton<ISprintSeriesRepository>(_store);
        _context.Services.AddSingleton<ISprintTaskSource>(new NoTasks());
        _context.Services.AddSingleton<ISprintRepositories>(new Registry("backlog", "docs"));
        _context.Services.AddProgressModule();
    }

    private ISprintSeriesPlanning Planning => _context.Services.GetRequiredService<ISprintSeriesPlanning>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Settings › Sprints -------------------------------------------------------------

    [Fact]
    public async Task A_series_added_on_the_settings_page_starts_its_first_sprint_from_its_card()
    {
        var page = _context.Render<SprintSettings>();
        page.WaitForAssertion(() => page.Find("[data-testid=sprint-settings-add]"));
        await page.Find("[data-testid=sprint-settings-add]").ClickAsync(new MouseEventArgs());
        page.Find("[data-testid=sprint-settings-new-repository] select").Change("backlog");
        var series = Assert.Single(await _store.ListAsync(Ct));
        var card = $"sprint-series-{series.Id:N}";
        page.WaitForAssertion(() => Assert.Equal("No sprint has started yet.", page.Find($"[data-testid={card}-running]").TextContent));

        await page.Find($"[data-testid={card}-start]").ClickAsync(new MouseEventArgs());
        Assert.Equal("2026-10-19", page.Find("[data-testid=sprint-start-day] input").GetAttribute("value"));
        await page.Find("[data-testid=sprint-start-goal] input").InputAsync(new ChangeEventArgs { Value = "Ship the ledger" });
        await page.Find("[data-testid=sprint-start-confirm]").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Assert.Equal("Sprint 1 runs Mon 19 Oct to Sun 1 Nov", page.Find($"[data-testid={card}-running]").TextContent));
        Assert.Empty(page.FindAll($"[data-testid={card}-start]"));
        Assert.Empty(page.FindAll("[data-testid=sprint-start-dialog]"));
        var open = (await Planning.GetSeriesAsync(series.Id, Ct)).Value.OpenSprint!;
        Assert.Equal((1, new DateOnly(2026, 10, 19), "Ship the ledger"), (open.Number, open.Start, open.Goal));
    }

    [Fact]
    public async Task A_switched_off_series_offers_no_start()
    {
        var series = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["backlog"]), Ct)).Value;
        await Planning.SwitchAsync(series.Id, false, Ct);

        var page = _context.Render<SprintSettings>();

        page.WaitForAssertion(() => page.Find($"[data-testid=sprint-series-{series.Id:N}]"));
        Assert.Empty(page.FindAll($"[data-testid=sprint-series-{series.Id:N}-start]"));
    }

    // ---- The sprint view ----------------------------------------------------------------

    [Fact]
    public async Task A_series_with_no_sprint_carries_the_switch_and_its_sprint_side_starts_the_first()
    {
        var series = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["backlog"], NamePattern: "Core {n}"), Ct)).Value;
        var view = RenderView();

        view.WaitForAssertion(() => Assert.Contains("Core 1", view.Find("[data-testid=in-progress-view-switch-sprint]").TextContent));
        await view.Find("[data-testid=in-progress-view-switch-sprint]").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => view.Find("[data-testid=in-progress-sprint-not-started]"));
        Assert.Empty(view.FindAll("[data-testid=sprint-board-reading]"));
        Assert.Empty(view.FindAll("[data-testid=in-progress-sprint-close]"));

        await view.Find("[data-testid=in-progress-sprint-start]").ClickAsync(new MouseEventArgs());
        await view.Find("[data-testid=sprint-start-goal] input").InputAsync(new ChangeEventArgs { Value = "First cut" });
        await view.Find("[data-testid=sprint-start-confirm]").ClickAsync(new MouseEventArgs());

        view.WaitForAssertion(() => Assert.Equal("First cut", view.Find("[data-testid=sprint-board-goal-text]").TextContent));
        Assert.Empty(view.FindAll("[data-testid=in-progress-sprint-not-started]"));
        Assert.Equal("Core 1", (await Planning.GetSeriesAsync(series.Id, Ct)).Value.OpenSprint!.Name);
    }

    [Fact]
    public async Task Edit_goal_on_the_open_sprint_writes_the_goal_through_the_module()
    {
        var series = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["backlog"]), Ct)).Value;
        await Planning.StartSprintAsync(series.Id, new DateOnly(2026, 10, 12), string.Empty, Ct);
        var view = RenderView();
        view.WaitForAssertion(() => view.Find("[data-testid=in-progress-view-switch-sprint]"));
        await view.Find("[data-testid=in-progress-view-switch-sprint]").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.Equal("Set goal", view.Find("[data-testid=sprint-board-goal-edit]").TextContent.Trim()));

        await view.Find("[data-testid=sprint-board-goal-edit]").ClickAsync(new MouseEventArgs());
        await view.Find("[data-testid=sprint-goal-text] input").InputAsync(new ChangeEventArgs { Value = "  Ship the ledger  " });
        await view.Find("[data-testid=sprint-goal-save]").ClickAsync(new MouseEventArgs());

        view.WaitForAssertion(() => Assert.Equal("Ship the ledger", view.Find("[data-testid=sprint-board-goal-text]").TextContent));
        Assert.Equal("Edit goal", view.Find("[data-testid=sprint-board-goal-edit]").TextContent.Trim());
        Assert.Empty(view.FindAll("[data-testid=sprint-goal-dialog]"));
        Assert.Equal("Ship the ledger", (await Planning.GetSeriesAsync(series.Id, Ct)).Value.OpenSprint!.Goal);
    }

    [Fact]
    public async Task A_closed_sprints_goal_card_only_reads()
    {
        var series = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["backlog"]), Ct)).Value;
        await Planning.StartSprintAsync(series.Id, new DateOnly(2026, 9, 28), "First cut", Ct);
        Assert.True((await Planning.CloseSprintAsync(series.Id, new Dictionary<Guid, CloseOutcome>(), "Second cut", Ct)).IsSuccess);
        var view = RenderView();
        view.WaitForAssertion(() => view.Find("[data-testid=in-progress-view-switch-sprint]"));
        await view.Find("[data-testid=in-progress-view-switch-sprint]").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.Equal("Second cut", view.Find("[data-testid=sprint-board-goal-text]").TextContent));
        Assert.Single(view.FindAll("[data-testid=sprint-board-goal-edit]"));

        await view.Find("[data-testid=sprint-board-previous]").ClickAsync(new MouseEventArgs());

        view.WaitForAssertion(() => Assert.Equal("First cut", view.Find("[data-testid=sprint-board-goal-text]").TextContent));
        Assert.Empty(view.FindAll("[data-testid=sprint-board-goal-edit]"));
    }

    [Fact]
    public async Task With_every_repository_in_scope_a_series_that_runs_a_sprint_comes_before_one_that_has_not_started()
    {
        await Planning.CreateSeriesAsync(new CreateSprintSeries(["backlog"], NamePattern: "Waiting {n}"), Ct);
        var running = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["docs"], NamePattern: "Docs {n}"), Ct)).Value;
        await Planning.StartSprintAsync(running.Id, new DateOnly(2026, 10, 12), "Docs first", Ct);

        var view = RenderView([]);

        view.WaitForAssertion(() => Assert.Contains("Docs 1", view.Find("[data-testid=in-progress-view-switch-sprint]").TextContent));
    }

    private IRenderedComponent<InProgressSprints> RenderView(IReadOnlyList<string>? repositories = null) =>
        _context.Render<InProgressSprints>(parameters => parameters
            .Add(p => p.Repositories, repositories ?? ["backlog"])
            .Add(p => p.Now, (RenderFragment<RenderFragment?>)(viewSwitch => builder => builder.AddContent(0, viewSwitch))));

    public void Dispose() => _context.Dispose();

    private sealed class Store : ISprintSeriesRepository
    {
        private readonly List<SprintSeries> _all = [];

        public Task<IReadOnlyList<SprintSeries>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SprintSeries>>([.. _all]);

        public Task<SprintSeries?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_all.FirstOrDefault(series => series.Id == id));

        public Task SaveAsync(SprintSeries series, CancellationToken cancellationToken = default)
        {
            if (!_all.Contains(series)) _all.Add(series);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _all.RemoveAll(series => series.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class NoTasks : ISprintTaskSource
    {
        public Task<IReadOnlyList<SprintTaskDto>> ReadAsync(SprintTaskQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SprintTaskDto>>([]);
    }

    private sealed class Registry(params string[] aliases) : ISprintRepositories
    {
        public IReadOnlyList<string> Aliases { get; } = aliases;
    }

    private sealed class Switch : IAppFeatureSettings
    {
        public bool On { get; set; }

        public event Action? Changed { add { } remove { } }

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "features.json";

        public bool IsEnabled(string key) => key == ProgressFeatures.Progress && On;

        public string? SetEnabled(string key, bool enabled)
        {
            On = enabled;
            return null;
        }
    }
}
