using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A folder source that can be made to fail the way a folder moving under a git
/// pull fails: every read through it throws <see cref="DirectoryNotFoundException"/>
/// until it is made readable again.
/// <para>
/// The failure is raised at the port rather than arranged on disk, because the
/// stores behind the panels check a folder exists before they read it — a
/// deleted folder answers "unavailable" there, and it is only the window between
/// that check and the read that throws. Throwing from the port is that window
/// held open, with none of the race that would make it flaky.
/// </para>
/// <para>
/// The announcement itself is never guarded: a pull raises it after the folder
/// has already moved, and the handler under test is the thing that has to cope
/// with what it finds.
/// </para>
/// </summary>
internal sealed class UnreadableDevbookFolderSource(IDevbookFolderSource inner) : IDevbookFolderSource
{
    private bool _unreadable;

    /// <summary>Wraps the folder source the harness registered and registers the
    /// wrapper in its place, so every store the panel resolves afterwards reads
    /// through it. Called before the first render, while the container can still
    /// be added to.</summary>
    internal static UnreadableDevbookFolderSource Install(BunitContext context)
    {
        var registered = context.Services.Last(descriptor => descriptor.ServiceType == typeof(IDevbookFolderSource));
        var source = new UnreadableDevbookFolderSource((IDevbookFolderSource)registered.ImplementationInstance!);
        context.Services.AddSingleton<IDevbookFolderSource>(source);
        return source;
    }

    internal void MakeUnreadable() => _unreadable = true;

    public event Action? Changed
    {
        add => inner.Changed += value;
        remove => inner.Changed -= value;
    }

    public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias)
    {
        ThrowIfUnreadable();
        return inner.Folders(repositoryAlias);
    }

    public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null)
    {
        ThrowIfUnreadable();
        return inner.Resolve(key, repositoryAlias);
    }

    public Task<DevbookFolderLocation> PrepareListingAsync(string key, string? repositoryAlias = null, CancellationToken cancellationToken = default)
    {
        ThrowIfUnreadable();
        return inner.PrepareListingAsync(key, repositoryAlias, cancellationToken);
    }

    public Task<DevbookFolderLocation> PrepareContentAsync(
        string key,
        string? repositoryAlias = null,
        IReadOnlyCollection<string>? relativePaths = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnreadable();
        return inner.PrepareContentAsync(key, repositoryAlias, relativePaths, cancellationToken);
    }

    public IDevbookFileTree FileTree(DevbookFolderLocation location) => inner.FileTree(location);

    public void NotifyContentChanged() => inner.NotifyContentChanged();

    private void ThrowIfUnreadable()
    {
        if (_unreadable) throw new DirectoryNotFoundException("The devbook folder moved while it was being read.");
    }
}
