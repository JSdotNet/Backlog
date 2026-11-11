using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>The knowledge event store, in memory. It keeps a copy of what was saved, so an event
/// changed and not saved reads back as it was.</summary>
internal sealed class InMemoryKnowledgeEvents : IKnowledgeEventRepository
{
    private readonly List<KnowledgeEvent> _saved = [];

    public int Saves { get; private set; }

    public Task<KnowledgeEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_saved.Find(saved => saved.Id == id) is { } found ? Copy(found) : null);

    public Task<IReadOnlyList<KnowledgeEvent>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<KnowledgeEvent>>([.. _saved.Select(Copy)]);

    public Task SaveAsync(KnowledgeEvent knowledgeEvent, CancellationToken cancellationToken = default)
    {
        Saves++;
        var at = _saved.FindIndex(saved => saved.Id == knowledgeEvent.Id);
        if (at >= 0) _saved[at] = Copy(knowledgeEvent);
        else _saved.Add(Copy(knowledgeEvent));
        return Task.CompletedTask;
    }

    private static KnowledgeEvent Copy(KnowledgeEvent e) => KnowledgeEvent.Restore(
        e.Id, e.Title, e.Series, e.Start, e.End, e.DateSource, e.Place, e.Link, e.State, e.SkipReason?.Text,
        e.WhatToLearn, e.KeyDates, e.Ticket, e.Cost, e.Notes, e.Tasks, e.Verdict?.Text);
}

/// <summary>The event series store, in memory.</summary>
internal sealed class InMemoryEventSeries : IEventSeriesRepository
{
    private readonly List<EventSeries> _saved = [];

    public Task<EventSeries?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_saved.Find(saved => saved.Id == id));

    public Task<IReadOnlyList<EventSeries>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EventSeries>>([.. _saved]);

    public Task SaveAsync(EventSeries series, CancellationToken cancellationToken = default)
    {
        _saved.RemoveAll(saved => saved.Id == series.Id);
        _saved.Add(series);
        return Task.CompletedTask;
    }
}

/// <summary>The task target at the boundary: the tasks it holds, the titles it was asked to
/// file, and a refusal it can answer with.</summary>
internal sealed class FakeEventTaskTarget : IEventTaskTarget
{
    public Dictionary<string, EventTaskFactsDto> Held { get; } = new(StringComparer.Ordinal);

    public List<string> Filed { get; } = [];

    public Error? RefuseWith { get; set; }

    public int Writes => Filed.Count;

    public string Hold(string title, string status = "Draft")
    {
        var id = Guid.NewGuid().ToString("D");
        Held[id] = new EventTaskFactsDto(id, title, status);
        return id;
    }

    /// <summary>When set, a filing waits on it, as a slow Tasks file write would.</summary>
    public TaskCompletionSource? FileGate { get; set; }

    public async Task<Result<string>> FileAsync(string title, CancellationToken cancellationToken = default)
    {
        if (FileGate is { } gate) await gate.Task;
        if (RefuseWith is { } error) return Result.Failure<string>(error);

        Filed.Add(title);
        return Result.Success(Hold(title));
    }

    public Task<IReadOnlyDictionary<string, EventTaskFactsDto>> LookupAsync(IReadOnlyCollection<string> taskIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, EventTaskFactsDto>>(
            taskIds.Where(Held.ContainsKey).Distinct().ToDictionary(id => id, id => Held[id], StringComparer.Ordinal));

    public event Action? Changed;

    public void RaiseChanged() => Changed?.Invoke();
}

/// <summary>The Brain at the boundary: the notes at their paths now.</summary>
internal sealed class FakeEventBrainNotes : IEventBrainNotes
{
    public Dictionary<string, string> Notes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? UnavailableReason { get; set; }

    public Task<IReadOnlyDictionary<string, EventBrainNoteDto>> ResolveAsync(IReadOnlyCollection<string> paths, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, EventBrainNoteDto>>(
            paths.Where(Notes.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(path => path, path => new EventBrainNoteDto(path, Notes[path]), StringComparer.OrdinalIgnoreCase));

    public Task<IReadOnlyList<EventBrainNoteDto>> CandidatesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EventBrainNoteDto>>([.. Notes.Select(note => new EventBrainNoteDto(note.Key, note.Value)).OrderBy(note => note.Title)]);

    public event Action? Changed;

    public void RaiseChanged() => Changed?.Invoke();
}
