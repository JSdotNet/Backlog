using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The annotation store a panel falls back on when its host composed none —
/// the storybook, and the unit tests that render a panel with the handful of
/// services it asks for by name.
/// <para>
/// In memory, for the life of the object, which is exactly what the panels had
/// before there was a port: a remark that lasts as long as the panel does. The
/// panels keep one path through <see cref="IDevbookAnnotationStore"/> whether
/// or not a host chose a real store, and a host that wants remarks to survive
/// registers one rather than this. Stamps come from the clock it is given, so
/// a test can fix them.
/// </para>
/// </summary>
public sealed class SessionDevbookAnnotationStore : IDevbookAnnotationStore
{
    private readonly Dictionary<Guid, DevbookAnnotation> _annotations = [];
    private readonly TimeProvider _time;

    public SessionDevbookAnnotationStore(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    public event Action? Changed;

    public IReadOnlyList<DevbookAnnotation> List(string? repositoryAlias, string chapterPath) =>
    [
        .. _annotations.Values
            .Where(annotation => annotation.IsLive)
            .Where(annotation => string.Equals(annotation.RepositoryAlias, Key(repositoryAlias), StringComparison.OrdinalIgnoreCase))
            .Where(annotation => string.Equals(annotation.ChapterPath, chapterPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(annotation => annotation.CreatedAt)
            .ThenBy(annotation => annotation.Id),
    ];

    public DevbookAnnotation? Find(Guid id) => _annotations.GetValueOrDefault(id);

    public DevbookAnnotation Add(
        string? repositoryAlias,
        string chapterPath,
        int blockIndex,
        string author,
        string? blockHash = null)
    {
        var now = _time.GetUtcNow();
        var annotation = new DevbookAnnotation(
            Guid.NewGuid(),
            Key(repositoryAlias),
            chapterPath,
            blockIndex,
            string.Empty,
            author,
            now,
            now,
            BlockHash: string.IsNullOrWhiteSpace(blockHash) ? null : blockHash.Trim());

        Apply(annotation);

        return annotation;
    }

    public void Edit(Guid id, string body) =>
        Mutate(id, annotation => annotation with { Body = body, UpdatedAt = Stamp(annotation) });

    public void SetResolved(Guid id, bool resolved) =>
        Mutate(id, annotation => annotation with { Resolved = resolved, UpdatedAt = Stamp(annotation) });

    public void Delete(Guid id)
    {
        if (Find(id) is not { IsLive: true } annotation) return;

        if (annotation.IsDraft)
        {
            _annotations.Remove(id);
            Changed?.Invoke();
            return;
        }

        var at = Stamp(annotation);
        Apply(annotation with { DeletedAt = at, UpdatedAt = at });
    }

    /// <summary>Now, or one tick past the stamp being changed when that is
    /// later — the same rule the file-backed store keeps, and for the same
    /// reason: a local change is later than the copy it was made to, whatever
    /// the clock that stamped that copy thought.</summary>
    private DateTimeOffset Stamp(DevbookAnnotation annotation)
    {
        var now = _time.GetUtcNow();
        return now > annotation.UpdatedAt ? now : annotation.UpdatedAt.AddTicks(1);
    }

    public void Apply(DevbookAnnotation replicated)
    {
        ArgumentNullException.ThrowIfNull(replicated);

        _annotations[replicated.Id] = replicated with { RepositoryAlias = Key(replicated.RepositoryAlias) };
        Changed?.Invoke();
    }

    public IReadOnlyList<DevbookAnnotation> ListChangedSince(DateTimeOffset watermark) =>
    [
        .. _annotations.Values
            .Where(annotation => !annotation.IsDraft && annotation.UpdatedAt > watermark)
            .OrderBy(annotation => annotation.UpdatedAt)
            .ThenBy(annotation => annotation.Id),
    ];

    private void Mutate(Guid id, Func<DevbookAnnotation, DevbookAnnotation> change)
    {
        if (Find(id) is not { IsLive: true } annotation) return;

        Apply(change(annotation));
    }

    private static string Key(string? repositoryAlias) =>
        string.IsNullOrWhiteSpace(repositoryAlias) ? string.Empty : repositoryAlias.Trim();
}
