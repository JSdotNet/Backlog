using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.ReturnToInbox;
using Backlog.Modules.Inbox.Features.RouteToBrain;
using Backlog.Modules.Inbox.Services;

using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// To Brain (<c>.devbook/domain/inbox/requirements.md#route-to-brain</c>): the
/// item's title and body become a note in the person's Brain — the
/// <c>ItemTriaged</c> knowledge route, realised as a call on
/// <see cref="IInboxBrainTarget"/> as Route to Tasks is a call on its target —
/// and only once the Brain has the note is the item marked routed.
/// </summary>
public sealed class RouteToBrainTests
{
    private static readonly DateTimeOffset Decision = Items.Noon.AddHours(1);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>The requirement's first scenario: a capture.</summary>
    [Fact]
    public async Task A_capture_becomes_a_note_and_the_item_leaves_the_queue_as_routed()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBrainTarget();
        var item = InboxItem.Capture(
            "Event sourcing in practice",
            new InboxSource("web", Person: null),
            "https://example.com/es",
            ContentKind.Article,
            Items.Noon,
            Items.Noon,
            replicaBacked: false,
            bodyMd: "Why the log is the truth.");
        store.Seed(item);

        var result = await Route(store, target, item.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("_inbox/event-sourcing-in-practice.md", result.Value.NotePath);

        var request = Assert.Single(target.Requests);
        Assert.Equal(item.Id, request.InboxItemId);
        Assert.Equal("Event sourcing in practice", request.Title);
        Assert.Equal("Why the log is the truth.", request.BodyMd);
        Assert.Equal("https://example.com/es", request.SourceUrl);

        Assert.Equal(InboxStatus.Triaged, item.Status);
        Assert.Equal(RoutingDomain.Devbook, item.Routing!.Domain);
        Assert.Empty(item.Routing.TaskIds);
        Assert.Equal(Decision, item.Routing.RoutedAt);
        Assert.False(item.IsOpen);
    }

    /// <summary>The requirement's second scenario, and the second requirement: a
    /// note keeps its title and body, and stays on the phone as the Inbox note it
    /// was — nothing tells the replica to forget it, and no second note goes out.</summary>
    [Fact]
    public async Task An_inbox_note_keeps_its_title_and_body_and_stays_on_the_phone()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBrainTarget();
        var note = InboxItem.FromCapture(
            Guid.CreateVersion7(),
            "Standup",
            new InboxSource("mobile", Person: null),
            sourceUrl: null,
            ContentKind.Note,
            Items.Noon,
            Items.Noon.AddMinutes(5),
            bodyMd: "- shipped sync");
        store.Seed(note);

        var result = await Route(store, target, note.Id);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(target.Requests);
        Assert.Equal("Standup", request.Title);
        Assert.Equal("- shipped sync", request.BodyMd);

        Assert.False(note.ReplicaAckPending);
        Assert.False(note.NotePushPending);
        Assert.Empty(await new InboxCaptureOutbox(store).ListPendingAsync(Cancellation));
    }

    [Fact]
    public async Task A_capture_from_the_phone_is_acknowledged_as_a_route_to_tasks_is()
    {
        var store = new InMemoryInboxStore();
        var item = Items.FromPhone("Read later");
        store.Seed(item);

        await Route(store, new FakeBrainTarget(), item.Id);

        Assert.True(item.ReplicaAckPending);
    }

    /// <summary>The third requirement: no Brain folder, no route.</summary>
    [Fact]
    public async Task A_brain_that_refuses_leaves_the_item_open_and_says_why()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBrainTarget { RefuseWith = "Pick a folder for your Brain in Settings › Storage." };
        var item = Items.Manual("Keep this");
        store.Seed(item);

        var result = await Route(store, target, item.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(InboxErrors.BrainUnavailableCode, result.Error.Code);
        Assert.Equal("Pick a folder for your Brain in Settings › Storage.", result.Error.Message);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.Null(item.Routing);
    }

    [Fact]
    public async Task A_host_with_no_brain_routes_nothing()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual("Keep this");
        store.Seed(item);

        var result = await new RouteToBrainCommandHandler(store, new FakeTimeProvider(Decision), target: null)
            .Handle(new RouteToBrainCommand(item.Id), Cancellation);

        Assert.Equal(InboxErrors.BrainUnavailableCode, result.Error.Code);
        Assert.Null(item.Routing);
    }

    [Fact]
    public async Task An_item_already_routed_writes_no_second_note()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBrainTarget();
        var item = Items.Manual();
        store.Seed(item);
        await Route(store, target, item.Id);

        var again = await Route(store, target, item.Id);

        Assert.True(again.IsFailure);
        Assert.Single(target.Requests);
    }

    [Fact]
    public async Task An_archived_item_writes_no_note()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBrainTarget();
        var item = Items.Manual();
        item.Archive(Items.Noon);
        store.Seed(item);

        var result = await Route(store, target, item.Id);

        Assert.True(result.IsFailure);
        Assert.Empty(target.Requests);
    }

    [Fact]
    public async Task A_missing_item_is_not_found()
    {
        var result = await Route(new InMemoryInboxStore(), new FakeBrainTarget(), Guid.CreateVersion7());

        Assert.Equal(InboxErrors.ItemNotFound, result.Error);
    }

    /// <summary>Undo puts the item back in the queue; the note it became is the
    /// person's file now, in their own folder, and stays.</summary>
    [Fact]
    public async Task Undo_returns_the_item_without_asking_the_backlog_to_delete_anything()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        store.Seed(item);
        await Route(store, new FakeBrainTarget(), item.Id);
        var backlog = new FakeBacklogTarget();

        var result = await new ReturnToInboxCommandHandler(store, backlog, new FakeTimeProvider(Decision.AddMinutes(1)))
            .Handle(new ReturnToInboxCommand(item.Id, DeleteTasks: false), Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.Null(item.Routing);
    }

    private static Task<Result<InboxBrainRoutedDto>> Route(InMemoryInboxStore store, FakeBrainTarget target, Guid id) =>
        new RouteToBrainCommandHandler(store, new FakeTimeProvider(Decision), target)
            .Handle(new RouteToBrainCommand(id), Cancellation);

    private sealed class FakeBrainTarget : IInboxBrainTarget
    {
        public List<InboxBrainNoteRequestDto> Requests { get; } = [];

        public string? RefuseWith { get; init; }

        public Task<Result<string>> WriteNoteAsync(InboxBrainNoteRequestDto request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return Task.FromResult(RefuseWith is { } refusal
                ? Result.Failure<string>(InboxErrors.BrainUnavailable(refusal))
                : Result.Success("_inbox/" + string.Join('-', request.Title.ToLowerInvariant().Split(' ')) + ".md"));
        }
    }
}
