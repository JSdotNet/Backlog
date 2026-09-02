using Backlog.Modules.Backlog.Abstractions.Services;

namespace Backlog.Modules.Backlog.UnitTests;

/// <summary>
/// The repository registry, small enough to read, standing in for the adapter
/// over Settings.
/// <para>
/// Shared by every test in this project that goes near a <c>repo:</c> value —
/// Import, the ordinary text save, and reconciliation — because a second fake
/// with the same job would be a second answer to "what does a name resolve to",
/// which is exactly the thing the resolver exists to have one of.
/// </para>
/// <para>
/// It records what was asked of it as well as answering, and the recordings are
/// half the point: several of these tests are about <em>whether</em> a name was
/// resolved or registered at all, which a directory that only returned answers
/// could not show.
/// </para>
/// <para>
/// It mirrors the real adapter rather than being convenient: resolution
/// dispatches on shape, an id compares without regard to case, an alias compares
/// exactly after the same normalization, and registering a bare name produces
/// owner and name standing in as the alias — which is why a registered
/// <c>newcomer</c> has the id <c>newcomer/newcomer</c> here, exactly as it would
/// in Settings.
/// </para>
/// </summary>
internal sealed class FakeRepositoryDirectory : IRepositoryDirectory
{
    private readonly List<BacklogRepositoryRef> _repositories;

    /// <summary>Repositories named by alias alone, under an owner nobody has to
    /// care about. Enough for a test whose subject is whether a name resolved at
    /// all.</summary>
    public FakeRepositoryDirectory(params string[] known)
        : this([.. known.Select(alias => new BacklogRepositoryRef(alias, "someone", alias))])
    {
    }

    /// <summary>Repositories stated in full, for a test whose subject is the id
    /// itself — its casing, or the difference between the label and the
    /// coordinate.</summary>
    public FakeRepositoryDirectory(IEnumerable<BacklogRepositoryRef> known) => _repositories = [.. known];

    public List<string> Resolved { get; } = [];

    public List<string> Registered { get; } = [];

    public IReadOnlyList<BacklogRepositoryRef> Repositories => _repositories;

    public BacklogRepositoryRef? Resolve(string name)
    {
        Resolved.Add(name);
        return Find(name);
    }

    public BacklogRepositoryRef Register(string name)
    {
        // Every call is recorded, and recorded as it was asked: a second call for
        // a name already registered is exactly the thing the resolver's
        // memoization exists to prevent, and a fake that swallowed it would make
        // the test pass whether the memoization was there or not.
        Registered.Add(name);

        var existing = Find(name);
        if (existing is not null) return existing;

        // The real adapter reads a coordinate through the same grammar the
        // Settings text box uses, and falls back to the placeholder only for a
        // bare name.
        var parts = name.Trim().Split('/');
        var added = parts.Length == 2 && parts.All(part => part.Trim().Length > 0)
            ? new BacklogRepositoryRef(Normalize(parts[1]), parts[0].Trim(), parts[1].Trim())
            : new BacklogRepositoryRef(Normalize(name), Normalize(name), Normalize(name));

        _repositories.Add(added);
        return added;
    }

    /// <summary>Shape dispatch, the rule the real directory applies: a name with
    /// a <c>/</c> is an id and is matched without regard to case, anything else
    /// is an alias and is matched exactly.</summary>
    private BacklogRepositoryRef? Find(string name) =>
        name.Contains('/', StringComparison.Ordinal)
            ? _repositories.FirstOrDefault(repository =>
                string.Equals(repository.Id, name.Trim(), StringComparison.OrdinalIgnoreCase))
            : _repositories.FirstOrDefault(repository =>
                string.Equals(repository.Alias, Normalize(name), StringComparison.Ordinal));

    /// <summary>The same lower-cased trim the real adapter applies, so a test
    /// asserting on an alias asserts on the form the workspace stores.</summary>
    private static string Normalize(string name) => name.Trim().ToLowerInvariant();
}
