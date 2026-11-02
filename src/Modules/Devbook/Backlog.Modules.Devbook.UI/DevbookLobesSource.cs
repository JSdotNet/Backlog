using Backlog.Modules.Devbook.Abstractions;

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
/// <para>
/// The personal devbook conforms to it (local ADR 0040 §2): the context that
/// supplies that devbook registers the implementation and reads it, and Devbook
/// only draws what it hands back.
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

    /// <summary>The repository chapter a link out of the personal devbook names,
    /// as the atlas's raw link text wrote it, or null when it names none.</summary>
    DevbookRepositoryChapter? RepositoryChapterFor(string? raw);
}

/// <summary>
/// The <see cref="IDevbookLobesSource"/> of a host that composes no personal
/// devbook: an atlas with nothing to map, that cannot build anything. The
/// takeover falls back to it when the context that supplies the personal devbook
/// registered no source of its own (local ADR 0040 §2).
/// </summary>
internal sealed class UnavailableLobesSource : IDevbookLobesSource
{
    public static UnavailableLobesSource Instance { get; } = new();


    public event Action? Changed
    {
        add { }
        remove { }
    }

    public Task<DevbookLobesGraph> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DevbookLobesGraph.Unavailable(NothingToMap));

    public bool CanBuildBrainIndex => false;

    public Task<string?> BuildBrainIndexAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(NothingToMap);

    public DevbookRepositoryChapter? RepositoryChapterFor(string? raw) => null;

    /// <summary>What the atlas says in a host with no personal devbook.</summary>
    internal const string NothingToMap = "Nothing to map here.";
}
