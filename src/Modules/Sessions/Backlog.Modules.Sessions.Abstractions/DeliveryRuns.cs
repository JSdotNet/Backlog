using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// One orchestrated delivery run — a <c>flow-*</c> or <c>orch-*</c> skill driven
/// through a dashboard — as the dashboard's own run file records it.
/// <para>
/// A <em>delivery</em> run, with the adjective, because this context already has a
/// run: <see cref="AgentActivityRun"/> is a stretch of transcript in which an agent was
/// producing, derived from the transcript by this product. This one is the other way
/// round — a record another tool wrote about a piece of work it was tracking, with a
/// title, stages and a status the tool assigned. The two share a word and nothing
/// else, so the word alone is not allowed to name either.
/// </para>
/// <para>
/// Every value is carried as the file states it. Statuses are strings rather than an
/// enum because two dashboard generations wrote seven spellings between them —
/// <c>done</c> and <c>completed</c>, <c>in_progress</c>, <c>blocked</c>, <c>parked</c>
/// — and a reader mapping them to five members would be deciding what a writer meant
/// rather than reporting what it wrote; a surface that needs a chip normalises at the
/// last moment, where a new spelling degrades to a plain label rather than to a
/// dropped row.
/// </para>
/// </summary>
/// <param name="Id">The dashboard's own run id, unique within a worktree's folder.</param>
/// <param name="Dashboard">Which dashboard wrote it — the folder name under the
/// profile: <c>orch-dashboard</c> or <c>delivery-surface-dashboard</c> for a run
/// imported from one of those servers, <c>backlog</c> for one this product recorded
/// itself through <see cref="IDeliverySurfaceLifecycle"/>. The shape is the same
/// either way; this is the only thing that tells them apart, and it is provenance
/// rather than a difference in kind.</param>
/// <param name="Worktree">
/// The dashboard's key for the worktree the run belonged to: the folder's leaf plus
/// eight hex characters of a hash of the whole path, exactly as the state folder is
/// named — see <see cref="DeliveryRunWorktrees"/>, which derives the same key from a
/// session's folder so the two can be matched. The path itself is not recoverable
/// from the key, so <see cref="WorktreeName"/> is what to show.
/// </param>
/// <param name="WorktreeName">The worktree's leaf, which for this product is the name
/// a session row also shows as its title.</param>
/// <param name="EnvironmentId">ADR 0005's machine id, stamped by the source: a run
/// file found here was written here, the same way a transcript was.</param>
/// <param name="Environment">What that machine is called.</param>
/// <param name="SkillId">The skill that owned the run.</param>
/// <param name="Title">What the run was called.</param>
/// <param name="Status">The dashboard's status word, verbatim.</param>
/// <param name="ChangeKind">What kind of change the run decided it was making, where
/// it recorded one.</param>
/// <param name="References">The work this run is linked to, in the order a reader
/// asks: what it was for, then what it produced. Deduplicated, and empty where the
/// file recorded nothing.</param>
/// <param name="StartedAt">Null where the file does not date its own start.</param>
/// <param name="UpdatedAt">Always known — at worst the file's own timestamp.</param>
/// <param name="Stages">In the order the dashboard tracked them.</param>
/// <param name="TokenUsage">Null where the file carries none: an older generation of
/// the dashboard wrote no usage at all.</param>
/// <param name="Context">The owner session's context gauge, where sampled.</param>
/// <param name="InsightsByCategory">Tool activity summed per category, largest first.</param>
/// <param name="InsightsByServer">The MCP-tool share of the same activity summed per
/// server, largest first; a built-in tool has no server and is not in here.</param>
public sealed record DeliveryRun(
    string Id,
    string Dashboard,
    string Worktree,
    string WorktreeName,
    string EnvironmentId,
    string Environment,
    string SkillId,
    string Title,
    string Status,
    string? ChangeKind,
    IReadOnlyList<DeliveryRunReference> References,
    DateTimeOffset? StartedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<DeliveryRunStage> Stages,
    DeliveryRunTokenUsage? TokenUsage,
    DeliveryRunContext? Context,
    IReadOnlyList<DeliveryRunInsightGroup> InsightsByCategory,
    IReadOnlyList<DeliveryRunInsightGroup> InsightsByServer)
{
    /// <summary>Whether the dashboard still considered this run under way when it last
    /// wrote the file. The one status question a surface asks often enough to deserve
    /// a single spelling here rather than a string comparison in every caller.</summary>
    public bool InProgress => string.Equals(Status, "in_progress", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The agent sessions that drove the run, as the run file's <c>sessionIds</c> names
    /// them — the one that started it and any that picked it up — or empty where the
    /// writer did not record any: every dashboard before the surface took a session id.
    /// Where it names one, the run joins that session by identity; where it names none,
    /// by worktree and overlapping time.
    /// </summary>
    public IReadOnlyList<string> SessionIds { get; init; } = [];
}

/// <summary>What a run is linked to.</summary>
public enum DeliveryRunReferenceKind
{
    /// <summary>A Backlog entry — this product's own work item, named by the plan
    /// item marker the run was started from.</summary>
    Task,

    /// <summary>A tracker issue the run was working.</summary>
    Issue,

    /// <summary>A pull request the run opened or updated.</summary>
    PullRequest
}

/// <summary>
/// One piece of work a run is linked to: the item it was for, or the pull request it
/// produced.
/// <para>
/// A list rather than the single tracker item the dashboard's own field holds, because
/// a run has more than one link and they are not the same link: it is started from an
/// issue or a Backlog entry and it ends in a pull request, and a reader looking at the
/// row wants to reach either. The issue comes from the run's own <c>githubIssue</c>
/// field, the pull requests from the links its stages recorded, and the Backlog entry
/// from the plan item marker in the prompt the run was started with.
/// </para>
/// <para>
/// <see cref="Url"/> is null for a reference this machine cannot address — a Backlog
/// entry is in this product rather than on a web page, and a surface renders it as
/// text or wires its own way of opening it. Nothing here invents an address.
/// </para>
/// </summary>
/// <param name="Kind">Which sort of work.</param>
/// <param name="Label">What to show: <c>#128</c> for an issue, <c>PR #74</c> for a
/// pull request, the entry's id for a Backlog task.</param>
/// <param name="Title">The item's own title where the file recorded one.</param>
/// <param name="Url">Where it lives, for the references that live somewhere.</param>
/// <param name="Repository">
/// <c>owner/name</c> where the reference names one. This is the one place a run can
/// say which repository it was working in: the run file itself names none.
/// </param>
/// <param name="Plan">
/// The plan a <see cref="DeliveryRunReferenceKind.Task"/> belongs to, and null for
/// every other kind. Carried beside the label rather than folded into it because the
/// two together are what identifies a Backlog entry across plan versions — the app
/// matches on the pair — and a surface that can open one needs both.
/// </param>
public sealed record DeliveryRunReference(
    DeliveryRunReferenceKind Kind,
    string Label,
    string? Title,
    string? Url,
    string? Repository,
    string? Plan = null);

/// <summary>
/// One stage of a run.
/// </summary>
/// <param name="Name">The dashboard's stage name, or a positional stand-in when the
/// file named none — an older generation stored stages nameless.</param>
/// <param name="Status">Verbatim, for the reason <see cref="DeliveryRun.Status"/> is.</param>
/// <param name="DurationMs">Wall time from the stage's start to its completion, where
/// both were stamped.</param>
/// <param name="DoneCount">How many times the stage was marked done. More than one is
/// a stage that was re-entered — a build that ran three times before it passed.</param>
public sealed record DeliveryRunStage(string Name, string Status, long? DurationMs, int DoneCount);

/// <summary>Model calls and the tokens they moved, as one bucket of a run's usage.</summary>
public sealed record DeliveryRunTokens(
    long ModelCalls,
    long InputTokens,
    long OutputTokens,
    long ReasoningTokens,
    long CacheReadTokens,
    long CacheWriteTokens);

/// <summary>One stage's share of a run's usage.</summary>
public sealed record DeliveryRunStageTokens(string StageName, DeliveryRunTokens Total, DeliveryRunTokens SubAgent);

/// <summary>
/// What a run cost in tokens.
/// </summary>
/// <param name="Total">The whole run, owner session and sub-agents together.</param>
/// <param name="SubAgent">The part of <paramref name="Total"/> that delegated agents
/// spent. A part, not a second total: the owner's own share is the difference.</param>
/// <param name="ByStage">In stage order, for the stages that recorded any.</param>
/// <param name="Models">Every model the run was observed to call.</param>
public sealed record DeliveryRunTokenUsage(
    DeliveryRunTokens Total,
    DeliveryRunTokens SubAgent,
    IReadOnlyList<DeliveryRunStageTokens> ByStage,
    IReadOnlyList<string> Models);

/// <summary>
/// The owner session's context gauge: how full its window was when last sampled,
/// and the fullest it got.
/// </summary>
public sealed record DeliveryRunContext(long CurrentTokens, long TokenLimit, long PeakTokens)
{
    /// <summary>The peak as a fraction of the limit, or null when the limit is not a
    /// number a fraction can be taken of. A gauge that reported 0 of 0 is not empty,
    /// it is unsampled.</summary>
    public decimal? PeakShare => TokenLimit > 0 ? (decimal)PeakTokens / TokenLimit : null;
}

/// <summary>
/// One slice of a run's tool activity: how many calls, how long they took together,
/// and how many failed.
/// </summary>
public sealed record DeliveryRunInsightGroup(string Name, int Count, long DurationMs, int Failed);

/// <summary>
/// What a source found and what it could not read — the same two lists
/// <see cref="AgentSessionCatalog"/> carries, for the same reason: one dashboard's
/// folder being unreadable is not a reason to blank the other's runs, and the surface
/// should be able to say which half is missing.
/// </summary>
/// <param name="Runs">Every run every readable folder holds. No cap, unlike sessions:
/// a run is a piece of work somebody started on purpose, there are a few hundred
/// rather than a few thousand, and most of them join rows the list already has rather
/// than adding rows.</param>
/// <param name="Unreadable">The dashboards whose folder could not be read, by name.</param>
public sealed record DeliveryRunCatalog(IReadOnlyList<DeliveryRun> Runs, IReadOnlyList<string> Unreadable)
{
    public static DeliveryRunCatalog Empty { get; } = new([], []);
}

/// <summary>
/// Where the delivery runs on a machine come from. A second port beside
/// <see cref="IAgentSessionSource"/> rather than a widening of it: a host that wants
/// the session list is not thereby asking for every run file in the profile to be
/// parsed, and the two are read from different folders by different code.
/// </summary>
public interface IDeliveryRunSource
{
    Task<DeliveryRunCatalog> GetRunsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The dashboards' key for a worktree, derived from its path the way the dashboards
/// derive it: the folder's leaf with anything outside <c>[A-Za-z0-9._-]</c> replaced
/// by a hyphen, then a hyphen and the first eight hex characters of the SHA-1 of the
/// lower-cased full path. Both dashboard servers compute exactly this from the git
/// top level of wherever they were started, and it is the only bridge between a run
/// file and the folder it was about — the hash is one-way, so the bridge can be
/// crossed from the session's side only.
/// </summary>
public static partial class DeliveryRunWorktrees
{
    /// <summary>The key for one folder, or null for a blank path — a session whose
    /// folder did not travel has nothing to be keyed on.</summary>
    public static string? KeyOf(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;

        var root = folder.TrimEnd('\\', '/');

        if (root.Length == 0) return null;

        var leaf = root[(Math.Max(root.LastIndexOf('\\'), root.LastIndexOf('/')) + 1)..];
        var slug = Unsafe().Replace(leaf, "-");

        if (slug.Length == 0) slug = "project";

        var hash = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(root.ToLowerInvariant())))[..8];

        return $"{slug}-{hash}";
    }

    /// <summary>
    /// The keys of a folder and of every folder above it, nearest first. A session's
    /// folder is where the agent was started, and that is not always the worktree's
    /// top level — a session opened in <c>src/</c> ran in the worktree above it, and
    /// the dashboard keyed its runs on that. The caller takes the first key it knows.
    /// </summary>
    public static IEnumerable<string> KeysUpFrom(string? folder)
    {
        var current = folder;

        while (KeyOf(current) is { } key)
        {
            yield return key;

            var root = current!.TrimEnd('\\', '/');
            var cut = Math.Max(root.LastIndexOf('\\'), root.LastIndexOf('/'));

            if (cut <= 0) yield break;

            current = root[..cut];
        }
    }

    [GeneratedRegex("[^a-zA-Z0-9._-]")]
    private static partial Regex Unsafe();
}

/// <summary>
/// One row of the session list: a session, the delivery runs it drove, or a run the
/// list holds no session for — <b>one list, whichever side arrived first.</b>
/// <para>
/// The row is the unit the list shows, and the session is not, because the two
/// sources describe the same piece of work from different sides and neither is
/// complete without the other. A session record says who was working where and for
/// how long; a run file says what the work was and what it cost. A row that had to be
/// a session would drop every run whose session aged out of the reading, and a list
/// that kept those as a second table would make the reader learn which table to look
/// in — which is the question the row answers for them.
/// </para>
/// <para>
/// Every fact a surface reads off a row comes from whichever source has it, and the
/// row says nothing either source did not. A run-only row's title is the worktree's
/// name, because that is what the dashboard recorded; its state is Finished, because
/// there is no liveness evidence for it — a session still running would be in the
/// reading and the run would have joined it. A session row's repository is the
/// session's, and where the agent recorded none, the tracker item a run of it named:
/// a recorded fact from a second source, which is not the guess from a path leaf the
/// Session Log forbids.
/// </para>
/// </summary>
/// <param name="Session">The session, or null for a row the runs alone account for.</param>
/// <param name="Runs">The runs, most recently updated first. Exactly one on a row with
/// no session; any number on a session's row.</param>
public sealed record SessionRow(AgentSession? Session, IReadOnlyList<DeliveryRun> Runs)
{
    /// <summary>A row for a session with no runs on it.</summary>
    public static SessionRow Of(AgentSession session) => new(session, []);

    private DeliveryRun First => Runs[0];

    /// <summary>The identifier a surface keys the row on: the session's, or the run's
    /// under a prefix that keeps a run id and a session id from ever colliding.</summary>
    public string Key => Session is { } session
        ? $"{session.Kind}/{session.Id}"
        : $"run/{First.Dashboard}/{First.Worktree}/{First.Id}";

    /// <summary>The session's id, or the run's. What tests and groupings read.</summary>
    public string Id => Session?.Id ?? First.Id;

    /// <summary>Which agent. A run with no session is Claude's: both dashboards are
    /// Claude Code plugins, and the hooks that fed the file were Claude's.</summary>
    public AgentSessionKind Kind => Session?.Kind ?? AgentSessionKind.Claude;

    public string EnvironmentId => Session?.EnvironmentId ?? First.EnvironmentId;

    public string Environment => Session?.Environment ?? First.Environment;

    public string Title => Session?.Title ?? First.WorktreeName;

    /// <summary>The session's folder, where there is a session. A run knows only its
    /// worktree key, which is not a path anyone can open.</summary>
    public string WorkingFolder => Session?.WorkingFolder ?? string.Empty;

    /// <summary>
    /// Every repository this row could be said to be about, in confidence order:
    /// what the agent recorded, what this product placed the session's folder under,
    /// and the repository a run of it named a tracker item in. Ordered, because the
    /// first is the one to show; enumerated, because a narrowing matches on any of
    /// them — a row is about one repository however the product came to know which.
    /// </summary>
    public IEnumerable<string?> Repositories
    {
        get
        {
            if (Session is { } session)
            {
                yield return session.Repository;
                yield return session.ResolvedRepository;
            }

            foreach (var reference in Runs.SelectMany(run => run.References))
            {
                yield return reference.Repository;
            }
        }
    }

    public string? Repository =>
        Repositories.FirstOrDefault(repository => !string.IsNullOrWhiteSpace(repository));

    /// <summary>Whether the repository shown was placed by this product rather than
    /// recorded by the agent — the case the cell's hint and title exist for. A
    /// repository a run's tracker item named is recorded too, by the dashboard, so
    /// only the session's own resolution counts here.</summary>
    public bool RepositoryResolved =>
        Session is { } session
        && string.IsNullOrWhiteSpace(session.Repository)
        && !string.IsNullOrWhiteSpace(session.ResolvedRepository);

    public string? Branch => Session?.Branch;

    /// <summary>
    /// The pull requests the session linked itself to that no run on this row already
    /// names — one pull request is one reference on screen, and a run's line under the
    /// row is where a run's own pull request is drawn. Matched on the URL, or on the
    /// repository and the <c>PR #n</c> label a run writes where it carried no URL. Empty
    /// both where the session linked none and where it could not say: neither has
    /// anything to draw.
    /// </summary>
    public IReadOnlyList<AgentPullRequest> PullRequests
    {
        get
        {
            if (Session?.PullRequests is not { Count: > 0 } linked) return [];

            var named = Runs
                .SelectMany(run => run.References)
                .Where(reference => reference.Kind is DeliveryRunReferenceKind.PullRequest)
                .ToList();

            bool NamedByRun(AgentPullRequest pr) => named.Any(reference =>
                (!string.IsNullOrWhiteSpace(reference.Url) && string.Equals(reference.Url.Trim(), pr.Url.Trim(), StringComparison.OrdinalIgnoreCase))
                || (string.Equals(reference.Repository, pr.Repository, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(reference.Label, $"PR #{pr.Number}", StringComparison.Ordinal)));

            return
            [
                .. linked
                    .Where(pr => !NamedByRun(pr))
                    .DistinctBy(pr => pr.Url.Trim(), StringComparer.OrdinalIgnoreCase)
            ];
        }
    }

    /// <summary>
    /// What a run's line on this row draws: the run's own references, and on the most
    /// recently updated run, the <see cref="PullRequests"/> no run named. Those are still
    /// the work the row's run delivered — the run just never wrote the link down, which
    /// is what happens when a host's own Create PR button takes over from a flow — and
    /// the run's line, under the State column, is where a reader looks for a delivered
    /// pull request. One run and not every run, so a pull request stays one reference
    /// on screen.
    /// </summary>
    public IReadOnlyList<DeliveryRunReference> ReferencesFor(DeliveryRun run)
    {
        if (Runs.Count == 0 || !ReferenceEquals(run, First)) return run.References;

        var unnamed = PullRequests;

        return unnamed.Count == 0
            ? run.References
            :
            [
                .. run.References,
                .. unnamed.Select(pr => new DeliveryRunReference(
                    DeliveryRunReferenceKind.PullRequest,
                    $"PR #{pr.Number}",
                    Title: null,
                    pr.Url,
                    string.IsNullOrWhiteSpace(pr.Repository) ? null : pr.Repository))
            ];
    }

    public DateTimeOffset? StartedAt => Session is { } session ? session.StartedAt : First.StartedAt;

    public DateTimeOffset LastActivityAt => Session?.LastActivityAt ?? First.UpdatedAt;

    /// <summary>The session's state; Finished for a row with no session, for the reason
    /// the type's summary gives.</summary>
    public AgentSessionState State => Session?.State ?? AgentSessionState.Finished;

    /// <summary>Local for a run-only row: a run file is read off this machine's own
    /// disk, and nothing replicates one.</summary>
    public AgentSessionOrigin Origin => Session?.Origin ?? AgentSessionOrigin.Local;

    /// <summary>Whether the row is a run the list holds no session for.</summary>
    public bool RunOnly => Session is null;
}

/// <summary>
/// Building the list: which run belongs on which session's row, and which runs are
/// rows of their own. A pure function over what it is given, like
/// <see cref="AgentSessionGroups"/>, so the rule can be tested without a profile
/// underneath it.
/// <para>
/// <b>The worktree is the key.</b> A run file is filed under the dashboard's key for
/// the worktree it ran in, and a session records the folder it ran in; the same key
/// derived from that folder — or from a folder above it, since a session may be started
/// below the worktree's top level — is what says the two were in the same place. No
/// session id is involved, because no run file carries one.
/// </para>
/// <para>
/// <b>The window is the tiebreak, and also a condition.</b> A worktree is worked in by
/// several sessions over its life — the plan that asks for a change, the session that
/// makes it, the one that resumes it after a handoff — so a place alone would put a
/// week-old run on today's session in the same checkout. A run joins a session that
/// was active while the run was open; of several, the one whose start is nearest the
/// run's own, which is the session that opened it. A run whose file does not date its
/// start is placed by its last update, the one moment it is known to have existed.
/// </para>
/// <para>
/// A run that matches nothing is a row of its own, never dropped: its session may be
/// older than the reading keeps, or read on another environment without its files.
/// The row says so, and shows what the run recorded.
/// </para>
/// </summary>
public static class SessionRows
{
    public static IReadOnlyList<SessionRow> Of(IReadOnlyList<AgentSession> sessions, IReadOnlyList<DeliveryRun> runs)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(runs);

        if (runs.Count == 0) return [.. sessions.Select(SessionRow.Of)];

        // A run that names the sessions that drove it joins the first of them the list
        // holds, and no guess is made for it.
        var byId = sessions
            .GroupBy(session => session.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        // Case-insensitively, because the two sides spell the leaf differently on
        // purpose: the dashboard slugs git's own casing of the top level, and a
        // session records the folder as the agent was launched in it — which on
        // Windows may be the same folder in a different case. The hash half is
        // already case-blind, since both sides lower-case the path before hashing.
        var worktrees = runs.Select(run => run.Worktree).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Each session's worktree key: the first key up its folder that any run was
        // filed under. Computed once per session, not once per run per session — a
        // hash per ancestor per pair is the version of this that is too slow to ship.
        var bySession = sessions
            .Select(session => (Session: session, Worktree: KeysOf(session).FirstOrDefault(worktrees.Contains)))
            .Where(pair => pair.Worktree is not null)
            .GroupBy(pair => pair.Worktree!, pair => pair.Session, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var attached = new Dictionary<AgentSession, List<DeliveryRun>>(ReferenceEqualityComparer.Instance);
        var alone = new List<SessionRow>();

        foreach (var run in runs.OrderByDescending(run => run.UpdatedAt))
        {
            var owner = run.SessionIds.Select(id => byId.GetValueOrDefault(id)).FirstOrDefault(session => session is not null)
                ?? (run.SessionIds.Count == 0 && bySession.TryGetValue(run.Worktree, out var candidates)
                    ? candidates.Where(session => Overlaps(session, run)).OrderBy(session => Distance(session, run)).FirstOrDefault()
                    : null);

            if (owner is null)
            {
                alone.Add(new SessionRow(null, [run]));

                continue;
            }

            if (!attached.TryGetValue(owner, out var list))
            {
                attached[owner] = list = [];
            }

            list.Add(run);
        }

        return
        [
            .. sessions.Select(session => new SessionRow(session, attached.TryGetValue(session, out var list) ? list : [])),
            .. alone
        ];
    }

    /// <summary>The keys a session can be matched on: every folder up from its own
    /// where it has one, and otherwise the one key its record carried.</summary>
    private static IEnumerable<string> KeysOf(AgentSession session) =>
        !string.IsNullOrWhiteSpace(session.WorkingFolder) ? DeliveryRunWorktrees.KeysUpFrom(session.WorkingFolder)
            : session.WorktreeKey is { } key ? [key]
            : [];

    /// <summary>Whether the session was active at any point while the run was open.
    /// A session with no recorded start is taken to have started when it was last
    /// active, which is the narrowest honest window rather than an open-ended one.</summary>
    private static bool Overlaps(AgentSession session, DeliveryRun run)
    {
        var sessionStart = session.StartedAt ?? session.LastActivityAt;
        var runStart = run.StartedAt ?? run.UpdatedAt;

        return sessionStart <= run.UpdatedAt && session.LastActivityAt >= runStart;
    }

    private static TimeSpan Distance(AgentSession session, DeliveryRun run) =>
        ((session.StartedAt ?? session.LastActivityAt) - (run.StartedAt ?? run.UpdatedAt)).Duration();
}
