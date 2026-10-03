using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.SaveTaskFromText;
using Backlog.Modules.Tasks.Features.SetDevbookReferences;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// Pointing a task at Devbook pages and chapters, as a use case.
/// <para>
/// The second place a task is written outside its text, after the issue link: the
/// references are the task's own field rather than tokens in the entry, so the
/// ordinary text save neither writes nor clears them, and this is the only door
/// they come through besides Import.
/// </para>
/// </summary>
public sealed class SetDevbookReferencesTests
{
    [Fact]
    public async Task The_whole_list_is_written_and_answered()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Link the chapter", string.Empty, EntryType.Task);
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);
        store.Writes = 0;

        var result = await Handle(store, entry.Id, ["[.devbook/domain/tasks/domain.md#task]", @".devbook\arc42\adr\0007.md"]);

        Assert.True(result.IsSuccess);
        Assert.Equal([".devbook/domain/tasks/domain.md#task", ".devbook/arc42/adr/0007.md"], result.Value.DevbookReferences);
        Assert.Equal(result.Value.DevbookReferences, store.Entries[entry.Id].DevbookReferences);
        Assert.Equal(1, store.Writes);
    }

    /// <summary>The use case MCP's <c>set_devbook_references</c> writes through:
    /// a path typed without its <c>.devbook/</c> root, its first folder a devbook
    /// area, is stored under it, and is one reference with the full spelling.</summary>
    [Fact]
    public async Task A_path_without_its_devbook_root_is_stored_under_it()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Link the chapter", string.Empty, EntryType.Task);
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);

        var result = await Handle(store, entry.Id, ["domain/tasks/domain.md#task", ".devbook/domain/tasks/domain.md#task", "docs/handbook.md"]);

        Assert.True(result.IsSuccess);
        Assert.Equal([".devbook/domain/tasks/domain.md#task", "docs/handbook.md"], store.Entries[entry.Id].DevbookReferences);
    }

    [Fact]
    public async Task An_empty_list_clears_the_references()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Link the chapter", string.Empty, EntryType.Task);
        entry.SetDevbookReferences([".devbook/domain/tasks/domain.md#task"]);
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);

        var result = await Handle(store, entry.Id, []);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.DevbookReferences);
        Assert.Empty(store.Entries[entry.Id].DevbookReferences);
    }

    /// <summary>A value naming no page is the caller's mistake to correct, so it
    /// is a validation failure with the value in the message — and nothing is
    /// written, not even the valid references beside it.</summary>
    [Fact]
    public async Task A_value_naming_no_page_is_refused_and_nothing_is_written()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Link the chapter", string.Empty, EntryType.Task);
        entry.SetDevbookReferences([".devbook/domain/tasks/domain.md#task"]);
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);
        store.Writes = 0;

        var result = await Handle(store, entry.Id, [".devbook/arc42/adr/0007.md", "task"]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(SetDevbookReferencesCommandHandler.InvalidReferenceCode, result.Error.Code);
        Assert.Contains("task", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, store.Writes);
        Assert.Equal([".devbook/domain/tasks/domain.md#task"], store.Entries[entry.Id].DevbookReferences);
    }

    [Fact]
    public async Task A_missing_task_is_not_found()
    {
        var result = await Handle(new InMemoryTaskRepository(), Guid.NewGuid(), [".devbook/arc42/adr/0007.md"]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>
    /// The references are not in the text, so a save of the text — which is what
    /// nearly every keystroke is — has nothing to read them from and must leave
    /// them where they are. Were the save to treat "not in the text" as "cleared",
    /// the first edit after linking a chapter would unlink it.
    /// </summary>
    [Fact]
    public async Task Saving_the_text_leaves_the_references_alone()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Link the chapter", string.Empty, EntryType.Task);
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);
        await Handle(store, entry.Id, [".devbook/domain/tasks/domain.md#task"]);

        var saved = await new SaveTaskFromTextCommandHandler(store, new FakeRepositoryDirectory()).Handle(
            new SaveTaskFromTextCommand(entry.Id, "# Link the chapter, renamed\n`task` `!ready`\n\nA new body.\n", 0),
            TestContext.Current.CancellationToken);

        Assert.True(saved.IsSuccess);
        Assert.Equal([".devbook/domain/tasks/domain.md#task"], saved.Value.Entry.DevbookReferences);
        Assert.Equal([".devbook/domain/tasks/domain.md#task"], store.Entries[entry.Id].DevbookReferences);
        Assert.DoesNotContain("devbook", store.Entries[entry.Id].ContentMd, StringComparison.Ordinal);
    }

    private static Task<Result<Abstractions.DataTransferObjects.TaskItemDto>> Handle(
        InMemoryTaskRepository store,
        Guid id,
        IReadOnlyList<string> references) =>
        new SetDevbookReferencesCommandHandler(store)
            .Handle(new SetDevbookReferencesCommand(id, references), TestContext.Current.CancellationToken);
}
