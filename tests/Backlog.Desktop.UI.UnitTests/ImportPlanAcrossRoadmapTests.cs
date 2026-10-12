using Backlog.Desktop.UI.Tasks;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Sqlite.Roadmap;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Extensions;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Extensions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// One Import path, two kinds, both orders (ADR 0013, ruling 3), end to end over the
/// graph a host builds: Tasks' Import, the <see cref="RoadmapPlanIntake"/> adapter,
/// Roadmap's own import command, and the rollup that draws an item's steps — every
/// one of them real, over real SQLite stores in a temporary workspace.
/// <para>
/// The gather is by tag, so nothing links the two documents but the <c>+myplan</c>
/// they share; these tests are what proves that is enough in either order, and that
/// bringing either side in again leaves the other as it was.
/// </para>
/// </summary>
public sealed class ImportPlanAcrossRoadmapTests : IDisposable
{
    private const string RoadmapDocument =
        "# Imported plans on the roadmap\n`plan` `+myplan` `repo:backlog`\n\nWhat the plan is about.\n";

    private const string TaskDocument =
        "# First step\n`prompt` `+myplan` `id:first` `effort:3`\n\n"
        + "# Second step\n`prompt` `+myplan` `id:second` `after:first` `effort:5`\n";

    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "backlog-import-across-roadmap",
        Guid.NewGuid().ToString("n"));

    private readonly ServiceProvider _provider;

    public ImportPlanAcrossRoadmapTests()
    {
        var store = new WorkspaceSettingsStore(_tempDir, Path.Combine(_tempDir, "settings.json"));

        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton<ITaskRepository>(_ => new RootedSqliteTaskRepository(() => store.RootDirectory));
        services.AddSingleton<IRoadmapPlanRepository>(_ => new RootedSqliteRoadmapPlanRepository(() => store.RootDirectory));
        services.AddSingleton(new GitHubSettingsStore(Path.Combine(_tempDir, "github", "github.json")));
        services.AddSingleton(new PlanningVelocitySettingsStore(Path.Combine(_tempDir, "velocity", "planning-velocity.json")));
        services.AddTasksAdapters();
        services.AddTasksModule();
        services.AddRoadmapModule();
        services.AddRoadmapCrossContextAdapters();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (Directory.Exists(_tempDir))
        {
            TestDatabases.Release(_tempDir);
            try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Roadmap_document_first_then_task_document_puts_the_steps_under_the_item()
    {
        var roadmap = await ImportAsync(RoadmapDocument);
        Assert.Equal(1, roadmap.Roadmap!.Created);

        var created = await SingleItemAsync();
        Assert.Equal(EffortWindow.EndFrom(created.Start, 0, 7m, WorkingHours.Default), created.End); // nothing gathered yet: one working week

        var tasks = await ImportAsync(TaskDocument);

        Assert.Equal(2, tasks.Created);
        Assert.Equal(1, tasks.Roadmap!.Relengthened);

        var item = await SingleItemAsync();
        Assert.Equal(created.Id, item.Id);
        Assert.Equal(created.Start, item.Start);
        Assert.Equal(EffortWindow.EndFrom(item.Start, 8, 7m, WorkingHours.Default), item.End); // 3 + 5 points at 7 a working week
        Assert.Equal(["First step", "Second step"], await StepTitlesAsync(item));
    }

    [Fact]
    public async Task Task_document_first_then_roadmap_document_gathers_the_existing_tasks()
    {
        var tasks = await ImportAsync(TaskDocument);
        Assert.Equal(2, tasks.Created);
        Assert.Null(tasks.Roadmap); // no item carries the tag yet, and none was asked for
        Assert.Empty((await PlanAsync()).Items);

        var roadmap = await ImportAsync(RoadmapDocument);

        Assert.Equal(1, roadmap.Roadmap!.Created);
        var item = await SingleItemAsync();
        Assert.Equal("Imported plans on the roadmap", item.Title);
        Assert.Equal(["backlog"], item.RepositoryAliases);
        Assert.Equal(EffortWindow.EndFrom(item.Start, 8, 7m, WorkingHours.Default), item.End); // placed against the effort already there
        Assert.Equal(["First step", "Second step"], await StepTitlesAsync(item));
    }

    [Fact]
    public async Task Re_importing_the_roadmap_document_leaves_the_tasks_intact()
    {
        await ImportAsync(RoadmapDocument);
        await ImportAsync(TaskDocument);
        var before = await TaskIdsAsync();

        var again = await ImportAsync(RoadmapDocument.Replace("Imported plans on the roadmap", "Imported plans, renamed", StringComparison.Ordinal));

        Assert.Equal((0, 0, 0, 0, 0), (again.Created, again.Replaced, again.Updated, again.Skipped, again.Removed));
        Assert.Equal(1, again.Roadmap!.Updated);
        Assert.Equal(before, await TaskIdsAsync());
        Assert.Equal("Imported plans, renamed", (await SingleItemAsync()).Title);
    }

    [Fact]
    public async Task Re_importing_the_task_document_leaves_the_item_intact()
    {
        await ImportAsync(TaskDocument);
        await ImportAsync(RoadmapDocument);
        var item = await SingleItemAsync();

        var again = await ImportAsync(TaskDocument);

        Assert.Equal(2, again.Replaced);
        Assert.Equal(2, (await TaskIdsAsync()).Count);
        var after = await SingleItemAsync();
        Assert.Equal((item.Id, item.Title, item.Start, item.End), (after.Id, after.Title, after.Start, after.End));
        Assert.Equal(["First step", "Second step"], await StepTitlesAsync(item));
    }

    [Fact]
    public async Task A_cycle_between_plan_entries_is_reported_and_the_tasks_are_kept()
    {
        var result = await ImportAsync(
            "# Plan a\n`plan` `+plan-a` `after:plan-b`\n\n"
            + "# Plan b\n`plan` `+plan-b` `after:plan-a`\n\n"
            + TaskDocument);

        Assert.Equal(2, result.Created);
        Assert.NotNull(result.Roadmap!.Refusal);
        Assert.Empty((await PlanAsync()).Items);
        Assert.Equal(2, (await TaskIdsAsync()).Count);
    }

    /// <summary>AC3 through the intake: the importer reads each plan's work back through
    /// the real gathering, sizes each by it, and starts the plan that waits on another the
    /// worked day after that one ends — all in the one document's import.</summary>
    [Fact]
    public async Task A_plan_waiting_on_a_plan_imported_with_it_starts_the_worked_day_after_it_ends()
    {
        await ImportAsync(
            "# Plan a\n`plan` `+plan-a`\n\n"
            + "# Plan b\n`plan` `+plan-b` `after:plan-a`\n\n"
            + "# A step\n`prompt` `+plan-a` `id:a-step` `effort:12`\n\n"
            + "# B step\n`prompt` `+plan-b` `id:b-step` `effort:3`\n");

        var items = (await PlanAsync()).Items;
        var a = Assert.Single(items, item => item.Tag == "plan-a");
        var b = Assert.Single(items, item => item.Tag == "plan-b");

        Assert.Equal(EffortWindow.EndFrom(a.Start, 12, 7m, WorkingHours.Default), a.End);
        Assert.Equal(EffortWindow.FirstWorkedDay(a.End.AddDays(1), WorkingHours.Default), b.Start);
        Assert.Equal(EffortWindow.EndFrom(b.Start, 3, 7m, WorkingHours.Default), b.End);
    }

    /// <summary>The Calendar's shelf drop, through Tasks' own port and the real adapter:
    /// the plan whose tasks arrived first is on the shelf, and dropping it on a day has the
    /// roadmap's own import open its window there, sized from its points at the pace.</summary>
    [Fact]
    public async Task A_shelf_plan_started_from_the_calendar_opens_on_that_day_for_its_effort()
    {
        await ImportAsync(TaskDocument);
        using var scope = _provider.CreateScope();
        var calendar = scope.ServiceProvider.GetRequiredService<ICalendarPlans>();
        var today = DateOnly.FromDateTime(DateTime.Now);

        var before = await calendar.ReadAsync(today, TestContext.Current.CancellationToken);
        var shelved = Assert.Single(before.Shelf);
        Assert.Equal(("myplan", "Myplan", 2, 8), (shelved.Tag, shelved.Title, shelved.TaskCount, shelved.TotalEffort));
        Assert.Empty(before.Windows);
        Assert.True(before.Available);

        // A Monday two weeks out, so the window is not floored to today.
        var monday = today.AddDays(14 - (((int)today.DayOfWeek + 6) % 7));
        Assert.Null(await calendar.StartAsync("myplan", monday, TestContext.Current.CancellationToken));

        var item = await SingleItemAsync();
        Assert.Equal(monday, item.Start);
        Assert.Equal(EffortWindow.EndFrom(monday, 8, 7m, WorkingHours.Default), item.End); // 3 + 5 points at 7 a working week
        Assert.Null(item.PlacedByImport); // the person's placement from now on

        var after = await calendar.ReadAsync(today, TestContext.Current.CancellationToken);
        Assert.Empty(after.Shelf);
        var window = Assert.Single(after.Windows);
        Assert.Equal(
            (item.Id, monday, item.End, "myplan", 0, 8),
            (window.Id, window.Start, window.End, window.Tag, window.DonePoints, window.TotalPoints));

        Assert.Equal("+nothing is no longer waiting on the shelf.", await calendar.StartAsync("nothing", monday, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Planned hours are the person's own plan for the Calendar and never a roadmap input:
    /// blocks set on every step of a plan — more hours than any week holds — leave the
    /// item's window, what it gathered, the pace in use and the Calendar's plan bars
    /// exactly as they were, and so does a re-import that relengthens the plan afterwards.
    /// </summary>
    [Fact]
    public async Task Planned_hours_never_change_a_roadmap_placement()
    {
        await ImportAsync(RoadmapDocument);
        await ImportAsync(TaskDocument);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var before = await RoadmapPictureAsync(today);

        using (var scope = _provider.CreateScope())
        {
            var tasks = scope.ServiceProvider.GetRequiredService<ITaskItems>();
            foreach (var entry in await tasks.ListAsync(TestContext.Current.CancellationToken))
            {
                List<PlannedHoursDto> blocks = [.. Enumerable.Range(0, 10).Select(day => new PlannedHoursDto(today.AddDays(day), 24m))];
                var planned = await tasks.SetPlannedHoursAsync(entry.Id, blocks, TestContext.Current.CancellationToken);
                Assert.True(planned.IsSuccess, planned.IsFailure ? planned.Error.Message : null);
                Assert.Equal(10, planned.Value.PlannedHours.Count);
            }
        }

        Assert.Equal(before, await RoadmapPictureAsync(today));

        // A re-import places the plan again from its points; the blocks still count for nothing.
        await ImportAsync(TaskDocument);
        Assert.Equal(before, await RoadmapPictureAsync(today));
    }

    /// <summary>The Calendar reads a day's capacity from the roadmap's working week through
    /// Tasks' own port, as the composed adapter answers it: nine to half five on a weekday,
    /// nothing at the weekend.</summary>
    [Fact]
    public void The_calendar_reads_a_days_capacity_from_the_roadmaps_working_week()
    {
        using var scope = _provider.CreateScope();
        var capacity = scope.ServiceProvider.GetRequiredService<ICalendarCapacity>();

        Assert.Equal(8.5m, capacity.HoursOn(new DateOnly(2026, 10, 12)));
        Assert.Equal(0m, capacity.HoursOn(new DateOnly(2026, 10, 17)));
    }

    /// <summary>Everything the roadmap draws for the plan, as one string to compare: the
    /// items and their windows, what each gathered, the pace placement divides by and the Calendar's
    /// reading of them.</summary>
    private async Task<string> RoadmapPictureAsync(DateOnly today)
    {
        using var scope = _provider.CreateScope();
        var plan = await scope.ServiceProvider.GetRequiredService<IRoadmapPlanning>().GetPlanAsync(TestContext.Current.CancellationToken);
        var gathered = await scope.ServiceProvider.GetRequiredService<IRoadmapItemRollup>().GatherPlanAsync(plan, TestContext.Current.CancellationToken);
        var velocity = scope.ServiceProvider.GetRequiredService<IPlanningVelocity>();
        var paces = new
        {
            Global = await velocity.GetStoryPointsPerWeekAsync(null, TestContext.Current.CancellationToken),
            Backlog = await velocity.GetStoryPointsPerWeekAsync("backlog", TestContext.Current.CancellationToken)
        };
        var calendar = await scope.ServiceProvider.GetRequiredService<ICalendarPlans>().ReadAsync(today, TestContext.Current.CancellationToken);

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Items = plan.Items.Select(item => new { item.Id, item.Tag, item.Start, item.End }),
            Gathered = gathered.OrderBy(pair => pair.Key).Select(pair => new { pair.Key, pair.Value.DoneEffort, pair.Value.TotalEffort, Entries = pair.Value.BacklogEntries.Count }),
            Paces = paces,
            Windows = calendar.Windows
        });
    }

    [Fact]
    public async Task Lay_out_on_the_roadmap_creates_the_item_a_task_document_names()
    {
        var result = await ImportAsync(TaskDocument, layOut: true);

        Assert.Equal(1, result.Roadmap!.Created);
        var item = await SingleItemAsync();
        Assert.Equal("Myplan", item.Title);
        Assert.Equal("myplan", item.Tag);
        Assert.Equal(EffortWindow.EndFrom(item.Start, 8, 7m, WorkingHours.Default), item.End);
        Assert.Equal(["First step", "Second step"], await StepTitlesAsync(item));
    }

    [Fact]
    public async Task An_import_in_one_scope_is_heard_by_a_roadmap_open_in_another()
    {
        // The band lives in the screen's scope and Import resolves its own; the
        // change has to cross from one to the other, or the band draws a stale plan.
        using var screen = _provider.CreateScope();
        var planning = screen.ServiceProvider.GetRequiredService<IRoadmapPlanning>();
        var heard = 0;
        void Heard() => heard++;
        planning.Changed += Heard;

        await ImportAsync(RoadmapDocument);
        Assert.Equal(1, heard);

        // A refused plan leaves the roadmap as it was, so there is nothing to redraw.
        await ImportAsync("# Plan a\n`plan` `+plan-a` `after:plan-b`\n\n# Plan b\n`plan` `+plan-b` `after:plan-a`\n");
        Assert.Equal(1, heard);

        planning.Changed -= Heard;
        await ImportAsync(RoadmapDocument.Replace("Imported plans on the roadmap", "Renamed", StringComparison.Ordinal));
        Assert.Equal(1, heard);
    }

    private async Task<ImportPlanResultDto> ImportAsync(string document, bool layOut = false)
    {
        using var scope = _provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ITaskItems>()
            .ImportPlanAsync(document, layOutOnRoadmap: layOut, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private async Task<RoadmapPlanDto> PlanAsync()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRoadmapPlanning>().GetPlanAsync(TestContext.Current.CancellationToken);
    }

    private async Task<RoadmapItemDto> SingleItemAsync() => Assert.Single((await PlanAsync()).Items);

    private async Task<List<string>> StepTitlesAsync(RoadmapItemDto item)
    {
        using var scope = _provider.CreateScope();
        var rollup = await scope.ServiceProvider.GetRequiredService<IRoadmapItemRollup>()
            .GatherAsync(item, TestContext.Current.CancellationToken);

        return [.. rollup.BacklogEntries.Select(entry => entry.Title).Order(StringComparer.Ordinal)];
    }

    private async Task<List<Guid>> TaskIdsAsync()
    {
        using var scope = _provider.CreateScope();
        var entries = await scope.ServiceProvider.GetRequiredService<ITaskItems>().ListAsync(TestContext.Current.CancellationToken);
        return [.. entries.Select(entry => entry.Id).Order()];
    }
}
