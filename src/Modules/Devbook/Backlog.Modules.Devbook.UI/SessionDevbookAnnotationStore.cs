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
    private readonly IDevbookFolderSource? _folders;

    /// <param name="time">Stamps the writes a panel makes, so a test can fix
    /// them.</param>
    /// <param name="folders">Where this machine has each knowledge folder
    /// pointed, so a chapter path can be put in canonical form — see
    /// <see cref="Canonical"/>. Optional for the same reason the whole class is a
    /// fallback: the storybook has no repository registry to ask, and without one
    /// the conventional folders are the right reading anyway.</param>
    public SessionDevbookAnnotationStore(TimeProvider? time = null, IDevbookFolderSource? folders = null)
    {
        _time = time ?? TimeProvider.System;
        _folders = folders;
    }

    public event Action? Changed;

    public IReadOnlyList<DevbookAnnotation> List(string? repositoryAlias, string chapterPath)
    {
        var alias = Key(repositoryAlias);

        // The question is canonicalized as well as the stored key, so a caller
        // asking in whichever spelling it holds is still asking about the one
        // chapter.
        var chapter = Canonical(alias, chapterPath);

        return
        [
            .. _annotations.Values
                .Where(annotation => annotation.IsLive)
                .Where(annotation => string.Equals(annotation.RepositoryAlias, alias, StringComparison.OrdinalIgnoreCase))
                .Where(annotation => string.Equals(annotation.ChapterPath, chapter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(annotation => annotation.CreatedAt)
                .ThenBy(annotation => annotation.Id),
        ];
    }

    public DevbookAnnotation? Find(Guid id) => _annotations.GetValueOrDefault(id);

    public DevbookAnnotation Add(
        string? repositoryAlias,
        string chapterPath,
        int blockIndex,
        string author,
        string? blockHash = null)
    {
        var now = _time.GetUtcNow();

        // Spelled as the caller handed it over, and settled once in Put. The
        // returned value is what the store now holds rather than what was built
        // here, so a caller that goes on to ask for this remark by its chapter
        // path is given the path the next List will answer to.
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

        var stored = Put(annotation);
        Changed?.Invoke();

        return stored;
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

    /// <summary>Writes a document as replication hands it over, with the two
    /// addressing fields settled on the way in — the alias as it always has been,
    /// and the chapter path in canonical form, so a stale spelling arriving from
    /// another device is filed under the name this one reads by. No stamp is
    /// touched: the merge has already decided this version wins.</summary>
    public void Apply(DevbookAnnotation replicated)
    {
        ArgumentNullException.ThrowIfNull(replicated);

        Put(replicated);
        Changed?.Invoke();
    }

    /// <summary>One document into the dictionary, with the two addressing fields
    /// settled, and the document as stored handed back. The only place either
    /// field is settled: applying the chapter key twice is not the same as
    /// applying it once for every configuration a repository can have.</summary>
    private DevbookAnnotation Put(DevbookAnnotation annotation)
    {
        var alias = Key(annotation.RepositoryAlias);

        var stored = annotation with
        {
            RepositoryAlias = alias,
            ChapterPath = Canonical(alias, annotation.ChapterPath)
        };

        _annotations[annotation.Id] = stored;

        return stored;
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

    /// <summary>The one name a chapter has on every device, for the repository
    /// this remark belongs to. A path that canonicalizes to nothing keeps what it
    /// had, because losing the address is worse than keeping one nobody can
    /// resolve. The chapter key itself cannot fail; the folder source is a port,
    /// and an adapter that throws throws through here rather than being caught
    /// into a silently mis-keyed remark.</summary>
    private string Canonical(string alias, string? chapterPath)
    {
        var canonical = DevbookChapterKey.Canonical(chapterPath, _folders?.Folders(alias));

        return canonical.Length == 0 ? chapterPath ?? string.Empty : canonical;
    }
}
