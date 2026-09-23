using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// Knowledge folders for one repository, pointed at a real directory on disk so
/// a chapter can actually be read.
/// <para>
/// Real files rather than an in-memory tree because the port hands back a path
/// and the tool opens it: a double that answered with text would test everything
/// except the step where a client-supplied path becomes a file, which is the step
/// with the containment check in it.
/// </para>
/// </summary>
internal sealed class FakeDevbookFolderSource(string rootPath, params DevbookFolderSetting[] folders) : IDevbookFolderSource
{
    /// <summary>The keys and paths <c>PrepareContentAsync</c> was asked for. What
    /// says a chapter read named the one file rather than fetching the area.</summary>
    public List<(string Key, string? Path)> Prepared { get; } = [];

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public void NotifyContentChanged()
    {
    }

    public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) =>
        repositoryAlias is null ? [] : folders;

    public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null)
    {
        var folder = folders.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));

        if (folder is null || !folder.Enabled)
        {
            return DevbookFolderLocation.Unavailable(key, $"No '{key}' folder is configured.", repositoryAlias: repositoryAlias);
        }

        var fullPath = Path.Combine(rootPath, folder.EffectivePath);

        return Directory.Exists(fullPath)
            ? new DevbookFolderLocation(
                key,
                Available: true,
                Message: null,
                RepositoryFullName: null,
                folder,
                fullPath,
                RootPath: rootPath,
                ScopeLabel: repositoryAlias,
                RepositoryAlias: repositoryAlias)
            : DevbookFolderLocation.Unavailable(
                key,
                $"The '{folder.DisplayName}' folder is not on this machine.",
                folder: folder,
                repositoryAlias: repositoryAlias);
    }

    public Task<DevbookFolderLocation> PrepareContentAsync(
        string key,
        string? repositoryAlias = null,
        IReadOnlyCollection<string>? relativePaths = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Prepared.Add((key, relativePaths?.SingleOrDefault()));

        return Task.FromResult(Resolve(key, repositoryAlias));
    }
}

/// <summary>
/// The private reading notes, in a list.
/// <para>
/// Every write is recorded and refused: a read-only tool must not add, edit,
/// resolve, delete or apply one, and local ADR 0012 §6 makes resolving a later
/// slice's job rather than this one's.
/// </para>
/// </summary>
internal sealed class FakeDevbookAnnotationStore(params DevbookAnnotation[] annotations) : IDevbookAnnotationStore
{
    private readonly List<DevbookAnnotation> _annotations = [.. annotations];

    /// <summary>The (alias, chapter) pairs that were asked for, in order. What
    /// shows which spellings a tool tried.</summary>
    public List<(string? Alias, string ChapterPath)> Listed { get; } = [];

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<DevbookAnnotation> List(string? repositoryAlias, string chapterPath)
    {
        Listed.Add((repositoryAlias, chapterPath));

        // The real store's own contract: live remarks, drafts included, oldest
        // first. A double that dropped drafts here would make "the tool excludes
        // drafts" true of the double.
        return
        [
            .. _annotations
                .Where(annotation => annotation.IsLive)
                .Where(annotation => string.Equals(annotation.RepositoryAlias, repositoryAlias ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                .Where(annotation => string.Equals(annotation.ChapterPath, chapterPath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(annotation => annotation.CreatedAt)
        ];
    }

    public DevbookAnnotation? Find(Guid id) => _annotations.FirstOrDefault(annotation => annotation.Id == id);

    public DevbookAnnotation Add(
        string? repositoryAlias,
        string chapterPath,
        int blockIndex,
        string author,
        string? blockHash = null) =>
        throw Written(nameof(Add));

    public void Edit(Guid id, string body) => throw Written(nameof(Edit));

    public void SetResolved(Guid id, bool resolved) => throw Written(nameof(SetResolved));

    public void Delete(Guid id) => throw Written(nameof(Delete));

    public void Apply(DevbookAnnotation replicated) => throw Written(nameof(Apply));

    public IReadOnlyList<DevbookAnnotation> ListChangedSince(DateTimeOffset watermark) =>
        throw Written(nameof(ListChangedSince));

    private static InvalidOperationException Written(string member) =>
        new($"A read-only MCP tool called IDevbookAnnotationStore.{member}.");
}

/// <summary>Notes with the fields these tests are about and defaults for the
/// rest.</summary>
internal static class Notes
{
    internal static DevbookAnnotation Note(
        string alias,
        string chapterPath,
        int blockIndex,
        string body = "Worth a second look.",
        bool resolved = false,
        DateTimeOffset? deletedAt = null,
        DateTimeOffset? createdAt = null,
        Guid? id = null)
    {
        var at = createdAt ?? new DateTimeOffset(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);

        return new DevbookAnnotation(
            id ?? Guid.NewGuid(),
            alias,
            chapterPath,
            blockIndex,
            body,
            Author: "SOMEMACHINE",
            at,
            at,
            resolved,
            deletedAt);
    }
}
