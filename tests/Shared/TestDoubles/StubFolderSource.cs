using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Tests;

/// <summary>
/// A devbook folder source that resolves every area against one root and stamps the
/// source on it, or that only lists the folders it was given.
/// <para>
/// Deliberately not the real <c>DevbookFolderSource</c> over a real settings pair: a
/// test about what a store or a panel does with a location it was handed would
/// otherwise put the resolution under test in every assertion.
/// </para>
/// </summary>
internal sealed class StubFolderSource : IDevbookFolderSource
{
    private readonly string? _root;
    private readonly IReadOnlyList<DevbookFolderSetting> _folders;

    /// <summary>Resolves every area against <paramref name="root"/> in the legacy
    /// layout, which the fixtures sit in and the real source reaches through its
    /// fallback, stamped with <paramref name="source"/>.</summary>
    public StubFolderSource(string root, DevbookSourceKind source)
    {
        _root = root;
        _folders = DevbookFolderSetting.Defaults();
        Source = source;
    }

    /// <summary>Lists <paramref name="folders"/> and resolves none: nothing the test
    /// covers asks where a folder is.</summary>
    public StubFolderSource(IReadOnlyList<DevbookFolderSetting> folders) => _folders = folders;

    /// <summary>The source stamped on every location; a test switches it, then
    /// announces the switch with <see cref="RaiseChanged"/>, the way the settings
    /// store republishes one.</summary>
    public DevbookSourceKind Source { get; set; }

    /// <summary>Whether the repository is archived, stamped on every location the way
    /// the real source stamps it.</summary>
    public bool Archived { get; set; }

    /// <summary>The named branch a snapshot was read from, stamped on every location
    /// the way the real source stamps it; null for the default branch.</summary>
    public string? Branch { get; set; }

    public event Action? Changed;

    public void RaiseChanged() => Changed?.Invoke();

    public void NotifyContentChanged() => Changed?.Invoke();

    public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) => _folders;

    public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null)
    {
        if (_root is null)
            return DevbookFolderLocation.Unavailable(key, "Nothing here asks where the folder is.");

        var folder = DevbookFolderSetting.Defaults()
            .First(setting => string.Equals(setting.Key, key, StringComparison.OrdinalIgnoreCase));
        folder = folder with { ResolvedPath = folder.LegacyRelativePath };

        var full = Path.Combine(_root, folder.EffectivePath);

        return Directory.Exists(full)
            ? new DevbookFolderLocation(key, true, null, "JSdotNet/Backlog", folder, full, _root, "JSdotNet/Backlog", "backlog", Source, RepositoryArchived: Archived, Branch: Branch)
            : DevbookFolderLocation.Unavailable(key, $"No {key}.", source: Source, repositoryArchived: Archived) with { Branch = Branch };
    }
}
