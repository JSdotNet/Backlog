using Backlog.Desktop.UI.PullRequests;
using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Backlog.UI.Components.Feedback;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The Backlog entries the Sessions context's two lists name, answered from one
/// window's own task list (local ADR 0043 §3, §5): the entry that linked a session,
/// the entry a pull request belongs to, and opening an entry — a jump to the Tasks
/// surface through the window's navigator, so it lands in the window it was pressed in.
/// <para>
/// Here rather than in either list because the records are on the entries, which
/// belong to Tasks, and the sessions and pull requests are another context's. It
/// moved out of <c>Home</c> when those lists became surfaces a window of their own can
/// show. One per window, over that window's <see cref="TasksDesktopState"/>.
/// </para>
/// </summary>
public sealed class ShellTaskReferences : IDisposable
{
    /// <summary>The test id of the toast for an entry this workspace does not hold.</summary>
    public const string PlanEntryMissingTestId = "sessions-plan-entry-missing";

    private readonly TasksDesktopState _state;
    private readonly ISurfaceNavigator _navigator;
    private readonly IToastChannel? _toasts;

    /// <summary>The matcher last built, with the list it was built over. A reference,
    /// written whole: the state announces changes from whichever thread made them,
    /// and a reader drawing a cell must see either the old matcher or none.</summary>
    private volatile MatcherOverRows? _pullRequestMatcher;

    public ShellTaskReferences(TasksDesktopState state, ISurfaceNavigator navigator, IToastChannel? toasts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(navigator);

        _state = state;
        _navigator = navigator;
        _toasts = toasts;

        // A row's links, status or assignee can change in place, which the list's
        // identity would not show, so any change rebuilds the pull request matcher.
        _state.Changed += ForgetMatcher;
    }

    /// <summary>
    /// Opens a Backlog entry a run, a session or a pull request names: the Tasks view
    /// with that entry in its editor, in this window.
    /// <para>
    /// Matched first on the entry's stored id, when the reference has one — the line the
    /// app puts on every entry it copies names it, and so does an entry that linked the
    /// session — because nothing identifies an entry more surely. Then on the pair the
    /// import writes down — the plan and the item's id within it — because that is what
    /// identifies an entry across plan versions; the plan is stored with its sigil and
    /// the marker states the slug, so the comparison drops it. An entry this workspace
    /// does not hold is said out loud rather than silently doing nothing.
    /// </para>
    /// </summary>
    public Task OpenTaskAsync(DeliveryRunReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        // Named by id alone, the label is the entry's title rather than an item id,
        // so there is no plan item to fall back on.
        var byIdOnly = reference.EntryId is not null && reference.Plan is null;

        var row = reference.EntryId is { } entryId
            ? _state.Rows.FirstOrDefault(candidate => candidate.Id == entryId)
            : null;

        row ??= byIdOnly
            ? null
            : _state.Rows.FirstOrDefault(candidate =>
                string.Equals(candidate.PreviewImportItemId, reference.Label, StringComparison.OrdinalIgnoreCase)
                && InPlan(candidate, reference.Plan));

        if (row is not { Id: { } id })
        {
            _toasts?.Publish(ToastMessage.Warning(
                byIdOnly
                    ? $"The entry {reference.Label} is not in this workspace."
                    : $"No entry of plan {reference.Plan} with the id {reference.Label} is in this workspace.",
                PlanEntryMissingTestId));

            return Task.CompletedTask;
        }

        _navigator.Show(TasksSurfaceQuery.SurfaceId, TasksSurfaceQuery.ForEntry(id.ToString()));
        return Task.CompletedTask;
    }

    /// <summary>
    /// The entry that linked a session: the entry's own record of the session it was
    /// worked in, written by the Backlog server's <c>link_session</c>.
    /// </summary>
    public DeliveryRunReference? SessionTaskFor(string sessionId)
    {
        var row = _state.Rows.FirstOrDefault(candidate =>
            candidate.Id is not null
            && candidate.SessionLinks.Any(link => string.Equals(link.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)));

        return TaskReference(row);
    }

    /// <summary>
    /// The entry a pull request belongs to, found by <see cref="PullRequestTaskMatcher"/>
    /// in order of how sure each relation is: the entry's own <c>pull-request</c>
    /// projection, a GitHub issue a linked task follows that the pull request closes, a
    /// linked task's external key in its title or branch, and the entry that linked its
    /// session. With <paramref name="mineOnly"/> only my tasks are asked.
    /// </summary>
    public DeliveryRunReference? PullRequestTaskFor(PullRequestTaskSubject pull, bool mineOnly)
    {
        var (matcher, rows, _) = PullRequestMatcher;
        return matcher.Match(pull, mineOnly) is { } match && rows.TryGetValue(match.Candidate.EntryId, out var row)
            ? TaskReference(row)
            : null;
    }

    public void Dispose() => _state.Changed -= ForgetMatcher;

    private void ForgetMatcher() => _pullRequestMatcher = null;

    /// <summary>One matcher over every entry, built on first ask and again whenever
    /// the task list changed — not per row the pane draws, which asks once per cell.</summary>
    private MatcherOverRows PullRequestMatcher
    {
        get
        {
            var source = _state.Rows;
            if (_pullRequestMatcher is { } built && ReferenceEquals(built.Source, source)) return built;

            var rows = new Dictionary<Guid, EntryRow>();
            var candidates = new List<PullRequestTaskCandidate>();
            foreach (var row in source)
            {
                if (row.Id is not { } id || !rows.TryAdd(id, row)) continue;

                var sourceRef = row.SourceRef;
                candidates.Add(new PullRequestTaskCandidate(
                    id,
                    [.. row.PullRequestLinks.Select(link => new RecordedPullRequest(link.Repository, link.Number))],
                    sourceRef is null ? null : PullRequestTaskMatcher.GitHubIssueOf(sourceRef.ConnectorId, sourceRef.Target, sourceRef.DisplayKey, sourceRef.Url),
                    PullRequestTaskMatcher.ExternalKeysOf(sourceRef?.DisplayKey),
                    [.. row.SessionLinks.Select(link => link.SessionId)],
                    IsMine: row.PreviewStatus is not (EntryStatus.Done or EntryStatus.Archived)));
            }

            var matcher = new MatcherOverRows(new PullRequestTaskMatcher(candidates), rows, source);
            _pullRequestMatcher = matcher;
            return matcher;
        }
    }

    private sealed record MatcherOverRows(PullRequestTaskMatcher Matcher, Dictionary<Guid, EntryRow> Rows, List<EntryRow> Source);

    /// <summary>
    /// Whether an entry belongs to the plan a run names: the plan id Import stamps, or
    /// the plan tag an entry typed by hand carries. The sigil is trimmed on both sides,
    /// and a reference that names no plan matches on the item id alone.
    /// </summary>
    private static bool InPlan(EntryRow row, string? plan)
    {
        if (plan is null) return true;

        var wanted = plan.TrimStart('+');

        return string.Equals((row.ImportPlanId ?? string.Empty).TrimStart('+'), wanted, StringComparison.OrdinalIgnoreCase)
            || row.PreviewTags.Any(tag => string.Equals(tag.TrimStart('+'), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>An entry as a task reference another pane can hand back: its title, or
    /// the first block of its id where it has none, and the id itself to open it by.</summary>
    private static DeliveryRunReference? TaskReference(EntryRow? row) =>
        row is { Id: { } id }
            ? new DeliveryRunReference(
                DeliveryRunReferenceKind.Task,
                string.IsNullOrWhiteSpace(row.PreviewTitle) ? id.ToString()[..8] : row.PreviewTitle,
                Title: null,
                Url: null,
                Repository: null,
                EntryId: id)
            : null;
}
