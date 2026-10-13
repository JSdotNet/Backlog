namespace Backlog.SharedKernel;

/// <summary>
/// The Brain as a member of the header's repository scope (local ADR 0027): the
/// scope of everything filed against no repository.
/// <para>
/// It travels in the same ordered list of aliases the repositories do, as the
/// reserved value <see cref="Value"/>, so the anchor rule, Ctrl and the remembered
/// order apply to it unchanged. The empty string, because no configured alias is
/// ever empty — <c>GitHubRepositoryRef.NormalizeAlias</c> trims and lower-cases one,
/// and a blank alias falls back to the repository name — so the value cannot
/// collide with a repository, where a readable word such as <c>brain</c> would
/// (ADR 0027, <em>Rejected</em>).
/// </para>
/// <para>
/// Every reader of a scope list has to know that <see cref="Value"/> means Brain.
/// The helpers here are that knowledge, kept in one place.
/// </para>
/// </summary>
public static class BrainScope
{
    /// <summary>The reserved scope member meaning Brain: the empty string.</summary>
    public const string Value = "";

    /// <summary>What the Brain is called wherever a scope is put into words.</summary>
    public const string Name = "Brain";

    /// <summary>Whether <paramref name="member"/> is the Brain. Null is not: only the
    /// exact reserved value is.</summary>
    public static bool Is(string? member) => member is { Length: 0 };

    /// <summary>Whether the scope holds the Brain.</summary>
    public static bool In(IEnumerable<string?> scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.Any(Is);
    }

    /// <summary>The scope's repositories, the Brain left out, in their order.</summary>
    public static IReadOnlyList<string> Repositories(IEnumerable<string?> scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return [.. scope.Where(member => !string.IsNullOrEmpty(member)).Select(member => member!)];
    }

    /// <summary>A scope member as a reader sees it: <see cref="Name"/> for the Brain,
    /// else the alias itself.</summary>
    public static string Display(string member) => Is(member) ? Name : member;

    /// <summary>The scope as prose, in its order, comma-separated, the Brain by name.
    /// Empty for an empty scope, so a caller writes its own "every repository".</summary>
    public static string Label(IEnumerable<string> scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return string.Join(", ", scope.Select(Display));
    }
}
