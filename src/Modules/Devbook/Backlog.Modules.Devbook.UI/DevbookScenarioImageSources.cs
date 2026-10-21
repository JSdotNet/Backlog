using System.Collections.Concurrent;

using Backlog.UI.Components.Devbook;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// One <see cref="DevbookScenarioImages"/> per repository, for a host that draws
/// <c>scenario:&lt;scenarios file&gt;#&lt;label&gt;</c> images outside the Devbook pane — the
/// shell, answering the Tasks pane, which may not reach the scenarios files itself.
/// <para>
/// <see cref="For"/> answers at once with the repository's source and fills its
/// catalog from <see cref="DevbookScenarioStore"/> behind it, so a render never waits
/// on the files. Each source is the same instance for as long as this lives, because
/// a host cascades it fixed and a fixed cascade never hands a view a new value; what
/// changes is the catalog inside it, and <see cref="Changed"/> says when, so the host
/// draws again.
/// </para>
/// </summary>
public sealed class DevbookScenarioImageSources : IDisposable
{
    private readonly DevbookScenarioStore? _store;
    private readonly ConcurrentDictionary<string, DevbookScenarioImages> _sources = new(StringComparer.Ordinal);

    /// <summary>The latest load asked for each repository. Loads can land out of
    /// order — the first ask's read, then a folder change's — and only the latest
    /// may set the catalog, or a read from before the change would put it back.</summary>
    private readonly ConcurrentDictionary<string, int> _generations = new(StringComparer.Ordinal);

    public DevbookScenarioImageSources(DevbookScenarioStore? store)
    {
        _store = store;
        if (_store is not null) _store.Changed += OnStoreChanged;
    }

    /// <summary>Raised when a repository's catalog arrived or moved, so whoever drew
    /// its images draws them again.</summary>
    public event Action? Changed;

    /// <summary>The image source for a repository — null or empty for the store's
    /// default — or null when the host composed no scenario store, so every image
    /// keeps its ordinary rule.</summary>
    public IDevbookImageSource? For(string? repositoryAlias)
    {
        if (_store is null) return null;

        var key = repositoryAlias ?? string.Empty;
        if (_sources.TryGetValue(key, out var known)) return known;

        var images = _sources.GetOrAdd(key, _ => new DevbookScenarioImages(_store));
        _ = LoadAsync(key, images);
        return images;
    }

    public void Dispose()
    {
        if (_store is not null) _store.Changed -= OnStoreChanged;
    }

    /// <summary>The store forgot its catalogs: every repository asked about so far is
    /// read again.</summary>
    private void OnStoreChanged()
    {
        foreach (var (key, images) in _sources)
        {
            _ = LoadAsync(key, images);
        }
    }

    private async Task LoadAsync(string key, DevbookScenarioImages images)
    {
        var generation = _generations.AddOrUpdate(key, 1, (_, latest) => latest + 1);

        // No catch: the store already turns a failed read into the empty catalog
        // and keeps nothing of it, so the next ask reads the files again.
        var catalog = await _store!.LoadAsync(key.Length == 0 ? null : key).ConfigureAwait(false);
        if (_generations.TryGetValue(key, out var latest) && latest != generation) return;
        if (ReferenceEquals(catalog, images.Catalog)) return;

        images.Catalog = catalog;
        Changed?.Invoke();
    }
}
