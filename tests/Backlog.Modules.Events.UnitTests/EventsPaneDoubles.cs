using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>The knowledge event store, in memory. It keeps a copy of what was saved, so an event
/// changed and not saved reads back as it was.</summary>
internal sealed class InMemoryKnowledgeEvents : IKnowledgeEventRepository
{
    private readonly List<KnowledgeEvent> _saved = [];

    public int Saves { get; private set; }

    /// <summary>When set, the next list throws, as a store that could not be read would.</summary>
    public bool FailNextList { get; set; }

    public Task<KnowledgeEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_saved.Find(saved => saved.Id == id) is { } found ? Copy(found) : null);

    public Task<IReadOnlyList<KnowledgeEvent>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (FailNextList)
        {
            FailNextList = false;
            throw new InvalidOperationException("The events store could not be read.");
        }

        return Task.FromResult<IReadOnlyList<KnowledgeEvent>>([.. _saved.Select(Copy)]);
    }

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

    /// <summary>Titles a filing is refused for, while the rest still file.</summary>
    public HashSet<string> RefuseTitles { get; } = new(StringComparer.Ordinal);

    /// <summary>The task ids it was asked to complete, in order.</summary>
    public List<string> Completed { get; } = [];

    /// <summary>When set, a completion answers it and changes nothing.</summary>
    public Error? RefuseCompletionWith { get; set; }

    /// <summary>When set, a completion throws it, as a Tasks store that could not be written would.</summary>
    public Exception? ThrowOnComplete { get; set; }

    /// <summary>Titles a filing throws for, while the rest still file.</summary>
    public HashSet<string> ThrowTitles { get; } = new(StringComparer.Ordinal);

    public async Task<Result<string>> FileAsync(string title, CancellationToken cancellationToken = default)
    {
        if (FileGate is { } gate) await gate.Task;
        if (RefuseWith is { } error) return Result.Failure<string>(error);
        if (RefuseTitles.Contains(title)) return Result.Failure<string>(Error.Validation("entry.needs_title", "Refused."));
        if (ThrowTitles.Contains(title)) throw new InvalidOperationException("The Tasks store could not be written.");

        Filed.Add(title);
        return Result.Success(Hold(title));
    }

    /// <summary>Marks a held task Done, and leaves one already Done or Archived as it is — the
    /// adapter's rule, proved against Tasks in <c>EventsTaskTargetTests</c>.</summary>
    public Task<Result> CompleteAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (ThrowOnComplete is { } thrown) throw thrown;
        if (RefuseCompletionWith is { } error) return Task.FromResult(Result.Failure(error));
        if (!Held.TryGetValue(taskId, out var held)) return Task.FromResult(Result.Failure(EventsErrors.TaskNotInBacklog()));
        if (held.Status is "Done" or "Archived") return Task.FromResult(Result.Success());

        Completed.Add(taskId);
        Held[taskId] = held with { Status = "Done" };
        return Task.FromResult(Result.Success());
    }

    public Task<IReadOnlyDictionary<string, EventTaskFactsDto>> LookupAsync(IReadOnlyCollection<string> taskIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, EventTaskFactsDto>>(
            taskIds.Where(Held.ContainsKey).Distinct().ToDictionary(id => id, id => Held[id], StringComparer.Ordinal));

    public event Action? Changed;

    public void RaiseChanged() => Changed?.Invoke();
}

/// <summary>The device's feature settings, holding the <c>events</c> area on or off.</summary>
internal sealed class FakeEventsFeature(bool on) : IAppFeatureSettings
{
    public event Action? Changed;

    public AppFeatureSettings Current => throw new NotSupportedException();

    public string SettingsPath => throw new NotSupportedException();

    public bool IsEnabled(string key) =>
        key == EventsFeatures.Events ? on : throw new ArgumentException($"No feature is called {key}.", nameof(key));

    public string? SetEnabled(string key, bool enabled) => throw new NotSupportedException();

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
