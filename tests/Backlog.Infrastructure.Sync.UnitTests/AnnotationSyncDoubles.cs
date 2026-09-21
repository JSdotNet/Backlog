using Backlog.Infrastructure.Sync.Annotations;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>The annotation-sync state port with no file behind it, recording
/// every state it was handed — <see cref="InMemoryTaskSyncStateStore"/>'s
/// shape over the third feed.</summary>
internal sealed class InMemoryAnnotationSyncStateStore : IAnnotationSyncStateStore
{
    public InMemoryAnnotationSyncStateStore(AnnotationSyncState? initial = null) =>
        Current = initial ?? new AnnotationSyncState(DateTimeOffset.MinValue, null);

    public event Action? Changed;

    public AnnotationSyncState Current { get; private set; }

    public List<AnnotationSyncState> Saved { get; } = [];

    public string StorePath => "in memory";

    public void Save(AnnotationSyncState state)
    {
        Current = state;
        Saved.Add(state);
        Changed?.Invoke();
    }
}

/// <summary>
/// The annotation store with nothing on disk: the port as the exchange and
/// the merge see it, with <see cref="Applied"/> recording every document
/// replication wrote so a test can tell an apply from a local write.
/// </summary>
internal sealed class InMemoryDevbookAnnotationStore : IDevbookAnnotationStore
{
    private readonly Dictionary<Guid, DevbookAnnotation> _annotations = [];

    public event Action? Changed;

    public List<DevbookAnnotation> Applied { get; } = [];

    public IReadOnlyList<DevbookAnnotation> List(string? repositoryAlias, string chapterPath) =>
    [
        .. _annotations.Values
            .Where(annotation => annotation.IsLive)
            .Where(annotation => string.Equals(annotation.RepositoryAlias, repositoryAlias ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            .Where(annotation => string.Equals(annotation.ChapterPath, chapterPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(annotation => annotation.CreatedAt),
    ];

    public DevbookAnnotation? Find(Guid id) => _annotations.GetValueOrDefault(id);

    public DevbookAnnotation Add(string? repositoryAlias, string chapterPath, int blockIndex, string author) =>
        throw new NotSupportedException("Seed the store instead.");

    public void Edit(Guid id, string body) => throw new NotSupportedException();

    public void SetResolved(Guid id, bool resolved) => throw new NotSupportedException();

    public void Delete(Guid id) => throw new NotSupportedException();

    public void Apply(DevbookAnnotation replicated)
    {
        _annotations[replicated.Id] = replicated;
        Applied.Add(replicated);
        Changed?.Invoke();
    }

    public IReadOnlyList<DevbookAnnotation> ListChangedSince(DateTimeOffset watermark) =>
    [
        .. _annotations.Values
            .Where(annotation => !annotation.IsDraft && annotation.UpdatedAt > watermark)
            .OrderBy(annotation => annotation.UpdatedAt)
            .ThenBy(annotation => annotation.Id),
    ];

    /// <summary>What this machine holds, as if a panel had written it.</summary>
    public DevbookAnnotation Seed(DevbookAnnotation annotation)
    {
        _annotations[annotation.Id] = annotation;
        return annotation;
    }
}

/// <summary>Builders for the annotation shapes the tests exchange.</summary>
internal static class Annotations
{
    public const string Repository = "JSdotNet/Backlog";
    public const string Chapter = ".domain/devbook/features.md";

    public static DevbookAnnotation Local(
        string body,
        DateTimeOffset updatedAt,
        DateTimeOffset? deletedAt = null,
        Guid? id = null,
        bool resolved = false) =>
        new(id ?? Guid.NewGuid(), Repository, Chapter, 2, body, "DEV-TOWER", updatedAt, updatedAt, resolved, deletedAt);

    public static AnnotationChange Change(
        Guid id,
        string body,
        DateTimeOffset updatedAt,
        DateTimeOffset? deletedAt = null,
        bool resolved = false) =>
        new(id, updatedAt, deletedAt, new AnnotationPayload(Repository, Chapter, 2, body, "DEV-LAPTOP", updatedAt, resolved));

    public static AnnotationChangeRecord Record(AnnotationChange change, Guid deviceId, long serverTimestamp) =>
        new(change, deviceId, serverTimestamp);
}
