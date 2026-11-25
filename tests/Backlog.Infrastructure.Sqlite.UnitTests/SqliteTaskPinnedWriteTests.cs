using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Extensions;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// A write pinned to the version its caller read — <c>ITaskItems.GetUpdatedAtAsync</c>
/// handed back as <c>expectedUpdatedAt</c> — through the module's own port over the real
/// store: refused with <c>tasks.stale_write</c>, the row untouched, once the entry has
/// been saved since; written while it has not.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteTaskPinnedWriteTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("backlog-sqlite-pinned");
    private readonly SqliteTaskRepository _repository;
    private readonly ServiceProvider _provider;
    private readonly ITaskItems _entries;

    public SqliteTaskPinnedWriteTests()
    {
        _repository = new SqliteTaskRepository(_temp.Path);

        var services = new ServiceCollection();
        services.AddTasksModule();
        services.AddSingleton<ITaskRepository>(_repository);
        services.AddSingleton<IRepositoryDirectory>(new NoRepositories());
        _provider = services.BuildServiceProvider();
        _entries = _provider.CreateScope().ServiceProvider.GetRequiredService<ITaskItems>();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _provider.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task The_version_is_the_stamp_the_store_holds_and_moves_on_every_save()
    {
        var id = await StoredAsync();

        var before = await _entries.GetUpdatedAtAsync(id, Token);
        await _entries.SaveFromTextAsync(id, "# Renamed\n", 0, cancellationToken: Token);
        var after = await _entries.GetUpdatedAtAsync(id, Token);

        Assert.Equal((await _repository.GetAsync(id, Token))!.UpdatedAt, after);
        Assert.NotEqual(before, after);
        Assert.Null(await _entries.GetUpdatedAtAsync(Guid.NewGuid(), Token));
    }

    [Fact]
    public async Task A_save_on_the_version_read_lands()
    {
        var id = await StoredAsync();
        var read = (await _entries.GetUpdatedAtAsync(id, Token))!.Value;

        var saved = await _entries.SaveFromTextAsync(id, "# Renamed\n", 0, read, Token);

        Assert.IsType<Ok<SavedTaskDto>>(saved.Value);
        Assert.Equal("Renamed", (await _repository.GetAsync(id, Token))!.Title);
    }

    /// <summary>Two calls one after the other on one read: the first lands, the second
    /// is refused and the first stands.</summary>
    [Fact]
    public async Task The_second_save_on_one_read_is_refused_and_writes_nothing()
    {
        var id = await StoredAsync();
        var read = (await _entries.GetUpdatedAtAsync(id, Token))!.Value;

        await _entries.SaveFromTextAsync(id, "# First\n", 0, read, Token);
        var refused = await _entries.SaveFromTextAsync(id, "# Second\n", 0, read, Token);

        AssertStale(refused.Value);
        Assert.Equal("First", (await _repository.GetAsync(id, Token))!.Title);
    }

    [Fact]
    public async Task A_link_on_an_outdated_read_is_refused_and_writes_nothing()
    {
        var id = await StoredAsync();
        var read = (await _entries.GetUpdatedAtAsync(id, Token))!.Value;
        await _entries.SaveFromTextAsync(id, "# Renamed\n", 0, cancellationToken: Token);

        var refused = await _entries.LinkToIssueAsync(id, "JSdotNet/Backlog", "7", EntryProjectionDto.PullRequestTargetType, read, Token);

        AssertStale(refused.Value);
        Assert.Empty((await _repository.GetAsync(id, Token))!.ProjectionRefs);
    }

    [Fact]
    public async Task Devbook_references_on_an_outdated_read_are_refused_and_write_nothing()
    {
        var id = await StoredAsync();
        var read = (await _entries.GetUpdatedAtAsync(id, Token))!.Value;
        await _entries.SaveFromTextAsync(id, "# Renamed\n", 0, cancellationToken: Token);

        var refused = await _entries.SetDevbookReferencesAsync(id, [".devbook/domain/tasks/domain.md"], read, Token);

        AssertStale(refused.Value);
        Assert.Empty((await _repository.GetAsync(id, Token))!.DevbookReferences);
    }

    [Fact]
    public async Task A_comment_on_an_outdated_read_is_refused_and_writes_nothing()
    {
        var id = await StoredAsync();
        var read = (await _entries.GetUpdatedAtAsync(id, Token))!.Value;
        await _entries.SaveFromTextAsync(id, "# Renamed\n\nBody.\n", 0, cancellationToken: Token);

        var refused = await _entries.CommentAsync(id, "A note.", new DateOnly(2026, 11, 25), "test", read, Token);

        AssertStale(refused.Value);
        Assert.DoesNotContain("A note.", (await _repository.GetAsync(id, Token))!.ContentMd, StringComparison.Ordinal);
    }

    private async Task<Guid> StoredAsync()
    {
        var task = new TaskItem("Shared entry", "Body.", EntryType.Task);
        await _repository.SaveAsync(task, Token);
        return task.Id;
    }

    private static void AssertStale(object? outcome)
    {
        var error = Assert.IsType<Error>(outcome);
        Assert.Equal(TaskEntryErrorCodes.StaleWrite, error.Code);
        Assert.Equal(ErrorType.Conflict, error.Type);
    }

    /// <summary>A registry that knows no repository: these entries name none.</summary>
    // Differs from the Tasks tests' FakeRepositoryDirectory: that one is not linked here, and nothing here resolves.
    private sealed class NoRepositories : IRepositoryDirectory
    {
        public IReadOnlyList<TasksRepositoryRef> Repositories => [];

        public TasksRepositoryRef? Resolve(string name) => null;

        public Result<TasksRepositoryRef> Register(string name) =>
            Error.Validation("repository.unknown", "No repository is registered here.");
    }
}
