using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.ArchiveItem;
using Backlog.Modules.Inbox.Features.MergeIntoTask;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// Merge into a task: a capture that repeats a task already in the backlog
/// becomes a comment on that task — its title, link and notes, handed over as
/// facts — and is archived as a duplicate of the task. Everything the Inbox can
/// refuse is refused before anything is written; a comment Tasks refuses leaves
/// the capture as it was.
/// </summary>
public sealed class MergeIntoTaskTests
{
    private static readonly DateTimeOffset Decision = Items.Noon.AddHours(1);

    private static readonly InboxTaskReferenceDto Existing = new(Guid.CreateVersion7(), null, "Tasks list re-keys on remote change", [], [], []);

    [Fact]
    public async Task A_merge_hands_the_captures_title_link_and_notes_to_the_task_and_archives_it_as_the_tasks_duplicate()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Capture("Scroll jumps to top when phone edits an entry", "Seen twice on the Pixel.", "https://example.com/report/7");
        store.Seed(item);

        var result = await Merge(store, target, item.Id, Existing.Id);

        Assert.True(result.IsSuccess);
        var comment = Assert.Single(target.Comments);
        Assert.Equal(new InboxMergeRequestDto(item.Id, Existing.Id, "Scroll jumps to top when phone edits an entry", "https://example.com/report/7", "Seen twice on the Pixel."), comment);

        Assert.Equal(InboxStatus.Archived, item.Status);
        Assert.Equal(Existing.Id, item.DuplicateOf);
        Assert.True(item.DuplicateOfTask);
        Assert.False(item.IsRouted);
        Assert.Equal(Decision, item.UpdatedAt);
        Assert.Contains(item.Id, store.ItemWrites);
    }

    [Fact]
    public async Task A_deferred_capture_can_be_merged_and_loses_its_review_date()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual("Read the design doc");
        item.Defer(new DateOnly(2026, 9, 30), Items.Noon);
        store.Seed(item);

        var result = await Merge(store, new FakeBacklogTarget(), item.Id, Existing.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(InboxStatus.Archived, item.Status);
        Assert.Null(item.DeferredUntil);
    }

    [Fact]
    public async Task A_capture_already_decided_is_refused_before_the_task_is_touched()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual("Read the design doc");
        item.Archive(Items.Noon);
        store.Seed(item);

        var result = await Merge(store, target, item.Id, Existing.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("inbox.item.invalid_transition", result.Error.Code);
        Assert.Empty(target.Comments);
        Assert.Null(item.DuplicateOf);
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task A_missing_capture_is_refused_as_not_found()
    {
        var target = new FakeBacklogTarget();

        var result = await Merge(new InMemoryInboxStore(), target, Guid.CreateVersion7(), Existing.Id);

        Assert.Equal(InboxErrors.ItemNotFound, result.Error);
        Assert.Empty(target.Comments);
    }

    [Fact]
    public async Task A_task_the_backlog_no_longer_has_is_refused_before_anything_is_written()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual("Read the design doc");
        store.Seed(item);

        var result = await Merge(store, target, item.Id, Guid.CreateVersion7(), new FakeTaskReferences(Existing));

        Assert.Equal(InboxErrors.MergeTaskNotFound, result.Error);
        Assert.Empty(target.Comments);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task A_task_the_comment_finds_gone_is_answered_in_the_inboxs_words()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget { FailCommentWith = Error.NotFound("item.not_found", "No backlog entry has that id.") };
        var item = Items.Manual("Read the design doc");
        store.Seed(item);

        var result = await Merge(store, target, item.Id, Existing.Id);

        Assert.Equal(InboxErrors.MergeTaskNotFound, result.Error);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task A_comment_tasks_refuses_leaves_the_capture_open_and_says_why()
    {
        var store = new InMemoryInboxStore();
        var refused = Error.Validation("comment.not_prose", "A comment is prose.");
        var target = new FakeBacklogTarget { FailCommentWith = refused };
        var item = Capture("Read the design doc", "## Not prose", null);
        store.Seed(item);

        var result = await Merge(store, target, item.Id, Existing.Id);

        Assert.Equal(refused, result.Error);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.Null(item.DuplicateOf);
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task Archiving_as_a_duplicate_of_a_task_walks_no_item_chain()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual("Read the design doc");
        store.Seed(item);

        // No item has the task's id, which as an item duplicate would be
        // refused as not found.
        var result = await new ArchiveItemCommandHandler(store, new FakeTimeProvider(Decision))
            .Handle(new ArchiveItemCommand(item.Id, Existing.Id, DuplicateOfTask: true), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(Existing.Id, item.DuplicateOf);
        Assert.True(item.DuplicateOfTask);
    }

    [Fact]
    public async Task An_item_archived_as_a_duplicate_of_a_merged_capture_names_that_capture()
    {
        var store = new InMemoryInboxStore();
        var merged = Items.Manual("Read the design doc");
        var later = Items.Manual("Read the design docs");
        store.Seed(merged);
        store.Seed(later);
        await Merge(store, new FakeBacklogTarget(), merged.Id, Existing.Id);

        // The chain stops at the link to the task: the merged capture is the
        // one kept on the Inbox's side.
        var result = await new ArchiveItemCommandHandler(store, new FakeTimeProvider(Decision))
            .Handle(new ArchiveItemCommand(later.Id, merged.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(merged.Id, later.DuplicateOf);
        Assert.False(later.DuplicateOfTask);
    }

    [Fact]
    public void An_item_archived_without_a_duplicate_names_no_task()
    {
        var item = Items.Manual("Read the design doc");

        item.Archive(Decision, duplicateOf: null, task: true);

        Assert.Null(item.DuplicateOf);
        Assert.False(item.DuplicateOfTask);
    }

    private static InboxItem Capture(string title, string body, string? sourceUrl) =>
        new(Guid.CreateVersion7(), title, body, sourceUrl, Items.Noon, Items.Noon, ContentKind.Text,
            new InboxSource(InboxEnumMap.ManualChannel, null), replicaBacked: false);

    private static Task<Result> Merge(
        InMemoryInboxStore store,
        FakeBacklogTarget target,
        Guid itemId,
        Guid taskId,
        FakeTaskReferences? taskReferences = null)
    {
        var clock = new FakeTimeProvider(Decision);

        return new MergeIntoTaskCommandHandler(store, target, new ArchiveItemCommandHandler(store, clock), taskReferences)
            .Handle(new MergeIntoTaskCommand(itemId, taskId), TestContext.Current.CancellationToken);
    }
}
