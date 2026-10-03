using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The Devbook a host that has wired none stands in with: nobody can look, so every
/// reference is <see cref="DevbookReferenceState.Unverified"/> and the picker offers
/// nothing but what is typed.
/// <para>
/// A null object, for the reason <see cref="EmptyRoadmapTagSource"/> is one. The
/// desktop heads compose the real adapter beside the Devbook they already read; a
/// host that composes no Devbook — the storybook's state-free pages, a test about
/// something else — still draws an entry's references, as the stored text, rather
/// than refusing to build the list.
/// </para>
/// </summary>
internal sealed class UnavailableDevbookReferenceResolver : IDevbookReferenceResolver
{
    public static UnavailableDevbookReferenceResolver Instance { get; } = new();

    public Task<IReadOnlyList<ResolvedDevbookReference>> ResolveAsync(
        string? repositoryAlias,
        IReadOnlyList<string> references,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ResolvedDevbookReference>>([.. references.Select(Unverified)]);

    public Task<IReadOnlyList<DevbookReferenceTarget>> ListTargetsAsync(
        string? repositoryAlias,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DevbookReferenceTarget>>([]);

    /// <summary>A reference as nobody has read it: its own text for a title, and
    /// no claim about whether it is there.</summary>
    internal static ResolvedDevbookReference Unverified(string reference)
    {
        var hash = reference.IndexOf('#');

        return new ResolvedDevbookReference(
            reference,
            hash < 0 ? reference : reference[..hash],
            hash < 0 ? null : reference[(hash + 1)..],
            DevbookReferenceState.Unverified,
            reference,
            Status: null,
            Folder: null);
    }
}
