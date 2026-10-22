using Backlog.Infrastructure.Devbook;
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

    /// <summary>Whether <see cref="BuildBrainIndexAsync"/> can build anything here:
    /// a Brain folder is set and the host runs the devbook database
    /// refresher.</summary>
    bool CanBuildBrainIndex { get; }

    /// <summary>Builds the Brain's index now, waiting for it, so the atlas's empty
    /// state can offer to build it rather than only say it is missing. Returns
    /// null when it was built — or was current already — and the reason in words
    /// when it could not be.</summary>
    Task<string?> BuildBrainIndexAsync(CancellationToken cancellationToken = default);
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

    /// <summary>The app's one refresher, the same that builds the Brain in the
    /// background when it is read (local ADR 0015); a host without one — a test
    /// host — cannot build.</summary>
    private DevbookDatabaseRefresher? Refresher => services.GetService<DevbookDatabaseRefresher>();

    public bool CanBuildBrainIndex => Brain is { BrainRoot.Length: > 0 } && Refresher is not null;

    public async Task<string?> BuildBrainIndexAsync(CancellationToken cancellationToken = default)
    {
        if (Brain is not { BrainRoot: { Length: > 0 } root }) return DevbookAtlasService.NoBrainFolder;
        if (Refresher is not { } refresher) return BuildUnavailable;

        try
        {
            // Recorded as the Brain first, so the refresher builds it under the
            // Brain's convention rather than a repository's. A build that ran
            // raises the refresher's Rebuilt, which the Brain's watch turns into
            // Changed, and every open atlas reads again.
            DevbookDatabaseLocation.RegisterBrainRoot(root);
            await refresher.RefreshAsync(root, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"The Brain's index could not be built: {ex.Message}";
        }
    }

    /// <summary>What a host that cannot build the index says.</summary>
    internal const string BuildUnavailable = "This app cannot build the Brain's index from here.";
}
