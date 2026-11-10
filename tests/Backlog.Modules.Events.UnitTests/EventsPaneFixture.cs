using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Services;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// The Events pane's port, over in-memory stores and the task and Brain targets at the boundary,
/// on a clock pinned to Tuesday 10 November 2026, 09:00 in Amsterdam. Each test class gets its
/// own pane per test.
/// </summary>
public abstract class EventsPaneFixture
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(1));

    protected static TimeSpan Cet { get; } = TimeSpan.FromHours(1);

    protected EventsPaneFixture()
    {
        var clock = new FakeTimeProvider(Now);
        clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("CET-test", Cet, "CET", "CET"));
        Pane = new EventsPane(Store, new InMemoryEventSeries(), Tasks, Brain, clock);
    }

    internal InMemoryKnowledgeEvents Store { get; } = new();

    internal FakeEventTaskTarget Tasks { get; } = new();

    internal FakeEventBrainNotes Brain { get; } = new();

    internal IEventsPane Pane { get; }

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    protected async Task<Guid> Add(string title) => (await Pane.AddAsync(title, cancellationToken: Token)).Value.Id;

    protected async Task<Guid> Dated(string title, DateOnly start)
    {
        var id = await Add(title);
        Assert.True((await Pane.SetDatesAsync(id, new EventDayDto(start), null, Token)).IsSuccess);
        return id;
    }
}
