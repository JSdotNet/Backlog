using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Answers "is this knowledge the latest version?" and, when it is not, brings it
/// up to date.
/// <para>
/// Devbook read out of a repository is read out of a clone, and a clone goes
/// stale quietly: the folder is still there, the chapters still open, and the
/// version somebody else wrote yesterday is not in them. This is the one place
/// that asks, so no panel has to.
/// </para>
/// <para>
/// It holds the repository store and the git adapter directly, the way
/// <see cref="DevbookScope"/> does, because a screen legitimately talks to an
/// adapter and because the question is about a clone — a thing only the adapter
/// knows anything about. What it does <em>not</em> do is hand either of them
/// upward: the pane sees <see cref="DevbookUpdateState"/> and nothing else.
/// </para>
/// </summary>
public sealed class DevbookUpdateService(
    GitHubSettingsStore repositories,
    ILocalGitRepositoryService git,
    IDevbookFolderSource folders,
    IDevbookSnapshotCache? snapshots = null)
{
    /// <summary>
    /// Whether this scope has a latest version at all.
    /// <para>
    /// No scope is nobody's clone and nobody's branch — there is no remote to
    /// be behind and nothing to fetch — so it is answered here rather than by a
    /// check that would only fail, and the pane leaves the control out instead
    /// of showing one that never works.
    /// </para>
    /// <para>
    /// A repository with no clone used to be answered the same way. It is not
    /// any more: reading a branch is exactly the case where "is this still the
    /// latest?" is worth asking, because nothing about a snapshot tells you the
    /// branch has moved.
    /// </para>
    /// </summary>
    public bool CanCheck(string? repositoryAlias) => Repository(repositoryAlias) is not null;

    /// <summary>The repository whose clone or branch carries this scope's
    /// knowledge, or null when the scope has neither behind it.</summary>
    public GitHubRepositoryRef? Repository(string? repositoryAlias)
    {
        if (string.IsNullOrWhiteSpace(repositoryAlias)) return null;

        var repository = repositories.Current.Find(repositoryAlias);
        if (repository is null) return null;

        return repository.DevbookSource is DevbookSourceKind.Branch
            ? snapshots is null ? null : repository
            : repository;
    }

    /// <summary>
    /// Ask the remote where the latest version is. Contacts the network, so it
    /// runs when somebody asks and never on its own.
    /// </summary>
    public async Task<DevbookUpdateState> CheckAsync(string? repositoryAlias, CancellationToken cancellationToken = default)
    {
        var repository = Repository(repositoryAlias);
        if (repository is null) return DevbookUpdateState.NotApplicable;

        if (repository.DevbookSource is DevbookSourceKind.Branch)
        {
            var snapshot = await snapshots!
                .CheckAsync(repository, repository.DevbookBranch, cancellationToken)
                .ConfigureAwait(false);

            return FromSnapshot(snapshot);
        }

        var check = await git.CheckForUpdatesAsync(repository, repository.CloneDirectory, cancellationToken).ConfigureAwait(false);
        return DevbookUpdateState.From(check);
    }

    /// <summary>
    /// Bring the knowledge up to the latest version — pulling the clone, or
    /// refreshing the branch's index — then tell the folder source its content
    /// was replaced so every open panel re-reads it.
    /// <para>
    /// For a branch, "pull" is the index: the listing of the new commit, with
    /// every fetched file the commit changed dropped from disk. The chapters
    /// themselves come back as the panels reload and prepare what they show, so
    /// a refresh costs one listing plus whatever actually moved, not the tree.
    /// </para>
    /// </summary>
    public async Task<DevbookUpdateState> PullAsync(string? repositoryAlias, CancellationToken cancellationToken = default)
    {
        var repository = Repository(repositoryAlias);
        if (repository is null) return DevbookUpdateState.NotApplicable;

        if (repository.DevbookSource is DevbookSourceKind.Branch)
        {
            var fetched = await snapshots!
                .FetchAsync(repository, repository.DevbookBranch, cancellationToken)
                .ConfigureAwait(false);

            // Announced only when something actually landed, for the reason the
            // clone path announces after the pull: a panel reloading on the news
            // must find the new text, and a failed fetch has no new text to find.
            if (fetched.Updated) folders.NotifyContentChanged();

            return FromSnapshot(fetched);
        }

        var result = await git.PullAsync(repository, repository.CloneDirectory, cancellationToken).ConfigureAwait(false);
        if (!result.Success) return DevbookUpdateState.Blocked(result.Message);

        // The announcement comes after the files have landed and before anything
        // is reported back, so a panel that reloads on the news reloads the pulled
        // text rather than the text that was there when the button was pressed.
        folders.NotifyContentChanged();

        return result.State is null
            ? new DevbookUpdateState(DevbookUpdateAvailability.UpToDate, 0, result.Message)
            : DevbookUpdateState.From(result.State) with { Message = result.Message };
    }

    /// <summary>
    /// A snapshot result as the pane's four states.
    /// <para>
    /// <c>BehindBy</c> is always zero: a commit id says the branch has moved but
    /// not by how much, and counting would be a second call for a number the
    /// badge can simply leave out. <see cref="DevbookUpdatePresentation.BehindLabel"/>
    /// already renders nothing for zero.
    /// </para>
    /// </summary>
    private static DevbookUpdateState FromSnapshot(DevbookSnapshotResult result) => result switch
    {
        { Updated: true } => new DevbookUpdateState(DevbookUpdateAvailability.UpToDate, 0, result.Message),
        { Behind: true } => new DevbookUpdateState(DevbookUpdateAvailability.UpdateAvailable, 0, result.Message),
        { Message: not null } and { Snapshot: null } => DevbookUpdateState.Blocked(result.Message),
        _ => new DevbookUpdateState(DevbookUpdateAvailability.UpToDate, 0, result.Message)
    };
}

/// <summary>
/// What the pane can do about the version it is reading.
/// <para>
/// Fewer states than git has, on purpose. A clone that is ahead, one that has
/// diverged, one on a branch tracking nothing and one whose remote could not be
/// reached are four different things to git and one thing to a reader: something
/// is in the way, and it is not a button's job to clear it. Only
/// <see cref="UpdateAvailable"/> earns an action.
/// </para>
/// </summary>
public enum DevbookUpdateAvailability
{
    /// <summary>This knowledge has no clone behind it, so no latest version to be on.</summary>
    NotApplicable,

    /// <summary>Nobody has asked yet.</summary>
    NotChecked,

    /// <summary>The clone has everything the remote has.</summary>
    UpToDate,

    /// <summary>The remote is ahead and a pull would bring it in.</summary>
    UpdateAvailable,

    /// <summary>Something is in the way; <see cref="DevbookUpdateState.Message"/> says what.</summary>
    Blocked
}

/// <summary>
/// The version state the pane renders: what can be done, how far behind, and the
/// sentence to show. The sentence comes up from the adapter rather than being
/// assembled here, so the reason a clone cannot be pulled is git's reason and not
/// a paraphrase of it.
/// </summary>
public sealed record DevbookUpdateState(
    DevbookUpdateAvailability Availability,
    int BehindBy,
    string? Message)
{
    public static readonly DevbookUpdateState NotApplicable = new(DevbookUpdateAvailability.NotApplicable, 0, null);

    public static readonly DevbookUpdateState NotChecked = new(DevbookUpdateAvailability.NotChecked, 0, null);

    public bool CanPull => Availability is DevbookUpdateAvailability.UpdateAvailable;

    public static DevbookUpdateState Blocked(string message) => new(DevbookUpdateAvailability.Blocked, 0, message);

    public static DevbookUpdateState From(LocalGitRepositoryUpdateCheck check) => check switch
    {
        { CanPull: true } => new DevbookUpdateState(DevbookUpdateAvailability.UpdateAvailable, check.Behind, check.Summary),
        { Currency: LocalGitRepositoryCurrency.UpToDate } => new DevbookUpdateState(DevbookUpdateAvailability.UpToDate, 0, check.Summary),
        _ => new DevbookUpdateState(DevbookUpdateAvailability.Blocked, check.Behind, check.Summary)
    };
}

/// <summary>
/// The words and classes the version control wears, kept out of the markup so a
/// test can pin them and so the pane's own code reads as layout rather than as
/// copywriting. The same split <c>AppUpdatePresentation</c> makes for the
/// application's own updates.
/// </summary>
public static class DevbookUpdatePresentation
{
    /// <summary>What the one button says. "Check now" rather than "Refresh",
    /// because refresh suggests something that would have happened anyway and is
    /// being hurried along — a clone is never checked unless a person asks, and a
    /// branch is re-checked on a cadence the folder source owns, which this
    /// button cuts short rather than replaces.</summary>
    public static string ActionLabel(DevbookUpdateState state) =>
        state.CanPull ? "Pull latest" : "Check now";

    /// <summary>The word shown while the action runs, and the button's accessible
    /// busy label.</summary>
    public static string BusyLabel(DevbookUpdateState state) =>
        state.CanPull ? "Pulling" : "Checking";

    /// <summary>How far behind, short enough for a badge, or null when there is
    /// nothing to say. A number without "commits" after it, because the badge sits
    /// beside a button that says what would be pulled.</summary>
    public static string? BehindLabel(DevbookUpdateState state) =>
        state.Availability is DevbookUpdateAvailability.UpdateAvailable && state.BehindBy > 0
            ? $"{state.BehindBy} behind"
            : null;

    /// <summary>The modifier the status line wears, so up to date, an available
    /// update and something in the way do not all read the same.</summary>
    public static string StatusClass(DevbookUpdateState state) => state.Availability switch
    {
        DevbookUpdateAvailability.UpToDate => "devbook-stack__update-status devbook-stack__update-status--ok",
        DevbookUpdateAvailability.UpdateAvailable => "devbook-stack__update-status devbook-stack__update-status--available",
        DevbookUpdateAvailability.Blocked => "devbook-stack__update-status devbook-stack__update-status--blocked",
        _ => "devbook-stack__update-status"
    };
}
