using Backlog.Modules.Backlog.Abstractions.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Backlog.Features.ReconcileRepositoryIds;

/// <summary>
/// Brings every entry's <c>repo_ids</c> up to the identity the registry states.
/// <para>
/// The alias used to be the stored value, so a workspace that predates this
/// change holds a mixture: aliases from the text path, <c>owner/name</c> from
/// every pushed entry, and whatever casing somebody typed. This is the one pass
/// that settles it, run at startup and after the workspace root moves.
/// </para>
/// <para>
/// No parameters, and no version column or row-rewrite machinery anywhere behind
/// it. The pass is idempotent by construction, so there is nothing for a version
/// to gate — and adding one would be the first in this repository and would
/// contradict ADR 0003's deliberate absence of migration machinery. There is no
/// markdown rewrite either: <c>content_md</c> holds only the body and the
/// metadata line is composed from these fields, so migrating the field migrates
/// the canonical text, the raw-markdown escape hatch and every chip in one write.
/// </para>
/// </summary>
public sealed record ReconcileRepositoryIdsCommand();

/// <summary>
/// Runs the pass and answers how many entries it changed.
/// <para>
/// A count rather than nothing, because the caller is a startup path with no
/// screen of its own: the number is the only way a test — or a log line — can
/// tell "there was nothing to do" from "it did not run".
/// </para>
/// </summary>
public sealed class ReconcileRepositoryIdsCommandHandler(ITaskRepository entries, IRepositoryDirectory repositories)
    : ICommandHandler<ReconcileRepositoryIdsCommand, Result<int>>
{
    public async Task<Result<int>> Handle(
        ReconcileRepositoryIdsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Snapshotted, because the loop below saves into the same store it is
        // reading from. Every real adapter materialises its answer, but a use case
        // that only works against the ones that do is a use case waiting to break.
        var stored = (await entries.ListAsync(cancellationToken)).ToList();

        // One memo for the whole pass, keyed on the stored value exactly as it is
        // written. A workspace where forty entries name one repository is one
        // question about one repository, and asking forty times is how an
        // id-shaped value nothing recognises would get registered forty times.
        var canonical = new Dictionary<string, string>(StringComparer.Ordinal);
        var changed = 0;

        foreach (var entry in stored)
        {
            var reconciled = Reconcile(entry.RepoIds, canonical);

            // Written only when something actually moved. A second run over a
            // reconciled workspace is a pure read, which is what makes the pass
            // safe to put on every start rather than behind a once-flag.
            if (reconciled.SequenceEqual(entry.RepoIds, StringComparer.Ordinal)) continue;

            entry.SetRepoIds(reconciled);
            await entries.SaveAsync(entry, cancellationToken);
            changed++;
        }

        return changed;
    }

    /// <summary>De-duplicates after canonicalising, for the reason the resolver
    /// does: before the registry has spoken, two casings of one repository are
    /// genuinely two strings.</summary>
    private List<string> Reconcile(IReadOnlyList<string> stored, Dictionary<string, string> canonical) =>
    [
        .. stored
            .Select(value => Memoized(value, canonical))
            .Distinct(StringComparer.Ordinal)
    ];

    private string Memoized(string value, Dictionary<string, string> canonical)
    {
        if (canonical.TryGetValue(value, out var already)) return already;

        var answer = Canonical(value);
        canonical[value] = answer;
        return answer;
    }

    /// <summary>
    /// One stored value, reconciled.
    /// <para>
    /// Rule 1 covers alias-to-id and casing in one step, because resolution
    /// answers with the registry's own spelling either way. Rule 2 is part 3 of
    /// the approved scope: an id the registry does not know is auto-registered as
    /// a directory-less entry, so an assignment that arrived with a synced
    /// <c>backlog.db</c> becomes an ordinary configured repository rather than an
    /// unresolvable string. Rule 3 leaves everything else exactly as it is.
    /// </para>
    /// <para>
    /// The shape guard on rule 2 is where ADR 0004's line now sits, and it is
    /// deliberately narrow. A typo'd <c>repo:xyz</c> is alias-shaped, resolves to
    /// nothing, and is never registered — the guard that keeps the ADR's sentence
    /// true is that the ordinary text save never registers at all, so the token
    /// itself does not introduce a repository. What is left is a hand-typed
    /// <c>repo:foo/bar</c> naming a repository nobody has: once stored it is
    /// indistinguishable from an assignment that arrived from another install, and
    /// it is picked up by the next session rather than by the keystroke.
    /// </para>
    /// <para>
    /// This does not go through <c>RepositoryIdResolver</c>, and the difference is
    /// the point: that shared rule either registers everything unrecognised
    /// (Import) or nothing (the text save), and reconciliation needs a third
    /// answer that depends on the value's shape.
    /// </para>
    /// </summary>
    private string Canonical(string value)
    {
        if (repositories.Resolve(value) is { } known) return known.Id;

        return IsIdShaped(value) ? repositories.Register(value).Id : value;
    }

    /// <summary>Whether a value is an <c>owner/name</c> coordinate: exactly two
    /// non-empty parts either side of a single <c>/</c>. The same test the
    /// registry applies when it reads a row, so a value this pass registers is a
    /// value the registry will keep.</summary>
    private static bool IsIdShaped(string value)
    {
        var parts = value.Split('/');
        return parts.Length == 2 && parts.All(part => part.Trim().Length > 0);
    }
}
