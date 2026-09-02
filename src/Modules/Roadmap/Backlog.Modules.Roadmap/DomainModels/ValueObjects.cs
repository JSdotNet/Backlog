using System.Globalization;
using System.Text;

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

/// <summary>
/// The slug that names a roadmap item wherever tags are used — the backlog item tag
/// list, and the <c>roadmap:</c> field in a knowledge chapter's <c>meta</c> block.
/// Equality is by value.
/// <para>
/// Lowercase kebab-case, and never empty: it has to survive a trip through a class
/// name, a URL fragment and a hand-edited <c>meta</c> block, and every one of those
/// is happier with <c>[a-z0-9-]</c> than with whatever was typed into the title.
/// </para>
/// <para>
/// Derived from the title by default but held separately once it exists, which is the
/// whole reason it is a value on the item rather than a function of the title. A tag
/// that recomputed itself every time the title changed would silently break the
/// backlog entries and chapters already pointing at it — renaming a plan item is an
/// ordinary thing to do, and it must not quietly rewrite somebody else's link. So the
/// item derives a tag when it is first given none and keeps it thereafter; a rename
/// leaves it alone, and only an explicit edit moves it.
/// </para>
/// <para>
/// Not unique across items on purpose. Two items may deliberately share a tag to be
/// read as one group, so the plan surfaces where a tag is shared (see
/// <see cref="RoadmapPlan.TagsInUse"/>) rather than forbidding it.
/// </para>
/// </summary>
public sealed record PlanningTag
{
    /// <summary>What a tag falls back to when a title slugifies to nothing — a title
    /// of only punctuation or diacritics that strip away. Stable and deterministic so
    /// two such titles do not collide by accident and a reader always has something to
    /// point at rather than a dangling <c>roadmap:</c> with no value.</summary>
    public const string Fallback = "item";

    private PlanningTag(string value) => Value = value;

    public string Value { get; }

    /// <summary>Derives a tag from a title: diacritics folded away, lowercased, every
    /// run of anything that is not a letter or digit becoming a single hyphen, and the
    /// ends trimmed. An empty result becomes <see cref="Fallback"/>.</summary>
    public static PlanningTag From(string? title) => new(Slugify(title));

    /// <summary>Normalizes an explicit tag — one typed into the editor or read back
    /// from a stored plan — through the same slug rules, so a value hand-edited into
    /// <c>Feature X!</c> still lands as <c>feature-x</c> rather than as something no
    /// class name will accept.</summary>
    public static PlanningTag Of(string? value) => new(Slugify(value));

    /// <summary>
    /// Lowercase, fold diacritics, collapse every non-alphanumeric run to one hyphen,
    /// trim the ends. The one guarantee callers rely on: the result always matches
    /// <c>^[a-z0-9]+(-[a-z0-9]+)*$</c>, falling back rather than returning empty.
    /// </summary>
    private static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Fallback;

        // Decompose so an accented letter becomes its base plus a combining mark, then
        // drop the marks: "Café" folds to "cafe" rather than losing the whole word.
        var folded = value.Trim().Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(folded.Length);
        var pendingHyphen = false;

        foreach (var ch in folded)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(ch))
            {
                if (pendingHyphen && builder.Length > 0) builder.Append('-');
                pendingHyphen = false;
                builder.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                // A run of separators or punctuation becomes at most one hyphen, and
                // only once a real character has been seen — leading hyphens never form.
                pendingHyphen = true;
            }
        }

        var slug = builder.ToString();
        return slug.Length == 0 ? Fallback : slug;
    }

    public override string ToString() => Value;
}

/// <summary>
/// The knowledge chapters a roadmap item points at, in the knowledge base's own
/// reference form — <c>&lt;repo-relative-path&gt;</c> or
/// <c>&lt;repo-relative-path&gt;#&lt;heading-slug&gt;</c>. Equality is by value, and
/// order is part of it: the person's ordering is the reading order.
/// <para>
/// Held as opaque strings, trimmed and de-duplicated but never resolved, for the same
/// reason <see cref="RepositoryScope"/> holds aliases opaquely: a reference whose
/// target has moved or not been written yet is an ordinary transient state of a
/// hand-editable plan, exactly like a dangling <see cref="RoadmapItem.TaskId"/>
/// — a reading outcome, not a broken plan. Checking a path exists would tie this
/// context to the knowledge base's storage, which it deliberately does not know.
/// </para>
/// <para>
/// An empty set is valid and means the item points at no chapter. It is not an error.
/// </para>
/// </summary>
public sealed class KnowledgeReferences : IEquatable<KnowledgeReferences>
{
    private readonly string[] _refs;

    private KnowledgeReferences(string[] refs) => _refs = refs;

    /// <summary>Pointing at no chapter.</summary>
    public static KnowledgeReferences Empty { get; } = new([]);

    public IReadOnlyList<string> Refs => _refs;

    public bool IsEmpty => _refs.Length == 0;

    /// <summary>
    /// A set over whatever references were given: trimmed, blanks dropped, duplicates
    /// collapsed, and order preserved — unlike a repository scope, which sorts, because
    /// here the order the person wrote is the order they mean them to be read in.
    /// </summary>
    public static KnowledgeReferences Of(IEnumerable<string>? refs)
    {
        if (refs is null) return Empty;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<string>();

        foreach (var reference in refs)
        {
            var trimmed = (reference ?? string.Empty).Trim();
            if (trimmed.Length == 0) continue;
            if (seen.Add(trimmed)) kept.Add(trimmed);
        }

        return kept.Count == 0 ? Empty : new KnowledgeReferences([.. kept]);
    }

    public bool Equals(KnowledgeReferences? other) =>
        other is not null && _refs.SequenceEqual(other._refs, StringComparer.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as KnowledgeReferences);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var reference in _refs) hash.Add(reference, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public override string ToString() => IsEmpty ? "(none)" : string.Join(", ", _refs);
}
