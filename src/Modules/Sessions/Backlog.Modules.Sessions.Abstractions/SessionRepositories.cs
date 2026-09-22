namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// PORT — which registered repository a working folder lies inside, on this
/// machine.
/// <para>
/// Repository Management owns the repository list and the clone directory each
/// entry was registered with; this context asks it one question and holds the
/// answer on <see cref="AgentSession.ResolvedRepository"/>. A port of one method
/// rather than the directory itself, for the reason the sync side's alias port is:
/// a session reader must not be able to register a repository by accident, and a
/// port that can only answer cannot.
/// </para>
/// <para>
/// The host decides what "registered" means. A head that has not composed a
/// repository list at all leaves this unregistered, and the source then resolves
/// nothing — which is the true answer on that head, not a fault.
/// </para>
/// </summary>
public interface ISessionRepositoryResolver
{
    /// <summary>
    /// The <c>owner/name</c> of the registered repository whose clone contains
    /// <paramref name="workingFolder"/>, or null when none does. Null is an
    /// ordinary answer: a session outside every registered clone, a repository
    /// registered without a clone directory, and a folder this machine cannot
    /// place are all the same absence.
    /// </summary>
    string? RepositoryOf(string workingFolder);
}

/// <summary>One registered clone, as the resolver needs it: the repository it
/// is a clone of, and where on this disk it sits.</summary>
/// <param name="Repository">The <c>owner/name</c> the clone was registered against.</param>
/// <param name="CloneDirectory">The clone's root folder on this machine.</param>
public sealed record RegisteredClone(string Repository, string CloneDirectory);

/// <summary>
/// Placing a working folder inside a registered clone. A pure function over the
/// clones it is given — no I/O, no settings store — so the containment rule can be
/// pinned by a test without a Repositories screen behind it, and so an adapter over
/// any registry is a projection into <see cref="RegisteredClone"/> and nothing more.
/// </summary>
public static class RegisteredClones
{
    /// <summary>
    /// The repository whose clone contains <paramref name="workingFolder"/>, or null.
    /// <para>
    /// Containment is by path segment, not by prefix: <c>D:\Repos\Backlog</c> holds
    /// <c>D:\Repos\Backlog\.claude\worktrees\x</c> and does not hold
    /// <c>D:\Repos\Backlog2</c>. The clone itself counts as inside — a session
    /// opened at the clone root is the common case, not an edge.
    /// </para>
    /// <para>
    /// The deepest clone wins. A clone registered inside another clone — a
    /// submodule, a nested checkout somebody registered separately — is the more
    /// specific claim about a folder under it, and the outer clone would otherwise
    /// swallow every session in the inner one.
    /// </para>
    /// <para>
    /// Case-insensitive on Windows and case-sensitive elsewhere, the way the file
    /// system itself compares them. Both sides are normalised to one separator and
    /// stripped of a trailing one, because the agents write the folder the way
    /// their own runtime spells it and the Repositories screen stores it the way a
    /// person typed it, and the two are allowed to disagree about a slash.
    /// </para>
    /// </summary>
    public static string? RepositoryContaining(string? workingFolder, IEnumerable<RegisteredClone> clones)
    {
        ArgumentNullException.ThrowIfNull(clones);

        var folder = Normalize(workingFolder);
        if (folder is null) return null;

        RegisteredClone? best = null;
        var bestLength = -1;

        foreach (var clone in clones)
        {
            var root = Normalize(clone.CloneDirectory);
            if (root is null || string.IsNullOrWhiteSpace(clone.Repository)) continue;

            if (Contains(root, folder) && root.Length > bestLength)
            {
                best = clone;
                bestLength = root.Length;
            }
        }

        return best?.Repository;
    }

    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool Contains(string root, string folder) =>
        folder.Length == root.Length
            ? string.Equals(folder, root, Comparison)
            : folder.Length > root.Length
                && folder.StartsWith(root, Comparison)
                && folder[root.Length] == '/';

    /// <summary>Forward slashes throughout and no trailing one, or null for a
    /// blank. Not <c>Path.GetFullPath</c>: that would resolve a path from another
    /// machine against this one's current directory, and a relative folder is not
    /// something either agent writes.</summary>
    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var normalized = path.Trim().Replace('\\', '/');
        while (normalized.Length > 1 && normalized.EndsWith('/'))
        {
            normalized = normalized[..^1];
        }

        return normalized;
    }
}
