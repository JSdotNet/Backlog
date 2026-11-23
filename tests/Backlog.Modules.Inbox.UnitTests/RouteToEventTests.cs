using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.ReceiveCapture;
using Backlog.Modules.Inbox.Features.ReturnToInbox;
using Backlog.Modules.Inbox.Features.RouteToEvent;

using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// To Event (<c>.devbook/domain/inbox/requirements.md#route-to-event</c>): the item's
/// title, link and found facts go to Events through <see cref="IInboxEventTarget"/>,
/// and only once the event exists is the item marked routed, to Events, with the
/// event's id. The Series an event joins is Events' to pick, so it is proved there.
/// </summary>
public sealed class RouteToEventTests
{
    private static readonly DateTimeOffset Decision = Items.Noon.AddHours(1);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly InboxEventFactsDto FoundFacts = new(
        new InboxEventDateDto(new DateOnly(2026, 11, 27), new TimeOnly(16, 30), TimeSpan.FromHours(1)),
        null,
        "Nieuwegein",
        "https://dotnetfriday.nl",
        "dotNET Friday");

    [Fact]
    public async Task An_event_page_capture_keeps_its_facts_on_the_item()
    {
        var store = new InMemoryInboxStore();
        var capture = new InboxCaptureDto(
            Guid.CreateVersion7(), "dotNET Friday Q4", "event_page", Items.Noon, Items.Noon, WithdrawnAt: null,
            SourceUrl: "https://dotnetfriday.nl/q4", ReplicaBacked: false, Event: FoundFacts);

        await new ReceiveCaptureCommandHandler(store, new FakeTimeProvider(Items.Noon))
            .Handle(new ReceiveCaptureCommand(capture), Cancellation);

        var facts = Assert.Single(store.Items.Values).EventFacts!;
        Assert.Equal(new InboxEventDate(new DateOnly(2026, 11, 27), new TimeOnly(16, 30), TimeSpan.FromHours(1)), facts.Start);
        Assert.Equal("Nieuwegein", facts.Place);
        Assert.Equal("https://dotnetfriday.nl", facts.PageUrl);
        Assert.Equal("dotNET Friday", facts.PageTitle);
    }

    [Fact]
    public async Task A_capture_from_any_other_source_has_no_facts()
    {
        var store = new InMemoryInboxStore();

        await new ReceiveCaptureCommandHandler(store, new FakeTimeProvider(Items.Noon)).Handle(
            new ReceiveCaptureCommand(new InboxCaptureDto(Guid.CreateVersion7(), "Read later", "mobile", Items.Noon, Items.Noon, null)),
            Cancellation);

        Assert.Null(Assert.Single(store.Items.Values).EventFacts);
    }

    [Fact]
    public async Task A_routed_item_hands_its_title_link_and_facts_to_Events_and_leaves_the_queue()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeEventTarget();
        var item = EventPageItem();
        store.Seed(item);

        var result = await Route(store, target, item.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(target.EventId, result.Value.EventId);

        var request = Assert.Single(target.Requests);
        Assert.Equal("dotNET Friday Q4", request.Title);
        Assert.Equal("https://dotnetfriday.nl/q4", request.SourceUrl);
        Assert.Equal(FoundFacts, request.Facts);

        Assert.Equal(InboxStatus.Triaged, item.Status);
        Assert.Equal(RoutingDomain.Events, item.Routing!.Domain);
        Assert.Equal(target.EventId, item.Routing.EventId);
        Assert.Empty(item.Routing.TaskIds);
        Assert.Equal(Decision, item.Routing.RoutedAt);
    }

    [Fact]
    public async Task An_item_with_no_facts_still_routes()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeEventTarget();
        var item = Items.Manual("Some meetup");
        store.Seed(item);

        var result = await Route(store, target, item.Id);

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(target.Requests).Facts);
        Assert.Equal(RoutingDomain.Events, item.Routing!.Domain);
    }

    /// <summary>The requirement's flag scenario: the route is withdrawn.</summary>
    [Fact]
    public async Task While_the_Events_area_is_off_nothing_is_made_and_the_item_stays()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeEventTarget { Offered = false };
        var item = Items.Manual();
        store.Seed(item);

        var result = await Route(store, target, item.Id);

        Assert.Equal(InboxErrors.EventsUnavailableCode, result.Error.Code);
        Assert.Empty(target.Requests);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
    }

    [Fact]
    public async Task A_host_with_no_Events_routes_nothing()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        store.Seed(item);

        var result = await new RouteToEventCommandHandler(store, new FakeTimeProvider(Decision), target: null)
            .Handle(new RouteToEventCommand(item.Id), Cancellation);

        Assert.Equal(InboxErrors.EventsUnavailableCode, result.Error.Code);
        Assert.Null(item.Routing);
    }

    /// <summary>The requirement's last scenario: Events cannot take it.</summary>
    [Fact]
    public async Task An_Events_store_that_fails_leaves_the_item_open_and_unrouted()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeEventTarget { Fail = true };
        var item = Items.Manual();
        store.Seed(item);

        var result = await Route(store, target, item.Id);

        Assert.Equal(InboxErrors.EventNotMadeCode, result.Error.Code);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.Null(item.Routing);
    }

    [Fact]
    public async Task An_item_already_routed_makes_no_second_event()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeEventTarget();
        var item = Items.Manual();
        store.Seed(item);
        await Route(store, target, item.Id);

        var again = await Route(store, target, item.Id);

        Assert.True(again.IsFailure);
        Assert.Single(target.Requests);
    }

    [Fact]
    public async Task An_archived_item_makes_no_event()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeEventTarget();
        var item = Items.Manual();
        item.Archive(Items.Noon);
        store.Seed(item);

        var result = await Route(store, target, item.Id);

        Assert.True(result.IsFailure);
        Assert.Empty(target.Requests);
    }

    [Fact]
    public async Task A_capture_from_the_phone_is_acknowledged_as_a_route_to_tasks_is()
    {
        var store = new InMemoryInboxStore();
        var item = Items.FromPhone("Techorama 2026");
        store.Seed(item);

        await Route(store, new FakeEventTarget(), item.Id);

        Assert.True(item.ReplicaAckPending);
    }

    /// <summary>Undo puts the item back in the queue; the event stays in Events.</summary>
    [Fact]
    public async Task Undo_returns_the_item_and_leaves_the_event()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        store.Seed(item);
        await Route(store, new FakeEventTarget(), item.Id);
        var backlog = new FakeBacklogTarget();

        var result = await new ReturnToInboxCommandHandler(store, backlog, new FakeTimeProvider(Decision.AddMinutes(1)))
            .Handle(new ReturnToInboxCommand(item.Id, DeleteTasks: true), Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.Null(item.Routing);
    }

    private static InboxItem EventPageItem()
    {
        var item = InboxItem.FromCapture(
            Guid.CreateVersion7(), "dotNET Friday Q4", new InboxSource("event_page", Person: null), "https://dotnetfriday.nl/q4",
            ContentKind.Link, Items.Noon, Items.Noon, replicaBacked: false);
        item.LoadEventFacts(ReceiveCaptureCommandHandler.EventFactsOf(FoundFacts));
        return item;
    }

    private static Task<Result<InboxEventRoutedDto>> Route(InMemoryInboxStore store, FakeEventTarget target, Guid id) =>
        new RouteToEventCommandHandler(store, new FakeTimeProvider(Decision), target)
            .Handle(new RouteToEventCommand(id), Cancellation);

    private sealed class FakeEventTarget : IInboxEventTarget
    {
        public Guid EventId { get; } = Guid.CreateVersion7();

        public List<InboxEventRequestDto> Requests { get; } = [];

        public bool Offered { get; init; } = true;

        public bool Fail { get; init; }

        public bool IsOffered => Offered;

        public Task<Result<Guid>> SpotEventAsync(InboxEventRequestDto request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return Task.FromResult(Fail
                ? Result.Failure<Guid>(InboxErrors.EventNotMade("disk full"))
                : Result.Success(EventId));
        }
    }
}
