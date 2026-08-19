using Backlog.Modules.Roadmap.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.DomainModels;

/// <summary>
/// When a piece of planned work is intended to run: a first and a last day, both
/// inclusive. Equality is by value.
/// <para>
/// Inclusive on both ends because that is how a plan is spoken — "through the
/// 31st" means the 31st — and because an exclusive end makes the shortest
/// possible piece of work indistinguishable from no work at all.
/// </para>
/// </summary>
public sealed record PlannedWindow
{
    private PlannedWindow(DateOnly start, DateOnly end)
    {
        Start = start;
        End = end;
    }

    /// <summary>First day, inclusive.</summary>
    public DateOnly Start { get; }

    /// <summary>Last day, inclusive.</summary>
    public DateOnly End { get; }

    /// <summary>How many days it covers, counting both ends. Never less than
    /// one.</summary>
    public int Days => End.DayNumber - Start.DayNumber + 1;

    /// <summary>
    /// A window, or the reason those two dates are not one.
    /// <para>
    /// A refusal rather than an exception: dates that do not make a window are an
    /// ordinary thing to type into two fields, and the caller can act on the
    /// answer.
    /// </para>
    /// </summary>
    public static Result<PlannedWindow> Create(DateOnly start, DateOnly end) =>
        end < start
            ? Result.Failure<PlannedWindow>(RoadmapErrors.InvalidWindow(start, end))
            : Result.Success(new PlannedWindow(start, end));

    /// <summary>The same window, for a caller that has already established the
    /// dates are ordered — rehydrating something previously stored, or moving a
    /// window whose length is being carried.</summary>
    public static PlannedWindow Of(DateOnly start, DateOnly end) =>
        new(start, end <= start ? start : end);

    public bool Contains(DateOnly day) => day >= Start && day <= End;
}

/// <summary>
/// The repositories a piece of planned work belongs to: a set of repository
/// aliases, normalized, without duplicates. Equality is by value, and order does
/// not affect it — two items scoped to the same two repositories are scoped the
/// same way whichever order they were typed in.
/// <para>
/// An empty scope is valid and means unfiled. It is not an error, and it is not a
/// default repository.
/// </para>
/// <para>
/// Aliases are held as opaque strings and never resolved here. That is what keeps
/// this context independent of Repository Management: the plan holds what the
/// person wrote, and resolution happens on the read path, where an alias that no
/// longer matches a configured repository is a presentation outcome rather than a
/// broken plan.
/// </para>
/// </summary>
public sealed class RepositoryScope : IEquatable<RepositoryScope>
{
    private readonly string[] _aliases;

    private RepositoryScope(string[] aliases) => _aliases = aliases;

    /// <summary>Belonging to no repository in particular.</summary>
    public static RepositoryScope Unfiled { get; } = new([]);

    public IReadOnlyList<string> Aliases => _aliases;

    public bool IsUnfiled => _aliases.Length == 0;

    /// <summary>
    /// A scope over whatever aliases were given: trimmed, lower-cased, blanks
    /// dropped, duplicates collapsed, and sorted so equality does not depend on
    /// typing order.
    /// </summary>
    public static RepositoryScope Of(IEnumerable<string>? aliases)
    {
        if (aliases is null) return Unfiled;

        var normalized = aliases
            .Select(Normalize)
            .Where(alias => alias.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return normalized.Length == 0 ? Unfiled : new RepositoryScope(normalized);
    }

    /// <summary>Aliases are compared to what the GitHub settings store, which
    /// lower-cases them, so they are normalized the same way here. The rule is
    /// restated rather than shared because a domain module may not reference an
    /// adapter — and the string is the whole contract.</summary>
    public static string Normalize(string? alias) => (alias ?? string.Empty).Trim().ToLowerInvariant();

    public bool Includes(string? alias)
    {
        var normalized = Normalize(alias);
        return normalized.Length > 0 && _aliases.Contains(normalized, StringComparer.Ordinal);
    }

    public bool Equals(RepositoryScope? other) =>
        other is not null && _aliases.SequenceEqual(other._aliases, StringComparer.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as RepositoryScope);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var alias in _aliases) hash.Add(alias, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public override string ToString() => IsUnfiled ? "(unfiled)" : string.Join(", ", _aliases);
}

/// <summary>
/// A free-form row label within a repository band — "platform", "migration",
/// whatever the person actually calls it. Equality is by value; blank is
/// normalized to the default lane.
/// <para>
/// Deliberately the person's vocabulary rather than an enum, for the same reason a
/// Backlog Entry's area is: the taxonomy is theirs, and an enum here would mean a
/// release every time someone invents a workstream.
/// </para>
/// </summary>
public sealed record PlanningLane
{
    /// <summary>What the lane is called when nobody has said.</summary>
    public const string DefaultName = "Planned";

    private PlanningLane(string name) => Name = name;

    public static PlanningLane Default { get; } = new(DefaultName);

    public string Name { get; }

    public bool IsDefault => string.Equals(Name, DefaultName, StringComparison.Ordinal);

    public static PlanningLane Of(string? name) =>
        string.IsNullOrWhiteSpace(name) ? Default : new PlanningLane(name.Trim());

    public override string ToString() => Name;
}
