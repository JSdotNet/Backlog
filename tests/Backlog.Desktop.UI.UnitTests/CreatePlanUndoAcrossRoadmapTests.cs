using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Sqlite.Roadmap;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Extensions;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Undoing Create plan with a project note, through the real Inbox target, Tasks'
/// import and Roadmap's store: the Roadmap Item the plan's <c>plan</c> entry laid out
/// goes with the tasks while nothing on it was edited and none of the tasks has
/// started — and stays, with the tasks, once either is no longer true
/// (<c>.devbook/domain/inbox/features.md#undo-a-decision</c>).
/// </summary>
public sealed class CreatePlanUndoAcrossRoadmapTests : IDisposable
{
    private const string Title = "Sync rework";

    private static readonly Guid InboxItemId = Guid.CreateVersion7();

    private static readonly string Tag = InboxPlanTag.For(Title, InboxItemId);

    /// <summary>What Create plan hands the import: its <c>plan</c> entry, then the
    /// drafter's steps under the same tag.</summary>
    private static readonly string Plan =
        $"# {Title}\n`plan` `+{Tag}` `devbook:#brain:projects/sync-rework.md`\n\n"
        + $"# First step\n`prompt` `+{Tag}` `id:first` `effort:3`\n\n"
        + $"# Second step\n`prompt` `+{Tag}` `id:second` `after:first` `effort:5`\n";

    private static readonly InboxLaidOutPlanDto LaidOut = new(Tag, Title, []);

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "backlog-create-plan-undo", Guid.NewGuid().ToString("n"));

    private readonly ServiceProvider _provider;

    public CreatePlanUndoAcrossRoadmapTests()
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
        services.AddInboxCrossContextAdapters();

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
    public async Task An_untouched_plan_takes_its_roadmap_item_back_with_its_tasks()
    {
        var taskIds = await CreatePlanAsync();
        Assert.Single((await PlanAsync()).Items);

        var result = await UndoAsync(taskIds);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Empty((await PlanAsync()).Items);
        Assert.Empty(await TaskIdsAsync());
    }

    [Fact]
    public async Task A_renamed_roadmap_item_refuses_the_undo_and_keeps_the_item_and_the_tasks()
    {
        var taskIds = await CreatePlanAsync();
        var item = Assert.Single((await PlanAsync()).Items);
        await WithPlanningAsync(planning => planning.UpdateItemAsync(
            item.Id, "Sync rework, phase one", item.Start, item.End, item.Priority, item.RepositoryAliases,
            item.Lane, item.TaskId, item.Notes, item.Tag, item.KnowledgeRefs, TestContext.Current.CancellationToken));

        var result = await UndoAsync(taskIds);

        Assert.Equal(InboxErrors.UndoPlanEditedCode, result.Error.Code);
        Assert.Equal("Can't undo — the roadmap item Sync rework, phase one has been changed", result.Error.Message);
        Assert.Single((await PlanAsync()).Items);
        Assert.Equal(taskIds.Order(), await TaskIdsAsync());
    }

    [Fact]
    public async Task A_roadmap_item_moved_by_hand_refuses_the_undo()
    {
        var taskIds = await CreatePlanAsync();
        var item = Assert.Single((await PlanAsync()).Items);
        await WithPlanningAsync(planning => planning.RescheduleItemAsync(
            item.Id, item.Start.AddDays(7), item.End.AddDays(7), cancellationToken: TestContext.Current.CancellationToken));

        var result = await UndoAsync(taskIds);

        Assert.Equal(InboxErrors.UndoPlanEditedCode, result.Error.Code);
        Assert.Single((await PlanAsync()).Items);
    }

    [Fact]
    public async Task A_started_task_refuses_the_undo_and_the_roadmap_item_stays()
    {
        var taskIds = await CreatePlanAsync();
        using (var scope = _provider.CreateScope())
        {
            var tasks = scope.ServiceProvider.GetRequiredService<ITaskItems>();
            var entry = await tasks.GetAsync(taskIds[0], TestContext.Current.CancellationToken);
            foreach (var status in new[] { "!ready", "!in-progress" })
            {
                var saved = await tasks.SaveFromTextAsync(
                    entry!.Id, $"# {entry.Title}\n`prompt` `+{Tag}` `{status}`\n", entry.Order, cancellationToken: TestContext.Current.CancellationToken);
                Assert.True(saved.IsSuccess, saved.IsFailure ? saved.Error.Message : null);
            }
        }

        var result = await UndoAsync(taskIds);

        Assert.Equal(InboxErrors.UndoTaskStartedCode, result.Error.Code);
        Assert.Single((await PlanAsync()).Items);
        Assert.Equal(2, (await TaskIdsAsync()).Count);
    }

    private async Task<IReadOnlyList<Guid>> CreatePlanAsync()
    {
        using var scope = _provider.CreateScope();
        var imported = await scope.ServiceProvider.GetRequiredService<IInboxBacklogTarget>()
            .ImportPlanAsync(Plan, InboxItemId, [], cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(imported.IsSuccess, imported.IsFailure ? imported.Error.Message : null);
        return imported.Value;
    }

    private async Task<Backlog.SharedKernel.Results.Result> UndoAsync(IReadOnlyList<Guid> taskIds)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IInboxBacklogTarget>()
            .DeleteRoutedPlanAsync(taskIds, LaidOut, TestContext.Current.CancellationToken);
    }

    private async Task WithPlanningAsync<T>(Func<IRoadmapPlanning, Task<Backlog.SharedKernel.Results.Result<T>>> edit)
    {
        using var scope = _provider.CreateScope();
        var edited = await edit(scope.ServiceProvider.GetRequiredService<IRoadmapPlanning>());
        Assert.True(edited.IsSuccess, edited.IsFailure ? edited.Error.Message : null);
    }

    private async Task<RoadmapPlanDto> PlanAsync()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRoadmapPlanning>().GetPlanAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<Guid>> TaskIdsAsync()
    {
        using var scope = _provider.CreateScope();
        var entries = await scope.ServiceProvider.GetRequiredService<ITaskItems>().ListAsync(TestContext.Current.CancellationToken);
        return [.. entries.Select(entry => entry.Id).Order()];
    }
}
