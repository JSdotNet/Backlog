using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Services;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// The Events pane's port, over in-memory stores and the task and Brain targets at the boundary,
/// on a clock pinned to Tuesday 10 November 2026, 09:00 in Amsterdam. Each test class gets its
/// own pane per test. Its saves are announced on <see cref="Announced"/>, the module's one notice, so
/// a second pane over the same stores (<see cref="Elsewhere"/>) stands in for another window or
/// the MCP server writing beside it.
/// </summary>
public abstract class EventsPaneFixture
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(1));

    private readonly FakeTimeProvider _clock;

    protected static TimeSpan Cet { get; } = TimeSpan.FromHours(1);

    protected EventsPaneFixture()
    {
        _clock = new FakeTimeProvider(Now);
        _clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("CET-test", Cet, "CET", "CET"));
        Pane = Over(Tasks, Brain);
    }

    internal InMemoryKnowledgeEvents Store { get; } = new();

    internal InMemoryEventSeries Series { get; } = new();

    /// <summary>What every save of the panes here raises, as the module's singleton does.</summary>
    internal KnowledgeEventSaved Announced { get; } = new();

    internal FakeEventTaskTarget Tasks { get; } = new();

    internal FakeEventBrainNotes Brain { get; } = new();

    /// <summary>The geocoder both panes here send an address to; it answers nothing until a
    /// test tells it what a town is.</summary>
    internal FakeAddressGeocoder Geocoder { get; } = new();

    internal IEventsPane Pane { get; }

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A second pane over the same stores and the same notice, with task and Brain
    /// targets of its own: another window, or the MCP server's request scope.</summary>
    internal IEventsPane Elsewhere() => Over(new FakeEventTaskTarget(), new FakeEventBrainNotes());

    protected async Task<Guid> Add(string title) => Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.AddAsync(title, cancellationToken: Token)).Value).Value.Id;

    protected async Task<Guid> Dated(string title, DateOnly start)
    {
        var id = await Add(title);
        Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.SetDatesAsync(id, new EventDateDto(start), null, Token)).Value);
        return id;
    }

    /// <summary>Waits until every lookup the panes here started after a save has answered and
    /// been kept: a place save answers once it is saved, and the geocoder call runs after it.</summary>
    protected async Task LookUpsSettledAsync()
    {
        foreach (var pane in _panes.ToList()) await pane.LookUpsSettledAsync();
    }

    private readonly List<EventsPane> _panes = [];

    private EventsPane Over(FakeEventTaskTarget tasks, FakeEventBrainNotes brain)
    {
        var pane = new EventsPane(Store, Series, tasks, brain, new HarvestTaskPolicy(Store, tasks), _clock, Geocoder, Announced);
        _panes.Add(pane);
        return pane;
    }
}
