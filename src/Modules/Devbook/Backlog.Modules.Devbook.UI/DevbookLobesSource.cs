using Backlog.Modules.Devbook.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Where the Lobes atlas takeover reads its graph from
/// (<c>.devbook/domain/devbook/features.md#lobes-atlas</c>): the Brain and every
/// registered devbook, read in one go, and a word whenever either changed.
/// <para>
/// A port of its own rather than the takeover reaching for
/// <see cref="DevbookAtlasService"/>, the Brain and the repository registry
/// itself, because which repositories count as devbooks and which Brain is meant
/// is a question with one answer, and the takeover only draws it.
/// </para>
/// </summary>
public interface IDevbookLobesSource
{
    /// <summary>Raised when the Brain's index or a devbook changed, so the atlas
    /// reads again.</summary>
    event Action? Changed;

    /// <summary>The Lobes graph as it stands now.</summary>
    Task<DevbookLobesGraph> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The application's <see cref="IDevbookLobesSource"/>: the Brain the host
/// composes, and the configured repositories with a devbook to read, in the
/// registry's order.
/// </summary>
internal sealed class DevbookLobesSource(DevbookAtlasService atlas, DevbookScope scope, IServiceProvider services) : IDevbookLobesSource
{
    /// <summary>Asked for on every read and every subscription: a host composes
    /// one Brain or none, and asking late keeps a host without one working.</summary>
    private IBrainFolderSource? Brain => services.GetService<IBrainFolderSource>();

    public event Action? Changed
    {
        add
        {
            atlas.Changed += value;
            if (Brain is { } brain) brain.Changed += value;
        }
        remove
        {
            atlas.Changed -= value;
            if (Brain is { } brain) brain.Changed -= value;
        }
    }

    public Task<DevbookLobesGraph> ReadAsync(CancellationToken cancellationToken = default) =>
        Brain is { } brain
            ? atlas.ReadLobesAsync(brain, scope.DevbookRepositoryAliases(), cancellationToken)
            : Task.FromResult(DevbookLobesGraph.Unavailable(DevbookAtlasService.NoBrainFolder));
}
