using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.SaveTaskFromText;

namespace Backlog.Modules.Tasks.UnitTests.LinkedTasks;

/// <summary>
/// The save is where a person finishes a task — the tick, the status menu, a bulk
/// change, an MCP transition all write the text through it — so it is where the
/// write-back is asked for. Asked, never awaited, and only by the step into Done:
/// a task saved while already Done, and local work, ask nothing. Reopening takes
/// back the refusal the task carried, in the same save.
/// </summary>
public sealed class SaveTaskFromTextWriteBackTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly RecordingWriteBack _writeBack = new();

    [Theory]
    [InlineData(EntryStatus.Ready)]
    [InlineData(EntryStatus.InProgress)]
    public async Task Finishing_a_linked_task_asks_for_its_item_to_be_finished_at_the_source(EntryStatus from)
    {
        var task = Linked(from);
        var store = new InMemoryTaskRepository(task);

        await SaveAsync(store, task.Id, "# Ship the installer\n`task` `!done`\n");

        Assert.Equal(EntryStatus.Done, store.Entries[task.Id].Status);
        Assert.Equal([task.Id], _writeBack.Requested);
    }

    [Fact]
    public async Task Saving_a_linked_task_that_was_already_done_asks_nothing()
    {
        var task = Linked(EntryStatus.Done);
        var store = new InMemoryTaskRepository(task);

        await SaveAsync(store, task.Id, "# Ship the installer, renamed\n`task` `!done`\n");

        Assert.Empty(_writeBack.Requested);
    }

    [Fact]
    public async Task Finishing_local_work_asks_nothing()
    {
        var task = new TaskItem(Guid.NewGuid(), "Local", string.Empty, EntryType.Task, EntryStatus.Ready, Priority.Medium, null, null, null, Now);
        var store = new InMemoryTaskRepository(task);

        await SaveAsync(store, task.Id, "# Local\n`task` `!done`\n");

        Assert.Empty(_writeBack.Requested);
    }

    [Fact]
    public async Task A_save_that_does_not_finish_the_task_asks_nothing()
    {
        var task = Linked(EntryStatus.Ready);
        var store = new InMemoryTaskRepository(task);

        await SaveAsync(store, task.Id, "# Ship the installer\n`task` `!in-progress`\n");

        Assert.Empty(_writeBack.Requested);
    }

    /// <summary>The heads with no connectors pass no port, and the save goes on as
    /// it always did.</summary>
    [Fact]
    public async Task A_head_with_no_write_back_finishes_a_linked_task_all_the_same()
    {
        var task = Linked(EntryStatus.Ready);
        var store = new InMemoryTaskRepository(task);

        var result = await new SaveTaskFromTextCommandHandler(store, new FakeRepositoryDirectory())
            .Handle(new SaveTaskFromTextCommand(task.Id, "# Ship the installer\n`task` `!done`\n", 0), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(EntryStatus.Done, store.Entries[task.Id].Status);
    }

    [Fact]
    public async Task Reopening_a_task_clears_the_refusal_it_carried()
    {
        var task = Linked(EntryStatus.Done);
        task.SetSourceRef(task.SourceRef! with { WriteBackRefusal = "GitHub refused (403)" });
        var store = new InMemoryTaskRepository(task);

        await SaveAsync(store, task.Id, "# Ship the installer\n`task` `!ready`\n");

        var stored = store.Entries[task.Id];
        Assert.Equal(EntryStatus.Ready, stored.Status);
        Assert.Null(stored.SourceRef!.WriteBackRefusal);
        Assert.Equal("#1", stored.SourceRef.DisplayKey);
        Assert.Empty(_writeBack.Requested);
    }

    [Fact]
    public async Task An_edit_that_leaves_the_task_done_keeps_its_refusal()
    {
        var task = Linked(EntryStatus.Done);
        task.SetSourceRef(task.SourceRef! with { WriteBackRefusal = "GitHub refused (403)" });
        var store = new InMemoryTaskRepository(task);

        await SaveAsync(store, task.Id, "# Ship the installer, with a note\n`task` `!done`\n\nA note.\n");

        Assert.Equal("GitHub refused (403)", store.Entries[task.Id].SourceRef!.WriteBackRefusal);
    }

    private async Task SaveAsync(InMemoryTaskRepository store, Guid id, string rawText)
    {
        var result = await new SaveTaskFromTextCommandHandler(store, new FakeRepositoryDirectory(), _writeBack)
            .Handle(new SaveTaskFromTextCommand(id, rawText, 0), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
    }

    private static TaskItem Linked(EntryStatus status)
    {
        var task = new TaskItem(Guid.NewGuid(), "Ship the installer", string.Empty, EntryType.Task, status, Priority.Medium, null, null, null, Now);
        task.SetSourceRef(new SourceRef("github", "JSdotNet/Backlog", "I_1", "https://example.test/I_1", "#1", null, "open", Now, normalisedState: NormalisedSourceState.Open));
        return task;
    }
}
