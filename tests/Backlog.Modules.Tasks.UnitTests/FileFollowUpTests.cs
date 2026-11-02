using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.FileFollowUp;
using Backlog.Modules.Tasks.Features.ImportPlan;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Markdown;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// Filing a follow-up after an entry
/// (<c>.devbook/domain/tasks/requirements.md#filing-a-follow-up-after-an-entry</c>): what
/// the one prompt an Approve + follow-up answer asks for is born with, that a second
/// delivery of the same answer files nothing, and that the person's words stay body
/// text — checked through <see cref="EntryTextParser"/>, the grammar every screen and
/// save reads the entry back with.
/// </summary>
public sealed class FileFollowUpTests
{
    private static readonly Guid Notification = Guid.Parse("0199a3f2-7c41-7d1a-9d0f-00000000000a");

    [Fact]
    public async Task A_follow_up_in_a_plan_is_a_ready_prompt_after_the_gated_entry_with_its_plan_tag_and_repository()
    {
        var gated = PlanEntry("Show the empty state", "+notifications", "t41", "owner/backlog");
        var store = new InMemoryTaskRepository(gated);

        var result = await File(store, GatedEntry.ById(gated.Id), "Add the empty-state screenshot to the docs");

        Assert.True(result.IsSuccess);
        var followUp = store.Entries[result.Value];
        Assert.Equal(EntryType.Prompt, followUp.Type);
        Assert.Equal(EntryStatus.Ready, followUp.Status);
        Assert.Equal([gated.Id.ToString()], followUp.DependsOn);
        Assert.Equal(["+notifications"], followUp.Tags);
        Assert.Equal(["owner/backlog"], followUp.RepoIds);
        Assert.Null(followUp.ImportPlanId);
        Assert.Null(followUp.ImportItemId);
        Assert.Equal("Follow-up: Show the empty state", followUp.Title);
        Assert.Equal("Add the empty-state screenshot to the docs", followUp.ContentMd);
        Assert.Equal(Notification, followUp.SourceNotificationId);
        Assert.Empty(followUp.SubItems);
    }

    /// <summary>A run that names its entry by the plan-item marker carries the bare
    /// plan slug — the marker's grammar has no <c>+</c> — and the item's <c>id:</c>.</summary>
    [Fact]
    public async Task A_gated_entry_named_by_its_plan_and_item_is_found()
    {
        var gated = PlanEntry("Show the empty state", "+notifications", "t41", "owner/backlog");
        var store = new InMemoryTaskRepository(gated, PlanEntry("Another step", "+notifications", "t42", "owner/backlog"));

        var result = await File(store, GatedEntry.ByPlanItem("notifications", "t41"), "Words");

        Assert.True(result.IsSuccess);
        Assert.Equal([gated.Id.ToString()], store.Entries[result.Value].DependsOn);
    }

    [Fact]
    public async Task A_gated_entry_outside_any_plan_gets_a_follow_up_with_no_plan_tag()
    {
        var gated = new TaskItem("Rename the old setting", string.Empty, EntryType.Prompt, tags: ["settings"]);
        var store = new InMemoryTaskRepository(gated);

        var result = await File(store, GatedEntry.ById(gated.Id), "Rename the setting");

        Assert.True(result.IsSuccess);
        var followUp = store.Entries[result.Value];
        Assert.Empty(followUp.Tags);
        Assert.Equal([gated.Id.ToString()], followUp.DependsOn);
        Assert.Equal(EntryStatus.Ready, followUp.Status);
    }

    /// <summary>A follow-up carries its plan's tag and no <c>import_plan_id</c>, so a
    /// follow-up of a follow-up finds the plan through that tag.</summary>
    [Fact]
    public async Task A_follow_up_of_a_follow_up_stays_in_the_plan()
    {
        var gated = PlanEntry("Show the empty state", "+notifications", "t41", "owner/backlog");
        var store = new InMemoryTaskRepository(gated);
        var first = await File(store, GatedEntry.ById(gated.Id), "First words");

        var second = await File(store, GatedEntry.ById(first.Value), "Second words", Guid.NewGuid());

        Assert.True(second.IsSuccess);
        Assert.Equal(["+notifications"], store.Entries[second.Value].Tags);
        Assert.Equal("Follow-up: Follow-up: Show the empty state", store.Entries[second.Value].Title);
    }

    [Fact]
    public async Task The_same_answer_handed_over_again_files_no_second_task()
    {
        var gated = PlanEntry("Show the empty state", "+notifications", "t41", "owner/backlog");
        var store = new InMemoryTaskRepository(gated);

        var first = await File(store, GatedEntry.ById(gated.Id), "Words");
        var again = await File(store, GatedEntry.ById(gated.Id), "Words");

        Assert.True(again.IsSuccess);
        Assert.Equal(first.Value, again.Value);
        Assert.Equal(2, store.Entries.Count);
    }

    /// <summary>The key is the notification, not the follow-up's being live: a person
    /// who deleted the follow-up does not get it back from a late redelivery.</summary>
    [Fact]
    public async Task A_follow_up_deleted_since_is_not_filed_again_by_the_same_answer()
    {
        var gated = PlanEntry("Show the empty state", "+notifications", "t41", "owner/backlog");
        var store = new InMemoryTaskRepository(gated);
        var first = await File(store, GatedEntry.ById(gated.Id), "Words");
        store.Entries[first.Value].MarkDeleted();

        var again = await File(store, GatedEntry.ById(gated.Id), "Words");

        Assert.True(again.IsSuccess);
        Assert.Equal(first.Value, again.Value);
        Assert.Equal(2, store.Entries.Count);
    }

    [Fact]
    public async Task A_gated_entry_that_is_gone_is_not_found()
    {
        var deleted = PlanEntry("Show the empty state", "+notifications", "t41", "owner/backlog");
        deleted.MarkDeleted();
        var store = new InMemoryTaskRepository(deleted);

        var byId = await File(store, GatedEntry.ById(deleted.Id), "Words");
        var byPlanItem = await File(store, GatedEntry.ByPlanItem("notifications", "t41"), "Words", Guid.NewGuid());
        var unknown = await File(store, GatedEntry.ById(Guid.NewGuid()), "Words", Guid.NewGuid());

        Assert.Equal(ErrorType.NotFound, byId.Error.Type);
        Assert.Equal(ErrorType.NotFound, byPlanItem.Error.Type);
        Assert.Equal(ErrorType.NotFound, unknown.Error.Type);
        Assert.Single(store.Entries);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public async Task Empty_words_are_refused(string text)
    {
        var gated = PlanEntry("Show the empty state", "+notifications", "t41", "owner/backlog");
        var store = new InMemoryTaskRepository(gated);

        var result = await File(store, GatedEntry.ById(gated.Id), text);

        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Single(store.Entries);
    }

    [Fact]
    public async Task A_gated_entry_named_neither_way_is_refused()
    {
        var store = new InMemoryTaskRepository();

        var result = await File(store, new GatedEntry(null), "Words");

        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(store.Entries);
    }

    /// <summary>The scenario "Words that look like a step": the line is kept, escaped
    /// so it reads as the text it is, and the task has no sub-items.</summary>
    [Fact]
    public async Task Words_that_look_like_a_step_stay_one_line_of_body_text()
    {
        var gated = PlanEntry("Show the empty state", "+notifications", "t41", "owner/backlog");
        var store = new InMemoryTaskRepository(gated);

        var result = await File(store, GatedEntry.ById(gated.Id), "- [ ] delete the old table");

        var followUp = store.Entries[result.Value];
        Assert.Equal(@"\- [ ] delete the old table", followUp.ContentMd);
        Assert.Empty(followUp.SubItems);
        Assert.Empty(Reparse(followUp).SubItems);
    }

    /// <summary>Whatever the person typed, the entry read back from its own text has
    /// no sub-items, no second entry, no tag but its plan's, and the type and status
    /// it was born with — the metadata line is still the entry's own.</summary>
    [Theory]
    [InlineData("- [ ] delete the old table")]
    [InlineData("  * [x] already done")]
    [InlineData("+ [ ] a plus list item")]
    [InlineData("## A chapter\nwith notes")]
    [InlineData("### [ ] A nested step")]
    [InlineData("# A second entry\n`task` `!done`")]
    [InlineData("Intro\n\n# A second entry")]
    [InlineData("```\n## inside a fence\n```")]
    [InlineData("~~~\ncode\n~~~")]
    [InlineData("`task` `!done` `#other`")]
    [InlineData("Ship it #urgent and #later")]
    [InlineData("#urgent first")]
    [InlineData("Move it under +other-plan")]
    [InlineData("A line\r\n## after a CRLF")]
    public async Task The_words_never_parse_as_structure_or_tags(string text)
    {
        var gated = PlanEntry("Show the empty state", "+notifications", "t41", "owner/backlog");
        var store = new InMemoryTaskRepository(gated);

        var result = await File(store, GatedEntry.ById(gated.Id), text);

        var followUp = store.Entries[result.Value];
        var raw = EntryTextParser.ToRawText(followUp.ToDto());
        var parsed = EntryTextParser.Parse(raw);

        Assert.Single(EntryTextParser.SplitSegments(raw));
        Assert.Empty(parsed.SubItems);
        Assert.Equal(["+notifications"], parsed.Tags);
        Assert.Equal(EntryType.Prompt, parsed.Type);
        Assert.Equal(EntryStatus.Ready, parsed.Status);
        Assert.Equal("Follow-up: Show the empty state", parsed.Title);
    }

    /// <summary>The escapes are for the grammar, not for the reader: the read view
    /// (<see cref="MarkdownPreview"/>) shows the person's line as they typed it, with
    /// no backslash, as one paragraph of text.</summary>
    [Theory]
    [InlineData("- [ ] delete the old table")]
    [InlineData("## A chapter")]
    [InlineData("# A second entry")]
    [InlineData("```")]
    [InlineData("`task` `!done` `#other`")]
    [InlineData("Ship it #urgent and #later")]
    [InlineData("Move it under +other-plan")]
    public async Task The_read_view_shows_the_words_as_typed(string text)
    {
        var gated = PlanEntry("Show the empty state", "+notifications", "t41", "owner/backlog");
        var store = new InMemoryTaskRepository(gated);

        var result = await File(store, GatedEntry.ById(gated.Id), text);

        var paragraph = Assert.IsType<MdParagraph>(Assert.Single(MarkdownPreview.Parse(store.Entries[result.Value].ContentMd)));
        Assert.Equal(text, Assert.IsType<MdText>(Assert.Single(paragraph.Content)).Text);
    }

    /// <summary>Ordinary prose is not touched: a <c>#</c> or <c>+</c> welded to a word,
    /// a hyphen mid-line, a number — only what the grammar would read is escaped.</summary>
    [Fact]
    public async Task Ordinary_prose_is_kept_exactly_as_typed()
    {
        var gated = PlanEntry("Show the empty state", "+notifications", "t41", "owner/backlog");
        var store = new InMemoryTaskRepository(gated);
        const string words = "Port the C# reader - see issue 12, then C++ 2+2.\n\nSecond paragraph.";

        var result = await File(store, GatedEntry.ById(gated.Id), words);

        Assert.Equal(words, store.Entries[result.Value].ContentMd);
    }

    /// <summary>The re-import clears by <c>import_plan_id</c>; the follow-up carries
    /// none, so it survives, and still wears the tag the plan is listed by.</summary>
    [Fact]
    public async Task The_plan_imported_again_keeps_the_follow_up()
    {
        var store = new InMemoryTaskRepository();
        var import = new ImportPlanCommandHandler(store, new FakeRepositoryDirectory());
        const string plan = "# Show the empty state\n`prompt` `+notifications` `id:t41`\n";
        await import.Handle(new ImportPlanCommand(plan), TestContext.Current.CancellationToken);
        // The gated entry is the one a run was working, so it is under way when the
        // answer comes: the re-import keeps it in place rather than clearing it.
        var gated = store.Entries.Values.Single();
        gated.SetStatus(EntryStatus.InProgress, new DateOnly(2026, 10, 29));

        var filed = await File(store, GatedEntry.ByPlanItem("notifications", "t41"), "Words");
        var reimported = await import.Handle(new ImportPlanCommand(plan), TestContext.Current.CancellationToken);

        Assert.True(reimported.IsSuccess);
        var followUp = store.Entries[filed.Value];
        Assert.Null(followUp.DeletedAt);
        Assert.Contains("+notifications", followUp.Tags);
        Assert.Null(followUp.ImportPlanId);
        Assert.Equal([gated.Id.ToString()], followUp.DependsOn);
        Assert.Null(gated.DeletedAt);
    }

    private static TaskItem PlanEntry(string title, string plan, string item, string repo)
    {
        var entry = new TaskItem(title, string.Empty, EntryType.Prompt, repoIds: [repo], tags: [plan]);
        entry.SetImportPlanId(plan);
        entry.SetImportItemId(item);
        entry.SetStatus(EntryStatus.Ready, new DateOnly(2026, 10, 29));
        return entry;
    }

    private static EntryTextParser.ParsedEntry Reparse(TaskItem entry) =>
        EntryTextParser.Parse(EntryTextParser.ToRawText(entry.ToDto()));

    private static Task<Result<Guid>> File(InMemoryTaskRepository store, GatedEntry gated, string text, Guid? notification = null) =>
        new FileFollowUpHandler(store, TimeProvider.System)
            .FileAfterAsync(new TaskFollowUpRequest(gated, text, notification ?? Notification), TestContext.Current.CancellationToken);
}
