using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The folder source behind a document-folder view (Design, AI), counting the
/// chapter reads the view makes.
/// <para>
/// The folder load goes through <see cref="IDevbookFolderSource.PrepareContentAsync"/>
/// and is not counted. The open chapter's sync reading resolves the folder with a
/// plain <see cref="Resolve"/> once per chapter read, and nothing else in the view
/// does, so the count of plain resolves of the folder is the count of chapter reads.
/// </para>
/// <para>
/// <see cref="SettleReadsAtOnce"/> answers those resolves unavailable, so the sync
/// reading comes back at once and a chapter read finishes in the dispatcher turn
/// that counted it: once the count has moved, one round trip through the
/// dispatcher puts the read's result on screen. What the folder says about sync is
/// lost, which a test about something else does not miss.
/// </para>
/// </summary>
internal sealed class ChapterReadCountingFolderSource(IDevbookFolderSource inner, string folderKey = ".design") : IDevbookFolderSource
{
    private int _reads;

    /// <summary>Wraps the folder source a harness registered, and registers the
    /// wrapper over it.</summary>
    internal static ChapterReadCountingFolderSource Install(BunitContext context, string folderKey = ".design")
    {
        var registered = context.Services.Last(descriptor => descriptor.ServiceType == typeof(IDevbookFolderSource));
        var source = new ChapterReadCountingFolderSource((IDevbookFolderSource)registered.ImplementationInstance!, folderKey);
        context.Services.AddSingleton<IDevbookFolderSource>(source);
        return source;
    }

    public int ChapterReads => Volatile.Read(ref _reads);

    public bool SettleReadsAtOnce { get; set; }

    public event Action? Changed
    {
        add => inner.Changed += value;
        remove => inner.Changed -= value;
    }

    public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) => inner.Folders(repositoryAlias);

    public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null)
    {
        if (!string.Equals(key, folderKey, StringComparison.OrdinalIgnoreCase)) return inner.Resolve(key, repositoryAlias);

        Interlocked.Increment(ref _reads);
        return SettleReadsAtOnce
            ? DevbookFolderLocation.Unavailable(key, "Answered unavailable so the sync reading comes back at once.")
            : inner.Resolve(key, repositoryAlias);
    }

    public Task<DevbookFolderLocation> PrepareListingAsync(string key, string? repositoryAlias = null, CancellationToken cancellationToken = default) =>
        inner.PrepareListingAsync(key, repositoryAlias, cancellationToken);

    public Task<DevbookFolderLocation> PrepareContentAsync(
        string key,
        string? repositoryAlias = null,
        IReadOnlyCollection<string>? relativePaths = null,
        CancellationToken cancellationToken = default) =>
        inner.PrepareContentAsync(key, repositoryAlias, relativePaths, cancellationToken);

    public IDevbookFileTree FileTree(DevbookFolderLocation location) => inner.FileTree(location);

    public void NotifyContentChanged() => inner.NotifyContentChanged();
}
