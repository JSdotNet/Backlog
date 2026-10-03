using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The Devbook as the reference port sees it: a fixed answer per reference, and
/// every question recorded.
/// <para>
/// A reference with no fixed answer comes back
/// <see cref="DevbookReferenceState.UnknownPage"/> titled with itself, which is
/// what the real adapter says about a page nobody wrote — so a test that forgets
/// to seed one sees a broken link rather than a made-up chapter.
/// </para>
/// </summary>
internal sealed class FakeDevbookReferenceResolver(params ResolvedDevbookReference[] known) : IDevbookReferenceResolver
{
    private readonly Dictionary<string, ResolvedDevbookReference> _known =
        known.ToDictionary(answer => answer.Reference, StringComparer.Ordinal);

    /// <summary>Every <c>ResolveAsync</c>: the repository alias it was scoped to
    /// and the references, as they arrived.</summary>
    public List<(string? Alias, IReadOnlyList<string> References)> Resolved { get; } = [];

    public Task<IReadOnlyList<ResolvedDevbookReference>> ResolveAsync(
        string? repositoryAlias,
        IReadOnlyList<string> references,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Resolved.Add((repositoryAlias, [.. references]));

        return Task.FromResult<IReadOnlyList<ResolvedDevbookReference>>(
        [
            .. references.Select(reference => _known.TryGetValue(reference, out var answer)
                ? answer
                : new ResolvedDevbookReference(reference, reference, null, DevbookReferenceState.UnknownPage, reference, null, null))
        ]);
    }

    public Task<IReadOnlyList<DevbookReferenceTarget>> ListTargetsAsync(
        string? repositoryAlias,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DevbookReferenceTarget>>([]);
}
