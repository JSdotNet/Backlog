namespace Backlog.Modules.Roadmap.DomainModels;

/// <summary>
/// What one node in the plan is waiting for: a set of ids, each naming another
/// item or milestone in the same plan.
/// <para>
/// A first-class collection rather than a bare list on each entity, because the
/// same behaviour — a dependency declared twice is one dependency, removing one
/// that was never there is not an error — is needed identically by
/// <see cref="RoadmapItem"/> and <see cref="Milestone"/>. Those two share this
/// rather than a base class: they are both dependency endpoints, and that is the
/// only thing they have in common. A common ancestor would imply a shared
/// lifecycle they do not have.
/// </para>
/// <para>
/// Held on the waiting side rather than as a separate edge list on the plan,
/// because "what am I waiting for" is the question a reader asks of an item, and
/// the direction in which a plan is edited. Whether anything depends on <em>this</em>
/// is answered by the plan, which can see every node.
/// </para>
/// </summary>
public sealed class Dependencies
{
    private readonly List<Guid> _dependsOn;

    private Dependencies(List<Guid> dependsOn) => _dependsOn = dependsOn;

    public static Dependencies None() => new([]);

    public static Dependencies Of(IEnumerable<Guid>? dependsOn) =>
        new([.. (dependsOn ?? []).Where(id => id != Guid.Empty).Distinct()]);

    public IReadOnlyList<Guid> All => _dependsOn;

    public int Count => _dependsOn.Count;

    public bool Contains(Guid id) => _dependsOn.Contains(id);

    /// <summary>Returns whether this actually added anything, so a caller can tell
    /// a new dependency from one that was already recorded.</summary>
    internal bool Add(Guid id)
    {
        if (id == Guid.Empty || _dependsOn.Contains(id)) return false;

        _dependsOn.Add(id);
        return true;
    }

    internal bool Remove(Guid id) => _dependsOn.Remove(id);
}
