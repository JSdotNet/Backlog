using Backlog.Desktop.UI.Devbook;
using Backlog.Infrastructure.Brain;
using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Modules.Brain.UI;

/// <summary>
/// The Brain's atlas (local ADR 0027 §6): its notes and the links between
/// them, read from the Brain's own database in the app's storage, with every
/// <c>repo:</c> link drawn as an edge to a node for the repository chapter it
/// names — labelled with the repository alias, and dashed where this machine
/// cannot follow it (<see cref="BrainRepoLinkResolver"/>, asked of the
/// repositories configured here through the same folder source the
/// repository atlas reads).
/// <para>
/// No Brain set, no database built yet, or one that will not open all say so
/// rather than drawing an empty map, for the reason the repository atlas does:
/// a map of links cannot be drawn from the note on screen. Nothing is read
/// from or written to a repository; the repository nodes are the links' own
/// words.
/// </para>
/// </summary>
public sealed class BrainAtlas(IDevbookFolderSource repositories)
{
    public Task<DevbookAtlasGraph> ReadBrainAsync(IBrainFolderSource brain, CancellationToken cancellationToken = default) =>
        ReadBrainAsync(brain, DatabaseFor, cancellationToken);

    /// <summary>Where a Brain folder's database is: the app's storage, unless a
    /// test points it at a scratch folder.</summary>
    internal Func<string, string?> DatabaseFor { get; init; } = BrainDatabaseLocation.ForBrainFolder;

    /// <param name="brain">The Brain, asked for its folder on every read so a
    /// moved setting is followed at once.</param>
    /// <param name="databaseFor">Where that folder's database is.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    internal Task<DevbookAtlasGraph> ReadBrainAsync(IBrainFolderSource brain, Func<string, string?> databaseFor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(databaseFor);

        var scope = DevbookAtlasScope.Brain;
        if (brain.BrainRoot is not { Length: > 0 } root)
        {
            return Task.FromResult(DevbookAtlasGraph.Unavailable(scope, NoBrainFolder));
        }

        return Task.Run(() => ReadBrain(scope, root, databaseFor), cancellationToken);
    }

    private DevbookAtlasGraph ReadBrain(DevbookAtlasScope scope, string root, Func<string, string?> databaseFor)
    {
        try
        {
            using var database = DevbookDatabase.TryOpen(databaseFor(root));
            if (database is null) return DevbookAtlasGraph.Unavailable(scope, BrainIndexMissing);

            var nodes = database.Nodes()
                .Select(node => new DevbookAtlasSourceNode(
                    node.Id,
                    node.Label ?? string.Empty,
                    node.Folder ?? string.Empty,
                    node.Type,
                    node.Status ?? string.Empty,
                    node.Path ?? string.Empty,
                    node.OutOfScope))
                .ToList();

            var edges = database.Edges()
                .Select(edge => new DevbookAtlasSourceEdge(edge.Type, edge.Source, edge.Target))
                .ToList();

            var resolver = new BrainRepoLinkResolver(repositories);
            return BrainAtlasReader.ReadBrain(scope, nodes, edges, resolver.Resolve);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            // Busy, or replaced by a rebuild under the read. The next change asks
            // again.
            return DevbookAtlasGraph.Unavailable(scope, $"The Brain atlas could not be read: {ex.Message}");
        }
    }

    /// <summary>
    /// The Lobes atlas (<c>.devbook/domain/devbook/features.md#lobes-atlas</c>):
    /// the Brain and the devbook of every registered repository in one graph,
    /// read from the Brain's database and each repository's devbook database and
    /// nothing else. Each node carries its scope, its region and its kind; each
    /// <c>repo:</c> link is resolved as the Brain atlas resolves it
    /// (<see cref="BrainRepoLinkResolver"/>) and drawn to that repository's
    /// chapter node, or into a ghost devbook when its alias is not registered.
    /// <para>
    /// No Brain folder, or no Brain index yet, says so rather than drawing a map
    /// without the Brain. A registered repository with no index yet is still a
    /// scope, unavailable, holding only the chapters the notes name.
    /// </para>
    /// </summary>
    /// <param name="brain">The Brain, asked for its folder on every read.</param>
    /// <param name="repositoryAliases">The registered repositories that have a
    /// devbook, in the order their scopes are offered.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public Task<DevbookLobesGraph> ReadLobesAsync(IBrainFolderSource brain, IReadOnlyList<string> repositoryAliases, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(repositoryAliases);

        if (brain.BrainRoot is not { Length: > 0 } root)
        {
            return Task.FromResult(DevbookLobesGraph.Unavailable(NoBrainFolder));
        }

        return Task.Run(() => ReadLobes(root, brain.Areas(), repositoryAliases, DatabaseFor, cancellationToken), cancellationToken);
    }

    private DevbookLobesGraph ReadLobes(string root, IReadOnlyList<BrainArea> declared, IReadOnlyList<string> repositoryAliases, Func<string, string?> databaseFor, CancellationToken cancellationToken)
    {
        try
        {
            BrainLobesNotes brain;
            using (var database = DevbookDatabase.TryOpen(databaseFor(root)))
            {
                if (database is null) return DevbookLobesGraph.MissingBrainIndex(BrainIndexMissing);

                var para = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var values in database.Attributes("para"))
                {
                    if (values.FirstOrDefault() is { Length: > 0 } value) para[values.Key] = value;
                }

                brain = new BrainLobesNotes(
                    DevbookAtlasService.SourceNodes(database.Nodes()),
                    DevbookAtlasService.SourceEdges(database.Edges()),
                    para,
                    [.. database.Outline(DevbookDatabaseSchema.RepositoryScope)
                        .Where(row => row.Type == "area")
                        .Select(row => row.Path)],
                    declared,
                    DevbookAtlasService.IndexedAt(database));
            }

            var devbookAtlas = new DevbookAtlasService(repositories);
            var devbooks = new List<DevbookLobesDevbookSource>();
            foreach (var alias in repositoryAliases.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                devbooks.Add(devbookAtlas.ReadLobesDevbook(alias));
            }

            return BrainLobesReader.Read(brain, devbooks, new BrainRepoLinkResolver(repositories).Resolve);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            return DevbookLobesGraph.Unavailable($"The Lobes atlas could not be read: {ex.Message}");
        }
    }

    /// <summary>What the Brain atlas says while no Brain folder is set.</summary>
    internal const string NoBrainFolder = "No Brain folder is set, so there is no Brain to map.";

    /// <summary>What the Brain atlas says before the Brain's index exists.</summary>
    internal const string BrainIndexMissing =
        "The Brain atlas needs the Brain's index, which has not been built yet. Backlog builds it in the background when the Brain is opened — try again in a moment.";
}
