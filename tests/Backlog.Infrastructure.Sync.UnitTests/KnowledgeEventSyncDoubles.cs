using Backlog.Infrastructure.Sync.KnowledgeEvents;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>The knowledge event sync-state port with no file behind it, recording every state
/// it was handed.</summary>
internal sealed class InMemoryKnowledgeEventSyncStateStore : IKnowledgeEventSyncStateStore
{
    public InMemoryKnowledgeEventSyncStateStore(KnowledgeEventSyncState? initial = null) =>
        Current = initial ?? new KnowledgeEventSyncState(DateTimeOffset.MinValue, null);

    public event Action? Changed;

    public KnowledgeEventSyncState Current { get; private set; }

    public List<KnowledgeEventSyncState> Saved { get; } = [];

    public string StorePath => "in memory";

    public void Save(KnowledgeEventSyncState state)
    {
        Current = state;
        Saved.Add(state);
        Changed?.Invoke();
    }
}

/// <summary>
/// Events' replication port with nothing behind it but two dictionaries, applying the port's
/// rule the way the module does: a copy is taken only when its stamp is later than the held
/// one, and kept at that stamp. A copy with a blank title or name reads as unreadable, as the
/// module's own check would.
/// </summary>
internal sealed class InMemoryKnowledgeEventReplication : IKnowledgeEventReplication
{
    private readonly Dictionary<Guid, KnowledgeEventReplicaDto> _events = [];
    private readonly Dictionary<Guid, EventSeriesReplicaDto> _series = [];

    public List<Guid> Taken { get; } = [];

    public KnowledgeEventReplicaDto? Event(Guid id) => _events.GetValueOrDefault(id);

    public EventSeriesReplicaDto? Series(Guid id) => _series.GetValueOrDefault(id);

    public KnowledgeEventReplicaDto Seed(KnowledgeEventReplicaDto copy) => _events[copy.Id] = copy;

    public EventSeriesReplicaDto Seed(EventSeriesReplicaDto copy) => _series[copy.Id] = copy;

    public Task<IReadOnlyList<KnowledgeEventReplicaDto>> ListEventsChangedSinceAsync(DateTimeOffset watermark, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<KnowledgeEventReplicaDto>>(
            [.. _events.Values.Where(copy => copy.UpdatedAt > watermark).OrderBy(copy => copy.UpdatedAt).ThenBy(copy => copy.Id)]);

    public Task<IReadOnlyList<EventSeriesReplicaDto>> ListSeriesChangedSinceAsync(DateTimeOffset watermark, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EventSeriesReplicaDto>>(
            [.. _series.Values.Where(copy => copy.UpdatedAt > watermark).OrderBy(copy => copy.UpdatedAt).ThenBy(copy => copy.Id)]);

    public Task<KnowledgeEventReplicaOutcome> ApplyEventAsync(KnowledgeEventReplicaDto copy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(copy.Title)) return Task.FromResult(KnowledgeEventReplicaOutcome.Unreadable);
        if (_events.TryGetValue(copy.Id, out var held) && copy.UpdatedAt <= held.UpdatedAt)
            return Task.FromResult(KnowledgeEventReplicaOutcome.NotLater);

        _events[copy.Id] = copy;
        Taken.Add(copy.Id);
        return Task.FromResult(KnowledgeEventReplicaOutcome.Taken);
    }

    public Task<KnowledgeEventReplicaOutcome> ApplySeriesAsync(EventSeriesReplicaDto copy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(copy.Name)) return Task.FromResult(KnowledgeEventReplicaOutcome.Unreadable);
        if (_series.TryGetValue(copy.Id, out var held) && copy.UpdatedAt <= held.UpdatedAt)
            return Task.FromResult(KnowledgeEventReplicaOutcome.NotLater);

        _series[copy.Id] = copy;
        Taken.Add(copy.Id);
        return Task.FromResult(KnowledgeEventReplicaOutcome.Taken);
    }
}

/// <summary>Builders for the event shapes the tests exchange.</summary>
internal static class KnowledgeEventCopies
{
    public static KnowledgeEventReplicaDto Event(string title, DateTimeOffset updatedAt, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), title, KnowledgeEventState.Spotted, null, null, null, null, false, null, [], null, null, [], null, null, null, [], null, updatedAt);

    /// <summary>A Going conference with every field the phone shows set.</summary>
    public static KnowledgeEventReplicaDto Techorama(DateTimeOffset updatedAt, Guid seriesId, Guid? id = null) => new(
        id ?? Guid.NewGuid(),
        "Techorama 2026",
        KnowledgeEventState.Going,
        new EventDateReplicaDto(new DateOnly(2026, 10, 26), new TimeOnly(8, 30), TimeSpan.FromHours(1)),
        new EventDateReplicaDto(new DateOnly(2026, 10, 28), null, null),
        DateSource.EventPage,
        "Kinepolis Utrecht",
        false,
        "https://techorama.nl/2026",
        [new EventKeyDateReplicaDto(new DateOnly(2026, 6, 30), "Early bird ends")],
        "Aspire in production",
        seriesId,
        [new EventNoteLinkReplicaDto(NoteLinkKind.Talk, "6f1c2d3e-0000-0000-0000-000000000042", "Keynote")],
        null,
        TicketState.Bought,
        new EventCostReplicaDto(1295.50m, "EUR"),
        [new EventTaskLinkReplicaDto("task-hotel", TaskPurpose.Prep)],
        null,
        updatedAt);

    public static EventSeriesReplicaDto Series(string name, DateTimeOffset updatedAt, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), name, "Techorama BV", "https://techorama.nl", updatedAt);
}

/// <summary>Feature switches answered per key, so a test can turn sync on and the events area
/// off.</summary>
internal sealed class KeyedFeatureSettings(bool syncOn, bool eventsOn) : IAppFeatureSettings
{
    private readonly Dictionary<string, bool> _enabled = new(StringComparer.Ordinal)
    {
        [Backlog.Modules.Sync.Abstractions.SyncFeatures.Sync] = syncOn,
        [EventsFeatures.Events] = eventsOn,
    };

    public event Action? Changed;

    public AppFeatureSettings Current { get; } = new();

    public string SettingsPath => "in memory";

    public bool IsEnabled(string key) => _enabled.GetValueOrDefault(key);

    public string? SetEnabled(string key, bool enabled)
    {
        _enabled[key] = enabled;
        Changed?.Invoke();
        return null;
    }
}
