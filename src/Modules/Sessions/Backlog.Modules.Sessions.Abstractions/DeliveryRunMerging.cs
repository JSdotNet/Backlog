namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// One run, however many surfaces reported it.
/// <para>
/// A flow reports its run to every surface it is bound to, and each surface answers
/// <c>start_run</c> with an id of its own and files its own run under it. So one piece
/// of work is two files in two dashboards' folders — the same worktree, the same
/// skill, the same session, started within seconds of each other — and a reader that
/// took the folders at their word drew every run twice: two folds, two diagrams and,
/// where the surfaces' status words differ, two outcomes. The plugin is not changed to
/// mend that. The files already on disk are what the reader has, and a surface's id is
/// meant to mean nothing to another surface.
/// </para>
/// <para>
/// A pure function over the runs it is given, like <see cref="SessionRows"/>, and
/// applied by the reader before anything else sees the catalog, so every consumer
/// holds one run where the folders hold two.
/// </para>
/// <para>
/// <b>What makes two files one run.</b> All of: the same worktree key, compared
/// case-blind as <see cref="SessionRows"/> compares it; the same skill; different
/// dashboards — two files from one surface are two runs, whatever else they share;
/// both dated, and started within <see cref="Window"/> of each other; and session ids
/// that do not contradict — they share one, or neither names any. A pair naming
/// different sessions stays two runs. That is the conservative answer, and the one
/// such pair found on a real profile was two runs.
/// </para>
/// <para>
/// <b>Why five minutes.</b> A surface reattaches a run of the same skill in the same
/// worktree rather than opening a second, so one surface cannot hold two genuine runs
/// that close together, and the window has no true pair to refuse. What it prevents is
/// the other error: a run whose partner never arrived, because one surface was down,
/// pairing with the next run's file from the other surface an hour later. The widest
/// gap measured between a pair's two starts was 23 seconds — the two <c>start_run</c>
/// calls are separate tool calls.
/// </para>
/// <para>
/// <b>Which file is the record.</b> The first of <c>delivery-surface-dashboard</c>,
/// <c>orch-dashboard</c> and <c>backlog</c> to have reported it. The dashboard measures
/// the whole session's tool activity and token usage, the backlog surface its own
/// share, and a merged run shows one measurement rather than the sum of two that
/// overlap. The primary's id, dashboard, status word, stages and measured blocks are
/// the run's. A secondary fills only what the primary left blank — the change kind, a
/// start, a measured block the primary has none of — and adds the references it did
/// not name. Which surfaces reported the run is kept beside which one wrote the file:
/// provenance, not a second record.
/// </para>
/// </summary>
public static class DeliveryRunMerging
{
    /// <summary>How far apart two surfaces' starts of one run may be.</summary>
    public static TimeSpan Window { get; } = TimeSpan.FromMinutes(5);

    /// <summary>The dashboards in the order one is preferred as the record. A dashboard
    /// not listed ranks after every one that is, and alphabetically among its kind.</summary>
    private static readonly string[] Preference = ["delivery-surface-dashboard", "orch-dashboard", "backlog"];

    /// <summary>The runs with every pair of surfaces' reports folded into one, in the
    /// order given — a merged run sits where its record did.</summary>
    public static IReadOnlyList<DeliveryRun> Merge(IReadOnlyList<DeliveryRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        if (runs.Count < 2) return runs;

        var taken = new HashSet<DeliveryRun>(ReferenceEqualityComparer.Instance);
        var merged = new Dictionary<DeliveryRun, DeliveryRun>(ReferenceEqualityComparer.Instance);

        // Same worktree and same skill is the outer condition; the rest is decided
        // inside the group. Preference first, then the start, so the primary of a pair
        // is always the preferred surface's file, and one surface's own runs are
        // walked oldest first.
        var groups = runs
            .GroupBy(run => (Worktree: run.Worktree.ToUpperInvariant(), run.SkillId))
            .Select(group => group
                .OrderBy(run => Rank(run.Dashboard))
                .ThenBy(run => run.Dashboard, StringComparer.OrdinalIgnoreCase)
                .ThenBy(run => run.StartedAt ?? run.UpdatedAt)
                .ToList());

        foreach (var group in groups)
        {
            foreach (var primary in group)
            {
                if (!taken.Add(primary)) continue;

                // At most one partner per other dashboard: the one whose start is
                // nearest the primary's.
                var partners = group
                    .Where(candidate => !taken.Contains(candidate) && Pairs(primary, candidate))
                    .GroupBy(candidate => candidate.Dashboard, StringComparer.OrdinalIgnoreCase)
                    .Select(dashboard => dashboard.OrderBy(candidate => Gap(primary, candidate)).First())
                    .OrderBy(candidate => Rank(candidate.Dashboard))
                    .ThenBy(candidate => candidate.Dashboard, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var partner in partners) taken.Add(partner);

                merged[primary] = partners.Count == 0 ? primary : Fold(primary, partners);
            }
        }

        return [.. runs.Where(merged.ContainsKey).Select(run => merged[run])];
    }

    private static bool Pairs(DeliveryRun a, DeliveryRun b) =>
        !string.Equals(a.Dashboard, b.Dashboard, StringComparison.OrdinalIgnoreCase)
        && a.StartedAt is { } first
        && b.StartedAt is { } second
        && (first - second).Duration() <= Window
        && SessionsAgree(a, b);

    private static bool SessionsAgree(DeliveryRun a, DeliveryRun b) =>
        (a.SessionIds.Count == 0 && b.SessionIds.Count == 0)
        || a.SessionIds.Intersect(b.SessionIds, StringComparer.Ordinal).Any();

    private static TimeSpan Gap(DeliveryRun a, DeliveryRun b) =>
        ((a.StartedAt ?? a.UpdatedAt) - (b.StartedAt ?? b.UpdatedAt)).Duration();

    private static int Rank(string dashboard)
    {
        var index = Array.FindIndex(Preference, name => string.Equals(name, dashboard, StringComparison.OrdinalIgnoreCase));

        return index < 0 ? Preference.Length : index;
    }

    private static DeliveryRun Fold(DeliveryRun primary, IReadOnlyList<DeliveryRun> others) =>
        primary with
        {
            ChangeKind = string.IsNullOrWhiteSpace(primary.ChangeKind)
                ? others.Select(other => other.ChangeKind).FirstOrDefault(kind => !string.IsNullOrWhiteSpace(kind))
                : primary.ChangeKind,
            References = References(primary, others),
            StartedAt = primary.StartedAt ?? others.Select(other => other.StartedAt).FirstOrDefault(start => start is not null),
            UpdatedAt = others.Select(other => other.UpdatedAt).Append(primary.UpdatedAt).Max(),
            Stages = primary.Stages.Count > 0 ? primary.Stages : First(others, other => other.Stages),
            TokenUsage = primary.TokenUsage ?? others.Select(other => other.TokenUsage).FirstOrDefault(usage => usage is not null),
            Context = primary.Context ?? others.Select(other => other.Context).FirstOrDefault(context => context is not null),
            InsightsByCategory = primary.InsightsByCategory.Count > 0 ? primary.InsightsByCategory : First(others, other => other.InsightsByCategory),
            InsightsByServer = primary.InsightsByServer.Count > 0 ? primary.InsightsByServer : First(others, other => other.InsightsByServer),
            SessionIds = [.. primary.SessionIds.Concat(others.SelectMany(other => other.SessionIds)).Distinct(StringComparer.Ordinal)],
            Surfaces = [primary.Dashboard, .. others.Select(other => other.Dashboard)]
        };

    private static IReadOnlyList<T> First<T>(IEnumerable<DeliveryRun> others, Func<DeliveryRun, IReadOnlyList<T>> of) =>
        others.Select(of).FirstOrDefault(list => list.Count > 0) ?? [];

    /// <summary>The primary's references, then every one of the others' it did not
    /// already name — the same item once, whichever file recorded it.</summary>
    private static IReadOnlyList<DeliveryRunReference> References(DeliveryRun primary, IEnumerable<DeliveryRun> others)
    {
        var references = new List<DeliveryRunReference>(primary.References);
        var seen = new HashSet<string>(primary.References.Select(KeyOf), StringComparer.OrdinalIgnoreCase);

        foreach (var reference in others.SelectMany(other => other.References))
        {
            if (seen.Add(KeyOf(reference))) references.Add(reference);
        }

        return references;
    }

    private static string KeyOf(DeliveryRunReference reference) =>
        $"{reference.Kind}|{reference.Url ?? reference.Label}";
}
