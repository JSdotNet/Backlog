using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Extensions;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Undoing "Move to backlog", carried through Tasks' real delete into a real
/// store: the entries a route made go, all of them — unless one has started, in
/// which case none does and the refusal names it.
/// </summary>
public sealed class InboxUndoRouteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-inbox-undo", Guid.NewGuid().ToString("n"));

    private readonly ServiceProvider _provider;

    public InboxUndoRouteTests()
    {
        Directory.CreateDirectory(_root);

        _provider = new ServiceCollection()
            .AddSingleton<ITaskRepository>(new RootedSqliteTaskRepository(() => _root))
            .AddSingleton<IRepositoryDirectory, NoRepositories>()
            .AddTasksModule()
            .BuildServiceProvider();
    }

    private ITaskItems Tasks => _provider.GetRequiredService<ITaskItems>();

    private InboxBacklogTarget Target => new(Tasks, new NoRepositories());

    [Fact]
    public async Task Draft_and_ready_entries_are_deleted_and_an_entry_already_gone_is_no_reason_to_refuse()
    {
        var draft = await TaskAsync("# Call the dentist\n`task` `!draft`\n");
        var ready = await TaskAsync("# Book the follow-up\n`task` `!ready`\n");
        var kept = await TaskAsync("# Something else\n`task` `!ready`\n");

        var result = await Target.DeleteRoutedTasksAsync([draft.Id, ready.Id, Guid.CreateVersion7()], TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var left = await Tasks.ListAsync(TestContext.Current.CancellationToken);
        Assert.Equal([kept.Id], left.Select(task => task.Id));
    }

    [Fact]
    public async Task One_started_entry_refuses_the_lot_by_its_title_and_deletes_nothing()
    {
        var draft = await TaskAsync("# Call the dentist\n`task` `!draft`\n");
        var started = await TaskAsync("# Book the follow-up\n`task` `!in-progress`\n");

        var result = await Target.DeleteRoutedTasksAsync([draft.Id, started.Id], TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(InboxErrors.UndoTaskStartedCode, result.Error.Code);
        Assert.Equal("Can't undo — Book the follow-up has started", result.Error.Message);
        var left = await Tasks.ListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, left.Count);
    }

    [Theory]
    [InlineData("!done")]
    [InlineData("!archived")]
    public async Task A_finished_entry_counts_as_started(string status)
    {
        var finished = await TaskAsync($"# Call the dentist\n`task` `{status}`\n");

        var result = await Target.DeleteRoutedTasksAsync([finished.Id], TestContext.Current.CancellationToken);

        Assert.Equal(InboxErrors.UndoTaskStartedCode, result.Error.Code);
    }

    /// <summary>Done now's undo: the entry it made was born Done and ticked, so
    /// Done is no reason to refuse — archived still is.</summary>
    [Fact]
    public async Task Done_nows_undo_deletes_the_done_entry_it_made_but_not_an_archived_one()
    {
        var done = await TaskAsync("# Reply to Sam\n`task` `!done` `completed:2026-11-11`\n");
        var archived = await TaskAsync("# Reply to Ann\n`task` `!archived` `completed:2026-11-11`\n");

        var refused = await Target.DeleteRoutedTasksAsync([archived.Id], madeDone: true, TestContext.Current.CancellationToken);
        var deleted = await Target.DeleteRoutedTasksAsync([done.Id], madeDone: true, TestContext.Current.CancellationToken);

        Assert.Equal(InboxErrors.UndoTaskStartedCode, refused.Error.Code);
        Assert.True(deleted.IsSuccess);
        var left = await Tasks.ListAsync(TestContext.Current.CancellationToken);
        Assert.Equal([archived.Id], left.Select(task => task.Id));
    }

    private async Task<TaskItemDto> TaskAsync(string text)
    {
        var saved = await Tasks.SaveFromTextAsync(null, text, 0, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(saved.IsSuccess, saved.IsFailure ? saved.Error.Message : null);
        return saved.Value.Entry;
    }

    public void Dispose()
    {
        _provider.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // SQLite's pool can hold the file a moment past the last call; the
            // folder is under the temp directory and harmless if it stays.
        }
    }

    private sealed class NoRepositories : IRepositoryDirectory
    {
        public IReadOnlyList<TasksRepositoryRef> Repositories => [];

        public TasksRepositoryRef? Resolve(string name) => null;

        public Result<TasksRepositoryRef> Register(string name) => throw new InvalidOperationException("An undo never registers a repository.");
    }
}
