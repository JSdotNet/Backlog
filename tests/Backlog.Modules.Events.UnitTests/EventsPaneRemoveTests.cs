using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Services;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// Removing an event added by mistake, through the pane's port
/// (<c>.devbook/domain/events/requirements.md#events-pane</c>, "An event added by mistake can be
/// removed"): it leaves the list and no longer opens, its tasks stay in Tasks, and an event holding
/// its harvest task is kept.
/// </summary>
public sealed class EventsPaneRemoveTests : EventsPaneFixture
{
    [Fact]
    public async Task A_removed_event_leaves_the_list_and_no_longer_opens()
    {
        var kept = await Add("Techorama Belgium");
        var duplicate = await Add("Techorama Belgium");

        var removed = await Pane.RemoveAsync(duplicate, Token);

        Assert.True(removed.IsSuccess);
        Assert.Equal([kept], (await Pane.ListAsync(Token)).SelectMany(group => group.Events).Select(e => e.Id));
        Assert.Equal("knowledge_event.not_found", (await Pane.GetAsync(duplicate, Token)).Error.Code);
        Assert.Equal("knowledge_event.not_found", (await Pane.RemoveAsync(duplicate, Token)).Error.Code);
    }

    [Fact]
    public async Task Removing_leaves_the_prep_task_in_tasks()
    {
        var id = await Add("Techorama Belgium");
        var prep = Assert.Single((await Pane.AddPrepTaskAsync(id, "Book the hotel", Token)).Value.Tasks).TaskId;

        Assert.True((await Pane.RemoveAsync(id, Token)).IsSuccess);

        Assert.Equal(new EventTaskFactsDto(prep, "Book the hotel", "Draft"), Tasks.Held[prep]);
        Assert.Empty(Tasks.Completed);
    }

    [Fact]
    public async Task An_event_with_a_harvest_task_is_refused_and_kept()
    {
        var id = await Dated("Techorama Belgium", new DateOnly(2026, 11, 7));
        await Pane.MoveToAsync(id, KnowledgeEventState.Going, Token);
        await new HarvestTaskPolicy(Store, Tasks).FileDueAsync(new DateOnly(2026, 11, 10), Token);
        var saves = Store.Saves;

        var removed = await Pane.RemoveAsync(id, Token);

        var opened = await Pane.GetAsync(id, Token);
        Assert.Equal("knowledge_event.has_harvest_task", removed.Error.Code);
        Assert.Equal(saves, Store.Saves);
        Assert.False(opened.Value.RemoveOffered);
        Assert.Equal(KnowledgeEventState.Going, opened.Value.State);
        Assert.Contains(id, (await Pane.ListAsync(Token)).SelectMany(group => group.Events).Select(e => e.Id));
    }

    [Fact]
    public async Task An_event_without_a_harvest_task_offers_remove()
    {
        var id = await Add("Techorama Belgium");

        var read = await Pane.AddPrepTaskAsync(id, "Book the hotel", Token);

        Assert.True(read.Value.RemoveOffered);
    }
}
