using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A Devbook that answers from a table rather than from a folder: what each
/// reference resolves to and what each repository offers a picker. A reference it
/// was not told about is <see cref="DevbookReferenceState.UnknownPage"/>, which is
/// what the real adapter answers for a path with no page behind it.
/// <para>
/// It records the repository every question was asked about, because which
/// repository a task's references are read against is half of what the pane
/// decides.
/// </para>
/// </summary>
internal sealed class FakeDevbookReferenceResolver : IDevbookReferenceResolver
{
    private readonly Dictionary<string, ResolvedDevbookReference> _known = new(StringComparer.Ordinal);

    public Dictionary<string, IReadOnlyList<DevbookReferenceTarget>> Targets { get; } = new(StringComparer.Ordinal);

    public List<string?> ResolvedFor { get; } = [];

    public List<string?> ListedFor { get; } = [];

    public FakeDevbookReferenceResolver Chapter(string reference, string title, string? status = null)
    {
        var hash = reference.IndexOf('#');
        _known[reference] = new ResolvedDevbookReference(
            reference,
            reference[..hash],
            reference[(hash + 1)..],
            DevbookReferenceState.Chapter,
            title,
            status,
            "domain");
        return this;
    }

    public FakeDevbookReferenceResolver Page(string reference, string title)
    {
        _known[reference] = new ResolvedDevbookReference(reference, reference, null, DevbookReferenceState.Page, title, null, "domain");
        return this;
    }

    public FakeDevbookReferenceResolver Broken(string reference, DevbookReferenceState state)
    {
        var hash = reference.IndexOf('#');
        _known[reference] = new ResolvedDevbookReference(
            reference,
            hash < 0 ? reference : reference[..hash],
            hash < 0 ? null : reference[(hash + 1)..],
            state,
            reference,
            null,
            null);
        return this;
    }

    public Task<IReadOnlyList<ResolvedDevbookReference>> ResolveAsync(
        string? repositoryAlias,
        IReadOnlyList<string> references,
        CancellationToken cancellationToken = default)
    {
        lock (ResolvedFor) ResolvedFor.Add(repositoryAlias);

        IReadOnlyList<ResolvedDevbookReference> answer =
        [
            .. references.Select(reference => _known.TryGetValue(reference, out var known)
                ? known
                : new ResolvedDevbookReference(reference, reference, null, DevbookReferenceState.UnknownPage, reference, null, null))
        ];

        return Task.FromResult(answer);
    }

    public Task<IReadOnlyList<DevbookReferenceTarget>> ListTargetsAsync(
        string? repositoryAlias,
        CancellationToken cancellationToken = default)
    {
        lock (ListedFor) ListedFor.Add(repositoryAlias);

        return Task.FromResult(
            Targets.TryGetValue(repositoryAlias ?? string.Empty, out var targets)
                ? targets
                : (IReadOnlyList<DevbookReferenceTarget>)[]);
    }
}
