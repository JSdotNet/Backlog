using Backlog.Infrastructure.FileSystem.Dashboard;
using Backlog.Infrastructure.Sqlite.Progress;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Parts;
using Backlog.Modules.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Extensions;
using Backlog.Modules.Progress.UI;
using Backlog.Modules.Progress.UI.InProgress;
using Backlog.Modules.Progress.UI.Planning;
using Backlog.SharedKernel;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A sprint series changed on the other desktop and pulled here by the sync redraws every
/// screen that shows it (<c>.devbook/domain/progress/features.md#syncing-sprints-between-devices</c>):
/// Settings › Sprints, the sprint view, the task's Sprint section and the weekly brief's
/// Sprint block — without the person navigating or pressing Refresh, and without losing
/// what they had typed or chosen in a dialog left open.
/// <para>
/// Two desktops, each over its own SQLite store and the real Progress module. The copy
/// travels the way the sync client carries it: read from the other desktop's
/// <see cref="ISprintSeriesReplication"/> and handed to this one's
/// <see cref="ISprintSeriesReplication.ApplyAsync"/>.
/// </para>
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SprintSyncRefreshTests : IDisposable
{
    private static readonly DateTimeOffset Wednesday = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private static readonly Guid Ledger = Guid.NewGuid();
    private static readonly Guid Specs = Guid.NewGuid();

    private static readonly SprintCandidate LedgerTask = new(Ledger, "Grootboek", ["backlog"], []);

    private readonly string _hereDir = Path.Combine(Path.GetTempPath(), "sprint-sync-refresh-" + Guid.NewGuid().ToString("N"));
    private readonly string _thereDir = Path.Combine(Path.GetTempPath(), "sprint-sync-refresh-" + Guid.NewGuid().ToString("N"));
    private readonly BunitContext _context = new();
    private readonly ServiceProvider _there;
    private readonly IServiceScope _thereScope;

    public SprintSyncRefreshTests()
    {
        Directory.CreateDirectory(_hereDir);
        Directory.CreateDirectory(_thereDir);

        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        Compose(_context.Services, _hereDir);
        _context.Services.AddScoped<ISprintBriefSource>(sp => new ProgressSprintBriefSource(
            sp.GetService<ISprintSeriesPlanning>(),
            sp.GetService<ISprintProgress>(),
            sp.GetService<IAppFeatureSettings>()));

        var there = new ServiceCollection();
        Compose(there, _thereDir);
        _there = there.BuildServiceProvider();
        _thereScope = _there.CreateScope();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>The other desktop, where the person made the change.</summary>
    private ISprintSeriesPlanning There => _thereScope.ServiceProvider.GetRequiredService<ISprintSeriesPlanning>();

    private static void Compose(IServiceCollection services, string dir)
    {
        var store = new SqliteSprintSeriesRepository(dir);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Wednesday));
        services.AddSingleton<IAppFeatureSettings>(new ProgressOn());
        services.AddSingleton<ISprintSeriesRepository>(store);
        services.AddSingleton<ISprintSeriesReplicaStore>(store);
        services.AddSingleton<ISprintTaskSource>(new Tasks());
        services.AddSingleton<ISprintRepositories>(new Registry());
        services.AddProgressModule();
    }

    /// <summary>One sync pull: every series the other desktop holds, offered here.</summary>
    private async Task<IReadOnlyList<SprintSeriesReplicaOutcome>> PullAsync()
    {
        var copies = await _there.GetRequiredService<ISprintSeriesReplication>().ReadAllAsync(Cancellation);
        var here = _context.Services.GetRequiredService<ISprintSeriesReplication>();

        var outcomes = new List<SprintSeriesReplicaOutcome>();
        foreach (var copy in copies) outcomes.Add(await here.ApplyAsync(copy, Cancellation));
        return outcomes;
    }

    /// <summary>A series of the backlog repository, shown in the weekly brief, with its
    /// first sprint running since Monday — set up on the other desktop and pulled here.</summary>
    private async Task<SprintSeriesDto> SeriesAsync(params Guid[] members)
    {
        var series = (await There.CreateSeriesAsync(new CreateSprintSeries(["backlog"], "Core", ShowInWeeklyBrief: true), Cancellation)).Value;
        series = (await There.StartSprintAsync(series.Id, Monday, "Ship sync", Cancellation)).Value;
        foreach (var member in members) series = (await There.AddTaskAsync(series.Id, 1, member, Cancellation)).Value;

        Assert.All(await PullAsync(), outcome => Assert.Equal(SprintSeriesReplicaOutcome.Taken, outcome));
        return series;
    }

    private async Task<IRenderedComponent<InProgressSprints>> OpenSprintViewAsync()
    {
        var view = _context.Render<InProgressSprints>(parameters => parameters
            .Add(p => p.Repositories, ["backlog"])
            .Add(p => p.Now, (RenderFragment<RenderFragment?>)(viewSwitch => builder => builder.AddContent(0, viewSwitch))));
        view.WaitForAssertion(() => view.Find("[data-testid=in-progress-view-switch-sprint]"));
        await view.Find("[data-testid=in-progress-view-switch-sprint]").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => view.Find("[data-testid=in-progress-sprint-close]"));
        return view;
    }

    [Fact]
    public async Task A_series_pulled_from_the_other_desktop_redraws_every_sprint_screen()
    {
        var series = await SeriesAsync();
        var settings = _context.Render<SprintSettings>();
        var view = await OpenSprintViewAsync();
        var section = _context.Render<TaskSprintSection>(parameters => parameters.Add(p => p.Candidate, LedgerTask));
        var brief = _context.Render<BriefSprintPart>(parameters => parameters.Add(p => p.Week, BriefWeek.Of(Wednesday, 0)));

        var title = $"[data-testid='sprint-series-{series.Id:N}'] .sprint-card__title";
        settings.WaitForAssertion(() => Assert.Equal("Core", settings.Find(title).TextContent.Trim()));
        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-testid=sprint-row-title]")));
        section.WaitForAssertion(() => Assert.Equal("Not in Sprint 1.", section.Find("[data-testid=sprint-task-line]").TextContent.Trim()));
        brief.WaitForAssertion(() => Assert.Equal("0", Items(brief)));

        // On the other desktop: the task goes into the sprint and the series is renamed.
        await There.AddTaskAsync(series.Id, 1, Ledger, Cancellation);
        await There.RenameAsync(series.Id, "Platform", "Sprint {n}", Cancellation);

        Assert.Equal([SprintSeriesReplicaOutcome.Taken], await PullAsync());

        settings.WaitForAssertion(() => Assert.Equal("Platform", settings.Find(title).TextContent.Trim()));
        view.WaitForAssertion(() => Assert.Equal(["Grootboek"], view.FindAll("[data-testid=sprint-row-title]").Select(row => row.TextContent.Trim())));
        section.WaitForAssertion(() => Assert.Equal("In Sprint 1, added directly.", section.Find("[data-testid=sprint-task-line]").TextContent.Trim()));
        brief.WaitForAssertion(() => Assert.Equal("1", Items(brief)));

        static string Items(IRenderedComponent<BriefSprintPart> part) =>
            part.Find("[data-testid='dashboard-brief-sprint-figure-items'] dd").TextContent.Trim();
    }

    [Fact]
    public async Task The_close_dialog_keeps_its_choices_and_next_goal_across_a_pull()
    {
        var series = await SeriesAsync(Ledger);
        var view = await OpenSprintViewAsync();

        await view.Find("[data-testid=in-progress-sprint-close]").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid=sprint-close-item]")));
        await view.Find("[data-testid=sprint-close-item-choice] select").ChangeAsync(new ChangeEventArgs { Value = "Drop" });
        await view.Find("[data-testid=sprint-close-next-goal] input").InputAsync(new ChangeEventArgs { Value = "Ship the brief" });

        // Another task joins the sprint on the other desktop while the dialog is open.
        await There.AddTaskAsync(series.Id, 1, Specs, Cancellation);
        Assert.Equal([SprintSeriesReplicaOutcome.Taken], await PullAsync());

        // The dialog now lists it, on Carry, beside the choice already made.
        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll("[data-testid=sprint-close-item]").Count));
        var choices = view.FindAll("[data-testid=sprint-close-item]")
            .ToDictionary(item => item.GetAttribute("data-task-id")!, item => item.QuerySelector("select")!.GetAttribute("value"));
        Assert.Equal("Drop", choices[Ledger.ToString()]);
        Assert.Equal("Carry", choices[Specs.ToString()]);
        Assert.Equal("Ship the brief", view.Find("[data-testid=sprint-close-next-goal] input").GetAttribute("value"));
    }

    [Fact]
    public async Task A_note_being_written_survives_a_pull()
    {
        var series = await SeriesAsync(Ledger);
        var section = _context.Render<TaskSprintSection>(parameters => parameters.Add(p => p.Candidate, LedgerTask));
        section.WaitForAssertion(() => section.Find("[data-testid=sprint-task-edit-note]"));

        await section.Find("[data-testid=sprint-task-edit-note]").ClickAsync(new MouseEventArgs());
        await section.Find("[data-testid=sprint-note-text] input").InputAsync(new ChangeEventArgs { Value = "Demo first" });

        // The task is marked a goal on the other desktop meanwhile.
        await There.MarkGoalAsync(series.Id, 1, Ledger, true, Cancellation);
        Assert.Equal([SprintSeriesReplicaOutcome.Taken], await PullAsync());

        section.WaitForAssertion(() => Assert.Single(section.FindAll("[data-testid=sprint-task-goal]")));
        Assert.Equal("Demo first", section.Find("[data-testid=sprint-note-text] input").GetAttribute("value"));
    }

    [Fact]
    public async Task A_card_keeps_a_pattern_held_back_across_a_pull()
    {
        var series = await SeriesAsync();
        var settings = _context.Render<SprintSettings>();
        var card = $"sprint-series-{series.Id:N}";
        settings.WaitForAssertion(() => settings.Find($"[data-testid='{card}-name']"));

        // A pattern without {n} is held back in the field, with its reason, until changed.
        await settings.Find($"[data-testid='{card}-name']").ChangeAsync(new ChangeEventArgs { Value = "Iteration" });

        // The series is shown in the daily on the other desktop meanwhile.
        await There.ShowInBriefsAsync(series.Id, daily: true, weekly: true, Cancellation);
        Assert.Equal([SprintSeriesReplicaOutcome.Taken], await PullAsync());

        settings.WaitForAssertion(() => Assert.True(settings.Find($"[data-testid='{card}-daily'] input").HasAttribute("checked")));
        Assert.Equal("Iteration", settings.Find($"[data-testid='{card}-name']").GetAttribute("value"));
    }

    public void Dispose()
    {
        _thereScope.Dispose();
        _there.Dispose();
        _context.Dispose();
        SqliteConnection.ClearAllPools();

        foreach (var dir in new[] { _hereDir, _thereDir })
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class Tasks : ISprintTaskSource
    {
        private static readonly IReadOnlyList<SprintTaskDto> All =
        [
            new(Ledger, "Grootboek", SprintTaskStatus.Ready, false, [], ["backlog"], [], null, Monday, []),
            new(Specs, "Specs naar het devbook", SprintTaskStatus.Ready, false, [], ["backlog"], [], null, Monday, [])
        ];

        public Task<IReadOnlyList<SprintTaskDto>> ReadAsync(SprintTaskQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SprintTaskDto>>([.. All.Where(task => query.TaskIds.Contains(task.Id))]);
    }

    private sealed class Registry : ISprintRepositories
    {
        public IReadOnlyList<string> Aliases { get; } = ["backlog", "docs"];
    }

    private sealed class ProgressOn : IAppFeatureSettings
    {
        public event Action? Changed { add { } remove { } }

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "features.json";

        public bool IsEnabled(string key) => key == ProgressFeatures.Progress;

        public string? SetEnabled(string key, bool enabled) => null;
    }
}
