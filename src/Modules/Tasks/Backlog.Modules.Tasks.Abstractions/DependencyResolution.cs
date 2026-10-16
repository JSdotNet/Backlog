namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>
/// What an <c>after:</c> value that is not a real id can still be resolved to:
/// the stored entry whose <c>id:</c> it names.
/// <para>
/// Import resolves <c>after:</c> against the <c>id:</c> tokens of the document it
/// is importing (ADR 0007), and that was the whole of it. A plan brought in over
/// two sittings — or one whose second half names a step the first half already
/// created — writes the local id through as if it were a real one, and from then
/// on nothing can tell what the entry is waiting for: the list shows the slug,
/// the picker cannot pick it, and a done predecessor keeps blocking because the
/// chain never finds the row it is done on. This is the rule that closes that
/// gap, and it is one rule shared by the write side (Import, before the value is
/// stored) and the read side (the pane, for values already stored that way), so
/// the two cannot disagree about which entry a slug means.
/// </para>
/// <para>
/// In Abstractions rather than beside the handler for the reason
/// <see cref="Attachment"/> is: the aggregate and the pane both have to ask it,
/// and neither references the other.
/// </para>
/// </summary>
public static class DependencyResolution
{
    /// <summary>One stored entry as the resolver sees it: its real id, the local
    /// <c>id:</c> it was imported under, and the plan that import belonged to.</summary>
    public readonly record struct Candidate(Guid Id, string? ImportItemId, string? ImportPlanId);

    /// <summary>
    /// The real id a dependency value resolves to, or the value unchanged.
    /// <para>
    /// A value that already is a candidate's real id is returned as written — a
    /// real id is never re-read as a slug, whatever a stored <c>id:</c> happens
    /// to say. Otherwise the candidates carrying the value as their
    /// <c>ImportItemId</c> are tried in a fixed order of confidence: the ones
    /// under <paramref name="planId"/> — the plan the asking entry belongs to —
    /// then the ones under any plan the asking entry is <paramref name="tagged"/>
    /// with, since a plan's id is its shared tag and an entry imported without a
    /// plan id still wears the tag that was meant to be one; then, only when
    /// exactly one candidate in the whole store carries the slug, that one. Two
    /// plans both owning a <c>review-plan</c> step and an asking entry under
    /// neither is left as written: guessing would chain the entry to another
    /// plan's work, and a slug on screen is the more honest failure.
    /// </para>
    /// </summary>
    /// <param name="value">The <c>after:</c> value exactly as written.</param>
    /// <param name="candidates">Every live entry the value may name.</param>
    /// <param name="planId">The plan the asking entry belongs to, or null.</param>
    /// <param name="tagged">The asking entry's tags — the plan ids it may belong
    /// to without saying so.</param>
    public static string Resolve(
        string value,
        IReadOnlyCollection<Candidate> candidates,
        string? planId,
        IReadOnlyList<string> tagged)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(tagged);

        return Resolve(value, new CandidateIndex(candidates), planId, tagged);
    }

    /// <summary>
    /// <see cref="Resolve(string, IReadOnlyCollection{Candidate}, string?, IReadOnlyList{string})"/>
    /// against an index built once over the same candidates — for a caller that
    /// asks about many values, or many rows, between two changes to the store.
    /// </summary>
    public static string Resolve(
        string value,
        CandidateIndex index,
        string? planId,
        IReadOnlyList<string> tagged)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(tagged);

        if (index.IsRealId(value)) return value;

        var named = index.Named(value);

        if (named.Count == 0) return value;

        // FirstOrDefault on a struct hands back a blank candidate, not null, so
        // each step asks for the id explicitly and reads "none" as no answer.
        Guid? underPlan = planId is null ? null : named
            .Where(candidate => string.Equals(candidate.ImportPlanId, planId, StringComparison.Ordinal))
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefault();
        if (underPlan is { } byPlan) return byPlan.ToString();

        Guid? underTag = named
            .Where(candidate => candidate.ImportPlanId is { } owner && tagged.Contains(owner, StringComparer.Ordinal))
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefault();
        if (underTag is { } byTag) return byTag.ToString();

        return named.Count == 1 ? named[0].Id.ToString() : value;
    }

    /// <summary>Resolves every value of a dependency list, keeping order and
    /// dropping a repeat that two different spellings collapsed into — a slug and
    /// the real id it means are one dependency, not two.</summary>
    public static IReadOnlyList<string> ResolveAll(
        IEnumerable<string> values,
        IReadOnlyCollection<Candidate> candidates,
        string? planId,
        IReadOnlyList<string> tagged)
    {
        ArgumentNullException.ThrowIfNull(values);

        // An empty list resolves to nothing without indexing the store: the import
        // asks once per entry it wrote, most of which wait on nothing.
        var listed = values as IReadOnlyCollection<string> ?? [.. values];
        if (listed.Count == 0) return [];

        ArgumentNullException.ThrowIfNull(candidates);

        return ResolveAll(listed, new CandidateIndex(candidates), planId, tagged);
    }

    /// <summary>
    /// <see cref="ResolveAll(IEnumerable{string}, IReadOnlyCollection{Candidate}, string?, IReadOnlyList{string})"/>
    /// against an index built once over the same candidates.
    /// </summary>
    public static IReadOnlyList<string> ResolveAll(
        IEnumerable<string> values,
        CandidateIndex index,
        string? planId,
        IReadOnlyList<string> tagged)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(index);

        var resolved = new List<string>();
        foreach (var value in values)
        {
            var id = Resolve(value, index, planId, tagged);
            if (!resolved.Contains(id, StringComparer.OrdinalIgnoreCase)) resolved.Add(id);
        }

        return resolved;
    }

    /// <summary>
    /// The candidates a resolver asks about, read once: which values are real
    /// ids, and which candidates carry each <c>id:</c> slug.
    /// <para>
    /// The real ids are compared case-insensitively, matching how the pane already
    /// compares a stored id against a typed one: a GUID is the same GUID in either
    /// case. They are held as the strings <see cref="Guid.ToString()"/> writes
    /// rather than parsed values, so a slug that merely parses as a GUID in some
    /// other format is still a slug. Each slug keeps its candidates in the order
    /// they were given, which is what makes "the first one under the plan" the
    /// same entry whichever overload asked.
    /// </para>
    /// <para>
    /// Never changed after it is built, so one index can be read from any thread;
    /// a change to the store is a new index.
    /// </para>
    /// </summary>
    public sealed class CandidateIndex
    {
        private readonly HashSet<string> _realIds;
        private readonly Dictionary<string, List<Candidate>> _bySlug = new(StringComparer.Ordinal);

        public CandidateIndex(IReadOnlyCollection<Candidate> candidates)
        {
            ArgumentNullException.ThrowIfNull(candidates);

            _realIds = new HashSet<string>(candidates.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates)
            {
                _realIds.Add(candidate.Id.ToString());

                if (candidate.ImportItemId is not { } slug) continue;
                if (!_bySlug.TryGetValue(slug, out var named)) _bySlug[slug] = named = [];
                named.Add(candidate);
            }
        }

        internal bool IsRealId(string value) => _realIds.Contains(value);

        internal IReadOnlyList<Candidate> Named(string slug) =>
            _bySlug.TryGetValue(slug, out var named) ? named : [];
    }
}
