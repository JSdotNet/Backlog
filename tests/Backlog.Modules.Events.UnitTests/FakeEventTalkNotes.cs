using Backlog.Modules.Events.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>The Inbox and the Brain at the boundary of a harvest: the open Inbox notes by id, the
/// Brain notes written so far by path, and which notes were archived. With no Brain set it writes
/// nothing, as the adapter answers.</summary>
internal sealed class FakeEventTalkNotes : IEventTalkNotes
{
    /// <summary>The open Inbox notes: title and body, by id.</summary>
    public Dictionary<Guid, (string Title, string Body)> Inbox { get; } = [];

    /// <summary>The notes written into the Brain's inbox, by path: title and body as written.</summary>
    public Dictionary<string, (string Title, string Body)> Written { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<Guid> Archived { get; } = [];

    /// <summary>Whether a Brain is set; without one nothing is written.</summary>
    public bool BrainSet { get; set; } = true;

    /// <summary>When set, writing this note throws, as a Brain folder that cannot be written would.</summary>
    public Guid? ThrowOnFiling { get; set; }

    /// <summary>When set, every archive is refused with it.</summary>
    public Error? RefuseArchiveWith { get; set; }

    /// <summary>The order the calls came in: <c>file:&lt;id&gt;</c> and <c>archive:&lt;id&gt;</c>.</summary>
    public List<string> Calls { get; } = [];

    public Task<EventBrainNoteDto?> FileIntoBrainAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"file:{noteId:D}");
        OnFiling?.Invoke();
        if (ThrowOnFiling == noteId) throw new IOException("The Brain folder could not be written.");
        if (!BrainSet || Archived.Contains(noteId) || !Inbox.TryGetValue(noteId, out var note)) return Task.FromResult<EventBrainNoteDto?>(null);

        var path = $"_inbox/{note.Title.ToLowerInvariant().Replace(' ', '-')}.md";
        Written[path] = note;
        return Task.FromResult<EventBrainNoteDto?>(new EventBrainNoteDto(path, note.Title));
    }

    /// <summary>Asked at each filing, before anything is written.</summary>
    public Action? OnFiling { get; set; }

    /// <summary>Asked at each archive, so a test can see what had happened by then.</summary>
    public Func<Task>? OnArchive { get; set; }

    public async Task<Result> ArchiveAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"archive:{noteId:D}");
        if (OnArchive is not null) await OnArchive();
        if (RefuseArchiveWith is { } refusal) return Result.Failure(refusal);

        Archived.Add(noteId);
        return Result.Success();
    }
}
