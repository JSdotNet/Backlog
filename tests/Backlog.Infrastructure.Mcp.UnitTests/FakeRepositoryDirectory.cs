using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The repository registry, small enough to read, standing in for the adapter
/// over Settings.
/// <para>
/// The same double <c>Backlog.Modules.Tasks.UnitTests</c> keeps, and for the same
/// reason: resolution dispatches on shape, an id compares without regard to case,
/// an alias compares exactly. A second answer to "what does a name resolve to"
/// would make these tests agree with themselves and with nothing else. It is
/// copied rather than shared because it is <c>internal</c> to that assembly, and
/// a test double is not a thing to publish out of one test project into another.
/// </para>
/// <para>
/// It records as well as answers, and the recordings are half the point here:
/// several of these tests are about <em>whether</em> a name was registered, which
/// a directory that only returned answers could not show.
/// </para>
/// </summary>
internal sealed class FakeRepositoryDirectory : IRepositoryDirectory
{
    private readonly List<TasksRepositoryRef> _repositories;

    /// <summary>Repositories named by alias alone, under an owner nobody has to
    /// care about.</summary>
    public FakeRepositoryDirectory(params string[] known)
        : this([.. known.Select(alias => new TasksRepositoryRef(alias, "someone", alias))])
    {
    }

    /// <summary>Repositories stated in full, for a test whose subject is the id
    /// itself — its casing, or the difference between the label and the
    /// coordinate.</summary>
    public FakeRepositoryDirectory(IEnumerable<TasksRepositoryRef> known) => _repositories = [.. known];

    public List<string> Resolved { get; } = [];

    public List<string> Registered { get; } = [];

    public IReadOnlyList<TasksRepositoryRef> Repositories => _repositories;

    public TasksRepositoryRef? Resolve(string name)
    {
        Resolved.Add(name);
        return Find(name);
    }

    public TasksRepositoryRef Register(string name)
    {
        // Recorded and then honoured, rather than throwing. A double that threw
        // would make "never registers" true of the test harness instead of true
        // of the tool, and the assertion has to be able to fail.
        Registered.Add(name);

        var existing = Find(name);
        if (existing is not null) return existing;

        var parts = name.Trim().Split('/');
        var added = parts.Length == 2 && parts.All(part => part.Trim().Length > 0)
            ? new TasksRepositoryRef(Normalize(parts[1]), parts[0].Trim(), parts[1].Trim())
            : new TasksRepositoryRef(Normalize(name), Normalize(name), Normalize(name));

        _repositories.Add(added);
        return added;
    }

    /// <summary>Shape dispatch, the rule the real directory applies: a name with
    /// a <c>/</c> is an id and is matched without regard to case, anything else
    /// is an alias and is matched exactly.</summary>
    private TasksRepositoryRef? Find(string name) =>
        name.Contains('/', StringComparison.Ordinal)
            ? _repositories.FirstOrDefault(repository =>
                string.Equals(repository.Id, name.Trim(), StringComparison.OrdinalIgnoreCase))
            : _repositories.FirstOrDefault(repository =>
                string.Equals(repository.Alias, Normalize(name), StringComparison.Ordinal));

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();
}
