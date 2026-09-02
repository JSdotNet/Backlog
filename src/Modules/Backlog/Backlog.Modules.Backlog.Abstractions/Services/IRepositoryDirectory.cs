namespace Backlog.Modules.Backlog.Abstractions.Services;

/// <summary>One repository the registry knows about, in the terms Backlog
/// Management needs it in.</summary>
/// <param name="Alias">The short name a person types in a <c>`repo:`</c> token,
/// and the label every chip, picker and colour mark reads. A display and typing
/// label only — it is mutable by design, and it is no longer the value an
/// entry's <c>repo_ids</c> ends up holding; <see cref="BacklogRepositoryRef.Id"/>
/// is.</param>
/// <param name="Owner">The GitHub account or organisation the repository sits
/// under.</param>
/// <param name="Name">The repository's own name under that owner.</param>
public sealed record BacklogRepositoryRef(string Alias, string Owner, string Name)
{
    /// <summary>
    /// The <c>owner/name</c> identity an entry's <c>repo_ids</c> holds.
    /// <para>
    /// Computed rather than a fourth positional parameter, deliberately. A
    /// parameter would be a second source of truth for a value that owner and
    /// name already determine, and every construction site — the adapter over
    /// Settings, the fakes, a plan's registration — would have to be trusted to
    /// keep the two in step. Derived, it cannot drift.
    /// </para>
    /// <para>
    /// The alias used to be what an entry stored. Moving the stored identity
    /// here is what lets somebody rename an alias without every entry filed
    /// against that repository losing its target.
    /// </para>
    /// </summary>
    public string Id => $"{Owner}/{Name}";
}

/// <summary>
/// PORT — which repositories exist, and how a name a plan wrote becomes one of
/// them.
/// <para>
/// Backlog Management does not own the repository list and must not become a
/// second place it is configured, so it asks. The adapter over Settings answers;
/// a test answers with a fixed row or two. The same shape
/// <see cref="IRoadmapTagSource"/> takes, for the same reason.
/// </para>
/// <para>
/// <see cref="Register"/> is here rather than in Import because of the line ADR
/// 0004 draws: "Import triggers registration; it does not perform it." Import
/// asks for a repository to exist and gets an alias back; what a registered
/// repository holds, and how registration behaves, stays behind this port with
/// whoever owns the registry. That is also why the method returns the
/// registered reference instead of a bare confirmation — Import never learns
/// enough about a repository to construct one itself.
/// </para>
/// </summary>
public interface IRepositoryDirectory
{
    /// <summary>Every repository the registry currently knows, in the order it
    /// holds them. Empty when none is configured.</summary>
    IReadOnlyList<BacklogRepositoryRef> Repositories { get; }

    /// <summary>The repository a name refers to, or null when the registry has
    /// never seen it. Null is an ordinary answer and not a failure: outside
    /// Import an unrecognized <c>repo:</c> simply stays unresolved, which is the
    /// token's general rule.
    /// <para>
    /// Matched on shape, because a stored <c>repo_id</c> and a hand-typed
    /// <c>repo:</c> are the same input arriving in two spellings. A name
    /// containing a <c>/</c> is an <see cref="BacklogRepositoryRef.Id"/> and is
    /// matched against that, without regard to case — GitHub is
    /// case-preserving but not case-sensitive, so the registry's casing is the
    /// answer either way. Anything else is an alias and is matched exactly,
    /// because both sides have been through the same normalization.
    /// </para></summary>
    BacklogRepositoryRef? Resolve(string name);

    /// <summary>Registers a repository under the name a plan gave it and returns
    /// it, so a plan can introduce a repository to the product just by mentioning
    /// it. Idempotent: a name the registry already knows is answered with what it
    /// already has rather than added a second time, because a plan naming the
    /// same repository twice is one repository, not two.</summary>
    BacklogRepositoryRef Register(string name);
}
