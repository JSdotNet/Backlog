using Backlog.Modules.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Extensions;
using Backlog.Modules.Progress.UI;
using Backlog.SharedKernel;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Settings › Sprints over the real Progress module and an in-memory store: a card per
/// series, the repositories in no series, Add another sprint, and every change made
/// through the planning port — refused ones said on the card. Offered only while the
/// progress switch is on.
/// </summary>
public sealed class SprintSettingsTests : IDisposable
{
    private readonly BunitContext _context = new();
    private readonly Store _store = new();
    private readonly Switch _features = new() { On = true };
    private readonly Registry _registry = new("backlog", "docs", "tools");

    public SprintSettingsTests()
    {
        _context.Services.AddSingleton<IAppFeatureSettings>(_features);
        _context.Services.AddSingleton<ISprintSeriesRepository>(_store);
        _context.Services.AddSingleton<ISprintTaskSource>(new NoTasks());
        _context.Services.AddSingleton<ISprintRepositories>(_registry);
        _context.Services.AddProgressModule();
    }

    private ISprintSeriesPlanning Planning => _context.Services.GetRequiredService<ISprintSeriesPlanning>();

    private static string Card(SprintSeriesDto series) => $"sprint-series-{series.Id:N}";

    private async Task<SprintSeriesDto> CreateAsync(params string[] repositories) =>
        (await Planning.CreateSeriesAsync(new CreateSprintSeries(repositories), TestContext.Current.CancellationToken)).Value;

    [Fact]
    public void The_page_is_registered_behind_the_progress_switch()
    {
        var services = new ServiceCollection().AddProgressSettings();

        var section = Assert.Single(services.Select(descriptor => descriptor.ImplementationInstance).OfType<SettingsSection>());
        Assert.Equal("sprints", section.Id);
        Assert.Equal("Sprints", section.Title);
        Assert.Equal(typeof(SprintSettings), section.Component);
        Assert.Equal(ProgressFeatures.Progress, section.FeatureKey);
    }

    [Fact]
    public void With_progress_off_nothing_is_drawn()
    {
        _features.On = false;

        var page = _context.Render<SprintSettings>();

        Assert.Empty(page.Markup.Trim());
    }

    [Fact]
    public async Task Draws_a_card_per_series_and_lists_the_repositories_in_no_series()
    {
        var core = await CreateAsync("backlog");

        var page = _context.Render<SprintSettings>();

        page.WaitForAssertion(() => Assert.Single(page.FindAll($"[data-testid='{Card(core)}']")));
        Assert.Equal(
            ["docs", "tools"],
            page.FindAll("[data-testid='sprint-settings-free-repositories'] .tag-chip__label").Select(chip => chip.TextContent));
        Assert.Equal(
            ["", "docs", "tools"],
            page.FindAll($"[data-testid='{Card(core)}-add-repository'] option").Select(option => option.GetAttribute("value")));
    }

    [Fact]
    public async Task Adding_a_repository_moves_it_out_of_not_in_a_sprint()
    {
        var core = await CreateAsync("backlog");
        var page = _context.Render<SprintSettings>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll($"[data-testid='{Card(core)}']")));

        page.Find($"[data-testid='{Card(core)}-add-repository'] select").Change("docs");

        page.WaitForAssertion(() => Assert.Equal(
            ["tools"],
            page.FindAll("[data-testid='sprint-settings-free-repositories'] .tag-chip__label").Select(chip => chip.TextContent)));
        Assert.Equal(["backlog", "docs"], (await Planning.GetSeriesAsync(core.Id, TestContext.Current.CancellationToken)).Value.Repositories);
    }

    [Fact]
    public async Task Switching_a_series_off_is_kept()
    {
        var core = await CreateAsync("backlog");
        var page = _context.Render<SprintSettings>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll($"[data-testid='{Card(core)}']")));

        page.Find($"[data-testid='{Card(core)}-switch'] [role='switch']").Click();

        page.WaitForAssertion(() => Assert.Equal("false", page.Find($"[data-testid='{Card(core)}-switch'] [role='switch']").GetAttribute("aria-checked")));
        Assert.False((await Planning.GetSeriesAsync(core.Id, TestContext.Current.CancellationToken)).Value.IsOn);
    }

    [Fact]
    public async Task Length_weekday_pattern_goal_and_briefs_are_written_to_the_series()
    {
        var core = await CreateAsync("backlog");
        var page = _context.Render<SprintSettings>();
        var card = Card(core);
        page.WaitForAssertion(() => Assert.Single(page.FindAll($"[data-testid='{card}']")));

        page.Find($"[data-testid='{card}-length-1']").Click();
        page.WaitForAssertion(() => Assert.Equal("true", page.Find($"[data-testid='{card}-length-1']").GetAttribute("aria-checked")));
        page.Find($"select#{card}-start").Change("Wednesday");
        page.WaitForAssertion(() => Assert.Equal("Wednesday", page.Find($"select#{card}-start").GetAttribute("value")));
        page.Find($"[data-testid='{card}-name']").Change("Core {n}");
        page.WaitForAssertion(() => Assert.Equal("next: Core 1", page.Find($"[data-testid='{card}-next']").TextContent));
        page.Find($"[data-testid='{card}-goal-marked'] input").Change(true);
        page.WaitForAssertion(() => Assert.True(page.Find($"[data-testid='{card}-goal-marked'] input").HasAttribute("checked")));
        page.Find($"[data-testid='{card}-weekly'] input").Change(true);

        var ct = TestContext.Current.CancellationToken;
        page.WaitForAssertion(() => Assert.True(Planning.GetSeriesAsync(core.Id, ct).Result.Value.ShowInWeeklyBrief));
        var stored = (await Planning.GetSeriesAsync(core.Id, ct)).Value;
        Assert.Equal(SprintLength.OneWeek, stored.Length);
        Assert.Equal(DayOfWeek.Wednesday, stored.StartWeekday);
        Assert.Equal("Core {n}", stored.NamePattern);
        Assert.Contains(stored.GoalRules, rule => rule.Kind == GoalRuleKind.MarkedAsGoal);
        Assert.Contains(stored.GoalRules, rule => rule.Kind == GoalRuleKind.Everything);
    }

    [Fact]
    public async Task A_weekday_picked_before_the_length_is_saved_keeps_the_new_length()
    {
        var core = await CreateAsync("backlog");
        var page = _context.Render<SprintSettings>();
        var card = Card(core);
        page.WaitForAssertion(() => Assert.Single(page.FindAll($"[data-testid='{card}']")));

        _store.HoldNextSave();
        _ = page.Find($"[data-testid='{card}-length-1']").ClickAsync(new());
        _ = page.Find($"select#{card}-start").ChangeAsync(new() { Value = "Wednesday" });
        _store.Release();

        var ct = TestContext.Current.CancellationToken;
        page.WaitForAssertion(() =>
        {
            var stored = Planning.GetSeriesAsync(core.Id, ct).Result.Value;
            Assert.Equal(SprintLength.OneWeek, stored.Length);
            Assert.Equal(DayOfWeek.Wednesday, stored.StartWeekday);
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Removing_a_series_last_repository_is_refused_on_its_card()
    {
        var core = await CreateAsync("backlog");
        await Planning.AddRepositoryAsync(core.Id, "docs", TestContext.Current.CancellationToken);
        var page = _context.Render<SprintSettings>();
        var card = Card(core);
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll($"[data-testid='{card}-repository']").Count));

        page.Find("[aria-label='Remove docs']").Click();
        page.WaitForAssertion(() => Assert.Single(page.FindAll($"[data-testid='{card}-repository']")));

        Assert.True(page.Find("[aria-label='Remove backlog']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Add_another_sprint_starts_a_series_over_the_repository_picked()
    {
        await CreateAsync("backlog");
        var page = _context.Render<SprintSettings>();
        page.WaitForAssertion(() => Assert.Equal("Add another sprint", page.Find("[data-testid='sprint-settings-add']").TextContent.Trim()));

        page.Find("[data-testid='sprint-settings-add']").Click();
        page.Find("[data-testid='sprint-settings-new-repository'] select").Change("tools");

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("section.sprint-card").Count));
        var all = (await Planning.ListSeriesAsync(TestContext.Current.CancellationToken)).Value;
        Assert.Equal(["tools"], all[1].Repositories);
        Assert.Equal(
            ["docs"],
            page.FindAll("[data-testid='sprint-settings-free-repositories'] .tag-chip__label").Select(chip => chip.TextContent));
    }

    [Fact]
    public async Task With_every_repository_in_a_series_add_another_sprint_says_so()
    {
        await CreateAsync("backlog", "docs", "tools");

        var page = _context.Render<SprintSettings>();

        page.WaitForAssertion(() => Assert.True(page.Find("[data-testid='sprint-settings-add']").HasAttribute("disabled")));
        Assert.Contains("Every repository is in a sprint.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_running_line_and_the_next_number_come_from_the_series()
    {
        var open = new SprintDto(42, "Sprint 42", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 18), "Goal", true, [], []);
        var series = new SprintSeriesDto(
            Guid.NewGuid(), "Core", ["backlog"], SprintLength.TwoWeeks, DayOfWeek.Monday, "Sprint {n}",
            false, false, [GoalRuleDto.InPlans("rekeningen"), GoalRuleDto.Tagged("release")], 80, [open]);

        var card = SprintSettings.ToCard(series);

        Assert.Equal("Sprint 42 runs Mon 5 Oct to Sun 18 Oct", card.Running);
        Assert.Equal(43, card.NextNumber);
        Assert.Equal(2, card.Weeks);
        Assert.True(card.Goal.InPlans);
        Assert.Equal(["rekeningen"], card.Goal.Plans);
        Assert.True(card.Goal.Tagged);
        Assert.False(card.Goal.Everything);
        Assert.Equal(80, card.Goal.Share);
    }

    /// <summary>Delete series…, confirmed, takes the card away and puts its repositories
    /// back under Not in a sprint, where Add another sprint can start a series over them
    /// (<c>.devbook/domain/progress/requirements.md#requirement-a-deleted-series-frees-its-repositories</c>).</summary>
    [Fact]
    public async Task A_confirmed_delete_removes_the_card_and_frees_its_repositories()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        var ct = TestContext.Current.CancellationToken;
        var core = await CreateAsync("backlog", "docs");
        var other = await CreateAsync("tools");
        await Planning.StartSprintAsync(core.Id, new DateOnly(2026, 10, 12), "Ship it", ct);
        var page = _context.Render<SprintSettings>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll($"[data-testid='{Card(core)}']")));

        await page.Find($"[data-testid='{Card(core)}-delete']").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Assert.Contains("Its one sprint is deleted with it", page.Markup, StringComparison.Ordinal));
        await page.Find($"[data-testid='{Card(core)}-delete-confirm']").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Assert.Empty(page.FindAll($"[data-testid='{Card(core)}']")));
        Assert.Single(page.FindAll($"[data-testid='{Card(other)}']"));
        Assert.Equal(
            ["backlog", "docs"],
            page.FindAll("[data-testid='sprint-settings-free-repositories'] .tag-chip__label").Select(chip => chip.TextContent));
        Assert.Equal([other.Id], (await Planning.ListSeriesAsync(ct)).Value.Select(series => series.Id));
    }

    /// <summary>The series went while the confirmation was open — a pull from another
    /// desktop deleted it — so confirming finds it gone: the card goes with nothing said,
    /// since that is what the person asked for.</summary>
    [Fact]
    public async Task Confirming_a_delete_another_desktop_already_made_removes_the_card_quietly()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        var core = await CreateAsync("backlog");
        var page = _context.Render<SprintSettings>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll($"[data-testid='{Card(core)}']")));

        await page.Find($"[data-testid='{Card(core)}-delete']").ClickAsync(new MouseEventArgs());
        await Planning.DeleteSeriesAsync(core.Id, TestContext.Current.CancellationToken);
        await page.Find($"[data-testid='{Card(core)}-delete-confirm']").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Assert.Empty(page.FindAll($"[data-testid='{Card(core)}']")));
        Assert.Empty(page.FindAll("[data-testid='sprint-settings-error']"));
        Assert.Contains("backlog", page.FindAll("[data-testid='sprint-settings-free-repositories'] .tag-chip__label").Select(chip => chip.TextContent));
    }

    [Fact]
    public async Task A_cancelled_delete_keeps_the_series()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        var core = await CreateAsync("backlog");
        var page = _context.Render<SprintSettings>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll($"[data-testid='{Card(core)}']")));

        await page.Find($"[data-testid='{Card(core)}-delete']").ClickAsync(new MouseEventArgs());
        await page.Find($"[data-testid='{Card(core)}-delete-cancel']").ClickAsync(new MouseEventArgs());

        Assert.Single(page.FindAll($"[data-testid='{Card(core)}']"));
        Assert.True((await Planning.GetSeriesAsync(core.Id, TestContext.Current.CancellationToken)).IsSuccess);
    }

    public void Dispose() => _context.Dispose();

    private sealed class Store : ISprintSeriesRepository
    {
        private readonly List<SprintSeries> _all = [];
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _holdNext;

        /// <summary>Makes the next save wait until <see cref="Release"/>.</summary>
        public void HoldNextSave() => _holdNext = true;

        public void Release() => _released.TrySetResult();

        public Task<IReadOnlyList<SprintSeries>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SprintSeries>>([.. _all]);

        public Task<SprintSeries?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_all.FirstOrDefault(series => series.Id == id));

        public async Task SaveAsync(SprintSeries series, CancellationToken cancellationToken = default)
        {
            if (_holdNext)
            {
                _holdNext = false;
                await _released.Task;
            }

            if (!_all.Contains(series)) _all.Add(series);
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

        public event Action? Changed;

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "features.json";

        public bool IsEnabled(string key) => key == ProgressFeatures.Progress && On;

        public string? SetEnabled(string key, bool enabled)
        {
            On = enabled;
            Changed?.Invoke();
            return null;
        }
    }
}
