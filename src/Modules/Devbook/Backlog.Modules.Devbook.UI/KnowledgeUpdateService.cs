using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// Answers "is this knowledge the latest version?" and, when it is not, brings it
/// up to date.
/// <para>
/// Knowledge read out of a repository is read out of a clone, and a clone goes
/// stale quietly: the folder is still there, the chapters still open, and the
/// version somebody else wrote yesterday is not in them. This is the one place
/// that asks, so no panel has to.
/// </para>
/// <para>
/// It holds the repository store and the git adapter directly, the way
/// <see cref="KnowledgeScope"/> does, because a screen legitimately talks to an
/// adapter and because the question is about a clone — a thing only the adapter
/// knows anything about. What it does <em>not</em> do is hand either of them
/// upward: the pane sees <see cref="KnowledgeUpdateState"/> and nothing else.
/// </para>
/// </summary>
public sealed class KnowledgeUpdateService(
    GitHubSettingsStore repositories,
    ILocalGitRepositoryService git,
    IKnowledgeFolderSource folders,
    IKnowledgeSnapshotCache? snapshots = null)
{
    /// <summary>
    /// Whether this scope has a latest version at all.
    /// <para>
    /// Knowledge kept in the storage folder is nobody's clone and nobody's
    /// branch — there is no remote to be behind and nothing to fetch — so it is
    /// answered here rather than by a check that would only fail, and the pane
    /// leaves the control out instead of showing one that never works.
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

        return repository.KnowledgeSource is KnowledgeSourceKind.Branch
            ? snapshots is null ? null : repository
            : repository;
    }

    /// <summary>
    /// Ask the remote where the latest version is. Contacts the network, so it
    /// runs when somebody asks and never on its own.
    /// </summary>
    public async Task<KnowledgeUpdateState> CheckAsync(string? repositoryAlias, CancellationToken cancellationToken = default)
    {
        var repository = Repository(repositoryAlias);
        if (repository is null) return KnowledgeUpdateState.NotApplicable;

        if (repository.KnowledgeSource is KnowledgeSourceKind.Branch)
        {
            var snapshot = await snapshots!
                .CheckAsync(repository, repository.KnowledgeBranch, cancellationToken)
                .ConfigureAwait(false);

            return FromSnapshot(snapshot);
        }

        var check = await git.CheckForUpdatesAsync(repository, repository.CloneDirectory, cancellationToken).ConfigureAwait(false);
        return KnowledgeUpdateState.From(check);
    }

    /// <summary>
    /// Bring the knowledge up to the latest version — pulling the clone, or
    /// re-fetching the branch snapshot — then tell the folder source its content
    /// was replaced so every open panel re-reads it.
    /// </summary>
    public async Task<KnowledgeUpdateState> PullAsync(string? repositoryAlias, CancellationToken cancellationToken = default)
    {
        var repository = Repository(repositoryAlias);
        if (repository is null) return KnowledgeUpdateState.NotApplicable;

        if (repository.KnowledgeSource is KnowledgeSourceKind.Branch)
        {
            var fetched = await snapshots!
                .FetchAsync(repository, repository.KnowledgeBranch, cancellationToken)
                .ConfigureAwait(false);

            // Announced only when something actually landed, for the reason the
            // clone path announces after the pull: a panel reloading on the news
            // must find the new text, and a failed fetch has no new text to find.
            if (fetched.Updated) folders.NotifyContentChanged();

            return FromSnapshot(fetched);
        }

        var result = await git.PullAsync(repository, repository.CloneDirectory, cancellationToken).ConfigureAwait(false);
        if (!result.Success) return KnowledgeUpdateState.Blocked(result.Message);

        // The announcement comes after the files have landed and before anything
        // is reported back, so a panel that reloads on the news reloads the pulled
        // text rather than the text that was there when the button was pressed.
        folders.NotifyContentChanged();

        return result.State is null
            ? new KnowledgeUpdateState(KnowledgeUpdateAvailability.UpToDate, 0, result.Message)
            : KnowledgeUpdateState.From(result.State) with { Message = result.Message };
    }

    /// <summary>
    /// A snapshot result as the pane's four states.
    /// <para>
    /// <c>BehindBy</c> is always zero: a commit id says the branch has moved but
    /// not by how much, and counting would be a second call for a number the
    /// badge can simply leave out. <see cref="KnowledgeUpdatePresentation.BehindLabel"/>
    /// already renders nothing for zero.
    /// </para>
    /// </summary>
    private static KnowledgeUpdateState FromSnapshot(KnowledgeSnapshotResult result) => result switch
    {
        { Updated: true } => new KnowledgeUpdateState(KnowledgeUpdateAvailability.UpToDate, 0, result.Message),
        { Behind: true } => new KnowledgeUpdateState(KnowledgeUpdateAvailability.UpdateAvailable, 0, result.Message),
        { Message: not null } and { Snapshot: null } => KnowledgeUpdateState.Blocked(result.Message),
        _ => new KnowledgeUpdateState(KnowledgeUpdateAvailability.UpToDate, 0, result.Message)
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
public enum KnowledgeUpdateAvailability
{
    /// <summary>This knowledge has no clone behind it, so no latest version to be on.</summary>
    NotApplicable,

    /// <summary>Nobody has asked yet.</summary>
    NotChecked,

    /// <summary>The clone has everything the remote has.</summary>
    UpToDate,

    /// <summary>The remote is ahead and a pull would bring it in.</summary>
    UpdateAvailable,

    /// <summary>Something is in the way; <see cref="KnowledgeUpdateState.Message"/> says what.</summary>
    Blocked
}

/// <summary>
/// The version state the pane renders: what can be done, how far behind, and the
/// sentence to show. The sentence comes up from the adapter rather than being
/// assembled here, so the reason a clone cannot be pulled is git's reason and not
/// a paraphrase of it.
/// </summary>
public sealed record KnowledgeUpdateState(
    KnowledgeUpdateAvailability Availability,
    int BehindBy,
    string? Message)
{
    public static readonly KnowledgeUpdateState NotApplicable = new(KnowledgeUpdateAvailability.NotApplicable, 0, null);

    public static readonly KnowledgeUpdateState NotChecked = new(KnowledgeUpdateAvailability.NotChecked, 0, null);

    public bool CanPull => Availability is KnowledgeUpdateAvailability.UpdateAvailable;

    public static KnowledgeUpdateState Blocked(string message) => new(KnowledgeUpdateAvailability.Blocked, 0, message);

    public static KnowledgeUpdateState From(LocalGitRepositoryUpdateCheck check) => check switch
    {
        { CanPull: true } => new KnowledgeUpdateState(KnowledgeUpdateAvailability.UpdateAvailable, check.Behind, check.Summary),
        { Currency: LocalGitRepositoryCurrency.UpToDate } => new KnowledgeUpdateState(KnowledgeUpdateAvailability.UpToDate, 0, check.Summary),
        _ => new KnowledgeUpdateState(KnowledgeUpdateAvailability.Blocked, check.Behind, check.Summary)
    };
}

/// <summary>
/// The words and classes the version control wears, kept out of the markup so a
/// test can pin them and so the pane's own code reads as layout rather than as
/// copywriting. The same split <c>AppUpdatePresentation</c> makes for the
/// application's own updates.
/// </summary>
public static class KnowledgeUpdatePresentation
{
    /// <summary>What the one button says. "Check now" rather than "Refresh",
    /// because refresh suggests something that would have happened anyway and is
    /// being hurried along — nothing here checks unless a person asks.</summary>
    public static string ActionLabel(KnowledgeUpdateState state) =>
        state.CanPull ? "Pull latest" : "Check now";

    /// <summary>The word shown while the action runs, and the button's accessible
    /// busy label.</summary>
    public static string BusyLabel(KnowledgeUpdateState state) =>
        state.CanPull ? "Pulling" : "Checking";

    /// <summary>How far behind, short enough for a badge, or null when there is
    /// nothing to say. A number without "commits" after it, because the badge sits
    /// beside a button that says what would be pulled.</summary>
    public static string? BehindLabel(KnowledgeUpdateState state) =>
        state.Availability is KnowledgeUpdateAvailability.UpdateAvailable && state.BehindBy > 0
            ? $"{state.BehindBy} behind"
            : null;

    /// <summary>The modifier the status line wears, so up to date, an available
    /// update and something in the way do not all read the same.</summary>
    public static string StatusClass(KnowledgeUpdateState state) => state.Availability switch
    {
        KnowledgeUpdateAvailability.UpToDate => "knowledge-stack__update-status knowledge-stack__update-status--ok",
        KnowledgeUpdateAvailability.UpdateAvailable => "knowledge-stack__update-status knowledge-stack__update-status--available",
        KnowledgeUpdateAvailability.Blocked => "knowledge-stack__update-status knowledge-stack__update-status--blocked",
        _ => "knowledge-stack__update-status"
    };
}
