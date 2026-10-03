using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Features.ImportPlan;
using Backlog.Modules.Tasks.Features.SaveTaskFromText;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// A plan names the Devbook chapters a step is about with <c>devbook:</c> tokens on
/// its metadata line, and Import lands them in the task's own field.
/// <para>
/// The tokens are consumed rather than kept: the references are a field of the
/// task, not text in it, so nothing of them may survive into the title or the
/// body — the canonical metadata line is rebuilt from the task's fields and has
/// no token to write them back as.
/// </para>
/// <para>
/// Import is the one text path that reads them. The ordinary save leaves the
/// field alone whatever the text says, so a token typed into the editor does what
/// any unrecognised token there has always done: nothing.
/// </para>
/// </summary>
public sealed class ImportPlanDevbookReferenceTests
{
    [Fact]
    public void The_parser_collects_every_devbook_token_in_order_once()
    {
        var parsed = EntryTextParser.Parse(
            "# First prompt\n`prompt` `devbook:.devbook/domain/tasks/domain.md#task` `repo:backlog` "
            + "`devbook:[.devbook/arc42/adr/0007.md]` `devbook:.devbook/domain/tasks/domain.md#task`\n\nDo it.\n");

        Assert.Equal([".devbook/domain/tasks/domain.md#task", "[.devbook/arc42/adr/0007.md]"], parsed.DevbookReferences);
        Assert.Equal(["backlog"], parsed.RepoIds);
        Assert.Equal("Do it.", parsed.Body);
    }

    [Fact]
    public void An_entry_with_no_devbook_token_parses_to_an_empty_list()
    {
        var parsed = EntryTextParser.Parse("# First prompt\n`prompt`\n\nDo it.\n");

        Assert.NotNull(parsed.DevbookReferences);
        Assert.Empty(parsed.DevbookReferences);
    }

    [Fact]
    public async Task Import_lands_the_tokens_in_the_field_and_not_in_the_text()
    {
        var store = new InMemoryTaskRepository();

        const string plan =
            "# First prompt\n`prompt` `+myplan` `id:first` `devbook:.devbook/domain/tasks/domain.md#task` "
            + "`devbook:[.devbook\\arc42\\adr\\0007.md]`\n\nDo the first thing.\n\n"
            + "# Second prompt\n`prompt` `+myplan` `id:second`\n\nDo the second thing.\n";

        var result = await Import(store, plan);

        var first = result.Entries.Single(entry => entry.Title == "First prompt");
        Assert.Equal([".devbook/domain/tasks/domain.md#task", ".devbook/arc42/adr/0007.md"], first.DevbookReferences);
        Assert.Empty(result.Entries.Single(entry => entry.Title == "Second prompt").DevbookReferences);

        var stored = store.Entries[first.Id];
        Assert.Equal(first.DevbookReferences, stored.DevbookReferences);
        Assert.DoesNotContain("devbook", stored.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("devbook", stored.ContentMd, StringComparison.Ordinal);
        Assert.DoesNotContain("devbook:", EntryTextParser.ToRawText(first), StringComparison.Ordinal);
    }

    /// <summary>A step already under way is brought up to date in place on a
    /// re-import, and its references with it: the plan is the source of what the
    /// step is about.</summary>
    [Fact]
    public async Task A_re_import_sets_the_references_from_the_plan()
    {
        var store = new InMemoryTaskRepository();

        await Import(store, "# First prompt\n`prompt` `+myplan` `id:first` `devbook:.devbook/domain/tasks/domain.md#task`\n");
        var started = store.Entries.Values.Single();
        started.SetStatus(EntryStatus.InProgress, new DateOnly(2026, 10, 1));

        var result = await Import(store, "# First prompt\n`prompt` `+myplan` `id:first` `!in-progress` `devbook:.devbook/arc42/adr/0007.md`\n");

        Assert.Equal(1, result.Updated);
        Assert.Equal([".devbook/arc42/adr/0007.md"], store.Entries[started.Id].DevbookReferences);
    }

    /// <summary>A plan that writes no <c>devbook:</c> token on an entry says nothing
    /// about its references — a generated plan never writes them — so a re-import
    /// leaves the ones the step has, picked by hand since or imported before.</summary>
    [Fact]
    public async Task A_re_import_with_no_devbook_token_keeps_the_references()
    {
        var store = new InMemoryTaskRepository();

        await Import(store, "# First prompt\n`prompt` `+myplan` `id:first` `devbook:.devbook/domain/tasks/domain.md#task`\n");
        var entry = store.Entries.Values.Single();
        entry.SetStatus(EntryStatus.InProgress, new DateOnly(2026, 10, 1));

        var result = await Import(store, "# First prompt\n`prompt` `+myplan` `id:first` `!in-progress`\n\nReworded.\n");

        Assert.Equal(1, result.Updated);
        Assert.Equal([".devbook/domain/tasks/domain.md#task"], store.Entries[entry.Id].DevbookReferences);
    }

    /// <summary>A token written without the <c>.devbook/</c> root, its first
    /// folder a devbook area, is stored under it — the spelling every other
    /// writer stores.</summary>
    [Fact]
    public async Task A_token_without_its_devbook_root_is_stored_under_it()
    {
        var store = new InMemoryTaskRepository();

        var result = await Import(store, "# First prompt\n`prompt` `+myplan` `devbook:domain/tasks/domain.md#task`\n");

        Assert.Equal([".devbook/domain/tasks/domain.md#task"], result.Entries.Single().DevbookReferences);
    }

    /// <summary>A token naming no page is the plan's mistake, and Import refuses
    /// the plan before writing anything — the way it refuses a doubled
    /// <c>id:</c> — rather than quietly dropping the reference the plan asked
    /// for.</summary>
    [Fact]
    public async Task A_devbook_token_naming_no_page_refuses_the_plan()
    {
        var store = new InMemoryTaskRepository();

        var result = await new ImportPlanCommandHandler(store, new FakeRepositoryDirectory()).Handle(
            new ImportPlanCommand("# First prompt\n`prompt` `+myplan` `devbook:task`\n\n# Second prompt\n`prompt` `+myplan`\n"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Contains("devbook:task", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, store.Writes);
    }

    /// <summary>The editor is not a door for references: a <c>devbook:</c> token
    /// typed into an entry is read by nothing, exactly as before the field
    /// existed.</summary>
    [Fact]
    public async Task A_devbook_token_typed_into_the_editor_sets_nothing()
    {
        var store = new InMemoryTaskRepository();

        var saved = await new SaveTaskFromTextCommandHandler(store, new FakeRepositoryDirectory()).Handle(
            new SaveTaskFromTextCommand(null, "# Typed by hand\n`task` `devbook:.devbook/domain/tasks/domain.md#task`\n\nBody.\n", 0),
            TestContext.Current.CancellationToken);

        Assert.True(saved.IsSuccess);
        Assert.Empty(saved.Value.Entry.DevbookReferences);
        Assert.Equal("Body.", store.Entries[saved.Value.Entry.Id].ContentMd);
    }

    private static async Task<ImportPlanResultDto> Import(InMemoryTaskRepository store, string rawText)
    {
        var result = await new ImportPlanCommandHandler(store, new FakeRepositoryDirectory())
            .Handle(new ImportPlanCommand(rawText), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }
}
