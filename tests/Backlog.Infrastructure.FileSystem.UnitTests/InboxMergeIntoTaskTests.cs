using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Extensions;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// "Merge into a task", carried through Tasks' real comment into a real store:
/// the capture's title, link and notes land on the task as one dated comment,
/// in that order, and everything the task had is still there. A capture Tasks'
/// comment rule will not take as prose, or a task that is gone, writes nothing.
/// </summary>
public sealed class InboxMergeIntoTaskTests : IDisposable
{
    private static readonly DateTimeOffset Today = new(2026, 10, 8, 9, 30, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-inbox-merge", Guid.NewGuid().ToString("n"));

    private readonly ServiceProvider _provider;

    public InboxMergeIntoTaskTests()
    {
        Directory.CreateDirectory(_root);

        _provider = new ServiceCollection()
            .AddSingleton<ITaskRepository>(new RootedSqliteTaskRepository(() => _root))
            .AddSingleton<IRepositoryDirectory, NoRepositories>()
            .AddTasksModule()
            .BuildServiceProvider();
    }

    private ITaskItems Tasks => _provider.GetRequiredService<ITaskItems>();

    private InboxBacklogTarget Target => new(Tasks, new NoRepositories(), new FakeTimeProvider(Today));

    [Fact]
    public async Task The_captures_title_link_and_notes_land_on_the_task_as_one_comment_and_its_steps_are_kept()
    {
        var task = await TaskAsync("""
            # Tasks list re-keys on remote change
            `task` `!ready`

            The list loses its scroll position.

            ## Reproduce on the phone
            """);

        var result = await Target.CommentOnTaskAsync(
            new InboxMergeRequestDto(
                Guid.CreateVersion7(),
                task.Id,
                "Scroll jumps to top\nwhen phone edits an entry",
                "https://example.com/report/7",
                "Seen twice on the Pixel.\nBoth times after a sync."),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);

        var stored = await EntryAsync(task.Id);
        var raw = EntryTextParser.ToRawText(stored);
        var parent = EntryTextParser.GetParentText(raw);

        Assert.Contains(
            "The list loses its scroll position.\n\n"
            + "2026-10-08: Scroll jumps to top when phone edits an entry\n"
            + "https://example.com/report/7\n\n"
            + "Seen twice on the Pixel.\nBoth times after a sync.",
            parent.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.Equal("Tasks list re-keys on remote change", stored.Title);
        Assert.Equal(task.TotalSubItems, stored.TotalSubItems);
        Assert.Equal(task.Status, stored.Status);
    }

    [Fact]
    public async Task A_capture_without_a_link_or_notes_is_its_title_alone()
    {
        var task = await TaskAsync("# Ship the parser\n`task` `!ready`\n");

        var result = await Target.CommentOnTaskAsync(
            new InboxMergeRequestDto(Guid.CreateVersion7(), task.Id, "Ship the parser", null, string.Empty),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.EndsWith(
            "2026-10-08: Ship the parser",
            EntryTextParser.GetParentText(EntryTextParser.ToRawText(await EntryAsync(task.Id))).TrimEnd('\n', '\r'),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Notes_that_would_become_structure_on_the_task_are_refused_and_nothing_is_written()
    {
        var task = await TaskAsync("# Ship the parser\n`task` `!ready`\n\nBody.\n");
        var before = EntryTextParser.ToRawText(await EntryAsync(task.Id));

        var result = await Target.CommentOnTaskAsync(
            new InboxMergeRequestDto(Guid.CreateVersion7(), task.Id, "Ship the parser", null, "## A heading\n- [ ] a step"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(TaskComments.NotProseCode, result.Error.Code);
        Assert.Equal(before, EntryTextParser.ToRawText(await EntryAsync(task.Id)));
    }

    [Fact]
    public async Task A_task_the_backlog_no_longer_has_is_refused_as_not_found()
    {
        var result = await Target.CommentOnTaskAsync(
            new InboxMergeRequestDto(Guid.CreateVersion7(), Guid.CreateVersion7(), "Ship the parser", null, string.Empty),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(TaskComments.NotFoundCode, result.Error.Code);
        Assert.Empty(await Tasks.ListAsync(TestContext.Current.CancellationToken));
    }

    private async Task<TaskItemDto> TaskAsync(string text)
    {
        var saved = await Tasks.SaveFromTextAsync(null, text, 0, cancellationToken: TestContext.Current.CancellationToken);
        return saved.Value.Entry;
    }

    private async Task<TaskItemDto> EntryAsync(Guid id) =>
        Assert.Single(await Tasks.ListAsync(TestContext.Current.CancellationToken), entry => entry.Id == id);

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

    /// <summary>A workspace with no repositories: these tasks name none.</summary>
    private sealed class NoRepositories : IRepositoryDirectory
    {
        public IReadOnlyList<TasksRepositoryRef> Repositories => [];

        public TasksRepositoryRef? Resolve(string name) => null;

        public Result<TasksRepositoryRef> Register(string name) => throw new InvalidOperationException("A merge never registers a repository.");
    }
}
