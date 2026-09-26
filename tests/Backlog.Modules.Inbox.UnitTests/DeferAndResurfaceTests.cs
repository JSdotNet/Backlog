using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.DeferItem;
using Backlog.Modules.Inbox.Features.ResurfaceDueItems;
using Backlog.Modules.Inbox.Features.ResurfaceItem;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// Deferral and its way back. An item is put aside with or without a review
/// date; the sweep the pane runs when it opens returns every item whose date
/// has been reached, and leaves an undated one where it is — only a person
/// brings that back.
/// </summary>
public sealed class DeferAndResurfaceTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(Items.Noon.UtcDateTime);

    /// <summary>A clock whose local day is <see cref="Today"/>, whatever zone
    /// the machine running the test is in: the sweep reads the local date.</summary>
    private static FakeTimeProvider Clock()
    {
        var clock = new FakeTimeProvider(Items.Noon.AddHours(1));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        return clock;
    }

    // --- The aggregate ---------------------------------------------------------

    [Fact]
    public void A_deferred_item_whose_review_date_is_reached_resurfaces_as_unprocessed()
    {
        var item = Items.Manual();
        item.Defer(Today, Items.Noon);

        var resurfaced = item.ResurfaceIfDue(Today, Items.Noon.AddHours(1));

        Assert.True(resurfaced);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.Null(item.DeferredUntil);
    }

    [Fact]
    public void A_deferred_item_before_its_review_date_stays_deferred()
    {
        var item = Items.Manual();
        item.Defer(Today.AddDays(1), Items.Noon);

        Assert.False(item.ResurfaceIfDue(Today, Items.Noon.AddHours(1)));

        Assert.Equal(InboxStatus.Deferred, item.Status);
        Assert.Equal(Today.AddDays(1), item.DeferredUntil);
    }

    /// <summary>No date is "until I say so", not "until now": nothing but a
    /// person brings it back.</summary>
    [Fact]
    public void A_deferred_item_without_a_review_date_is_never_due()
    {
        var item = Items.Manual();
        item.Defer(until: null, Items.Noon);

        Assert.False(item.ResurfaceIfDue(Today.AddYears(1), Items.Noon.AddHours(1)));

        Assert.Equal(InboxStatus.Deferred, item.Status);
    }

    [Fact]
    public void An_item_that_is_not_deferred_is_never_due()
    {
        var item = Items.Manual();

        Assert.False(item.ResurfaceIfDue(Today, Items.Noon.AddHours(1)));

        Assert.Equal(InboxStatus.Unprocessed, item.Status);
    }

    /// <summary>Deferring again is how a review date is changed.</summary>
    [Fact]
    public void A_deferred_item_can_be_deferred_again_to_a_new_date()
    {
        var item = Items.Manual();
        item.Defer(Today, Items.Noon);

        item.Defer(Today.AddDays(7), Items.Noon.AddHours(1));

        Assert.Equal(InboxStatus.Deferred, item.Status);
        Assert.Equal(Today.AddDays(7), item.DeferredUntil);
    }

    [Fact]
    public void A_routed_item_cannot_be_deferred()
    {
        var item = Items.Manual();
        item.RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon);

        Assert.Throws<InvalidInboxTransitionException>(() => item.Defer(Today, Items.Noon.AddHours(1)));
    }

    // --- The commands ------------------------------------------------------------

    [Fact]
    public async Task Deferring_saves_the_item_with_its_review_date()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        store.Seed(item);

        var result = await new DeferItemCommandHandler(store, Clock())
            .Handle(new DeferItemCommand(item.Id, Today.AddDays(3)), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(InboxStatus.Deferred, store.Items[item.Id].Status);
        Assert.Equal(Today.AddDays(3), store.Items[item.Id].DeferredUntil);
        Assert.Equal([item.Id], store.ItemWrites);
    }

    [Fact]
    public async Task Deferring_an_archived_item_is_refused_and_writes_nothing()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        item.Archive(Items.Noon);
        store.Seed(item);

        var result = await new DeferItemCommandHandler(store, Clock())
            .Handle(new DeferItemCommand(item.Id, null), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("inbox.item.invalid_transition", result.Error.Code);
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task Deferring_an_unknown_item_is_not_found()
    {
        var result = await new DeferItemCommandHandler(new InMemoryInboxStore(), Clock())
            .Handle(new DeferItemCommand(Guid.CreateVersion7(), null), TestContext.Current.CancellationToken);

        Assert.Equal(InboxErrors.ItemNotFound, result.Error);
    }

    [Fact]
    public async Task Returning_a_deferred_item_to_the_inbox_makes_it_unprocessed()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        item.Defer(until: null, Items.Noon);
        store.Seed(item);

        var result = await new ResurfaceItemCommandHandler(store, Clock())
            .Handle(new ResurfaceItemCommand(item.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(InboxStatus.Unprocessed, store.Items[item.Id].Status);
    }

    [Fact]
    public async Task Returning_an_item_that_is_not_deferred_is_refused()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        store.Seed(item);

        var result = await new ResurfaceItemCommandHandler(store, Clock())
            .Handle(new ResurfaceItemCommand(item.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Empty(store.ItemWrites);
    }

    /// <summary>The sweep: past and today come back, tomorrow and undated stay,
    /// and only what moved is written.</summary>
    [Fact]
    public async Task The_sweep_returns_every_deferred_item_whose_date_is_reached_and_touches_nothing_else()
    {
        var store = new InMemoryInboxStore();
        var past = Deferred(store, Today.AddDays(-2));
        var due = Deferred(store, Today);
        var future = Deferred(store, Today.AddDays(1));
        var undated = Deferred(store, null);
        var open = Items.Manual("Still open");
        store.Seed(open);

        var result = await new ResurfaceDueItemsCommandHandler(store, Clock())
            .Handle(new ResurfaceDueItemsCommand(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        Assert.Equal(InboxStatus.Unprocessed, store.Items[past.Id].Status);
        Assert.Equal(InboxStatus.Unprocessed, store.Items[due.Id].Status);
        Assert.Equal(InboxStatus.Deferred, store.Items[future.Id].Status);
        Assert.Equal(InboxStatus.Deferred, store.Items[undated.Id].Status);
        Assert.Equal(new HashSet<Guid> { past.Id, due.Id }, store.ItemWrites.ToHashSet());
    }

    [Fact]
    public async Task A_sweep_with_nothing_due_writes_nothing()
    {
        var store = new InMemoryInboxStore();
        Deferred(store, Today.AddDays(5));

        var result = await new ResurfaceDueItemsCommandHandler(store, Clock())
            .Handle(new ResurfaceDueItemsCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Value);
        Assert.Empty(store.ItemWrites);
    }

    private static InboxItem Deferred(InMemoryInboxStore store, DateOnly? until)
    {
        var item = Items.Manual($"Deferred until {until?.ToString("O") ?? "later"}");
        item.Defer(until, Items.Noon);
        store.Seed(item);
        return item;
    }
}
