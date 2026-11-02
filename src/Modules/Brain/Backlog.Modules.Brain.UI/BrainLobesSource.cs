using Backlog.Desktop.UI.Devbook;
using Backlog.Infrastructure.Brain;
using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Devbook.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Brain.UI;

/// <summary>
/// The application's <see cref="IDevbookLobesSource"/>: the Brain the host
/// composes, and the configured repositories with a devbook to read, in the
/// registry's order. The Brain conforms to the Lobes atlas Devbook draws (local
/// ADR 0040 §2), so the Brain registers it in place of Devbook's empty one.
/// </summary>
internal sealed class BrainLobesSource(DevbookAtlasService devbookAtlas, BrainAtlas brainAtlas, DevbookScope scope, IServiceProvider services) : IDevbookLobesSource
{
    /// <summary>Asked for on every read and every subscription: a host composes
    /// one Brain or none, and asking late keeps a host without one working.</summary>
    private IBrainFolderSource? Brain => services.GetService<IBrainFolderSource>();

    public event Action? Changed
    {
        add
        {
            devbookAtlas.Changed += value;
            if (Brain is { } brain) brain.Changed += value;
        }
        remove
        {
            devbookAtlas.Changed -= value;
            if (Brain is { } brain) brain.Changed -= value;
        }
    }

    public Task<DevbookLobesGraph> ReadAsync(CancellationToken cancellationToken = default) =>
        Brain is { } brain
            ? brainAtlas.ReadLobesAsync(brain, scope.DevbookRepositoryAliases(), cancellationToken)
            : Task.FromResult(DevbookLobesGraph.Unavailable(BrainAtlas.NoBrainFolder));

    /// <summary>The app's one refresher, the same that builds the Brain in the
    /// background when it is read (local ADR 0015); a host without one — a test
    /// host — cannot build.</summary>
    private DevbookDatabaseRefresher? Refresher => services.GetService<DevbookDatabaseRefresher>();

    public bool CanBuildBrainIndex => Brain is { BrainRoot.Length: > 0 } && Refresher is not null;

    public async Task<string?> BuildBrainIndexAsync(CancellationToken cancellationToken = default)
    {
        if (Brain is not { BrainRoot: { Length: > 0 } root }) return BrainAtlas.NoBrainFolder;
        if (Refresher is not { } refresher) return BuildUnavailable;

        try
        {
            // Recorded as the Brain first, so the refresher builds it under the
            // Brain's convention rather than a repository's. A build that ran
            // raises the refresher's Rebuilt, which the Brain's watch turns into
            // Changed, and every open atlas reads again.
            BrainDatabaseLocation.RegisterBrainRoot(root);
            await refresher.RefreshAsync(root, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"The Brain's index could not be built: {ex.Message}";
        }
    }

    public DevbookRepositoryChapter? RepositoryChapterFor(string? raw) =>
        BrainRepoLink.Parse(raw) is { } link ? new DevbookRepositoryChapter(link.Alias, link.Reference) : null;

    /// <summary>What a host that cannot build the index says.</summary>
    internal const string BuildUnavailable = "This app cannot build the Brain's index from here.";
}
