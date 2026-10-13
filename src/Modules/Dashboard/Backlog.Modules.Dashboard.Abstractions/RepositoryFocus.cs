using Backlog.SharedKernel;

namespace Backlog.Modules.Dashboard.Abstractions;

/// <summary>
/// Which repositories the dashboard is looking at: every one of them, or a set of
/// aliases in the order they were taken into focus.
/// <para>
/// A set rather than one alias, because the focus is the shell's repository scope
/// and that scope holds several repositories at once — the same chips that narrow
/// the task list. The first alias is the <em>anchor</em>, the one a reader that
/// can take one repository and not several takes: the trend highlights it, the way
/// Devbook reads it.
/// </para>
/// <para>
/// A class with its own equality rather than a record over a list. Every part
/// decides whether to re-fetch by comparing the scope it last fetched for against
/// the one it was given, and <see cref="DashboardScope"/> is a record precisely so
/// that comparison is by value. A list member would compare by reference and make
/// every render look like a new focus; this compares the aliases, ignoring case,
/// so an unchanged scope is an equal scope.
/// </para>
/// <para>
/// The Brain (local ADR 0027) is a member like any other, held as the reserved
/// <see cref="BrainScope.Value"/>: the scope of everything filed against no
/// repository. It names no repository, so <see cref="Contains"/> never matches an
/// alias with it; <see cref="IncludesBrain"/> is how a part asks for the work filed
/// nowhere, and <see cref="RepositoryAliases"/> is the focus with the Brain left out.
/// </para>
/// </summary>
public sealed class RepositoryFocus : IEquatable<RepositoryFocus>
{
    /// <summary>Every repository: nothing in focus.</summary>
    public static RepositoryFocus All { get; } = new([]);

    private readonly string[] _aliases;

    private RepositoryFocus(string[] aliases) => _aliases = aliases;

    /// <summary>
    /// The focus on <paramref name="aliases"/>, in the order given, blanks and
    /// duplicates dropped — the Brain's reserved empty value kept, in its place.
    /// Nothing left is <see cref="All"/>.
    /// </summary>
    public static RepositoryFocus Of(IEnumerable<string?> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);

        var distinct = aliases
            .Where(alias => BrainScope.Is(alias) || !string.IsNullOrWhiteSpace(alias))
            .Select(alias => alias!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return distinct.Length == 0 ? All : new RepositoryFocus(distinct);
    }

    /// <summary>The focus on one repository, or <see cref="All"/> for a blank alias.</summary>
    public static RepositoryFocus Of(params string?[] aliases) => Of((IEnumerable<string?>)aliases);

    /// <summary>The aliases in focus, in the order they were taken. Empty for all.</summary>
    public IReadOnlyList<string> Aliases => _aliases;

    /// <summary>True when no repository is in focus.</summary>
    public bool IsAll => _aliases.Length == 0;

    /// <summary>True when the Brain is in focus: the work filed against no repository.</summary>
    public bool IncludesBrain => Array.Exists(_aliases, BrainScope.Is);

    /// <summary>The repositories in focus, the Brain left out, in the order taken.</summary>
    public IReadOnlyList<string> RepositoryAliases => BrainScope.Repositories(_aliases);

    /// <summary>Whether work filed against <paramref name="aliases"/> is in focus:
    /// everything is when nothing is focused; work filed against no repository is when
    /// the Brain is; otherwise work in a focused repository.</summary>
    public bool Covers(IReadOnlyCollection<string> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);

        return IsAll || (aliases.Count == 0 ? IncludesBrain : aliases.Any(Contains));
    }

    /// <summary>The first alias in focus, or null for all — what a reader that can
    /// take one repository and not several takes.</summary>
    public string? Anchor => _aliases.Length == 0 ? null : _aliases[0];

    /// <summary>Whether <paramref name="alias"/> is in focus. Everything is when
    /// nothing is.</summary>
    public bool Contains(string? alias) =>
        IsAll || (!string.IsNullOrEmpty(alias) && Array.Exists(_aliases, one => string.Equals(one, alias, StringComparison.OrdinalIgnoreCase)));

    /// <summary>The focus as one cache key: <c>*</c> for all, else the aliases
    /// lower-cased and sorted, so two focuses on the same repositories share one
    /// fetch whatever order they were taken in.</summary>
    public string Key => IsAll
        ? "*"
        : string.Join(",", _aliases.Select(alias => alias.ToLowerInvariant()).Order(StringComparer.Ordinal));

    /// <summary>The focus as prose: the aliases in the order taken, comma-separated,
    /// the Brain by name. Empty for all, so a caller writes its own "every repository".</summary>
    public string Label => BrainScope.Label(_aliases);

    public bool Equals(RepositoryFocus? other) =>
        other is not null && string.Equals(Key, other.Key, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as RepositoryFocus);

    public override int GetHashCode() => Key.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => IsAll ? "every repository" : Label;
}
