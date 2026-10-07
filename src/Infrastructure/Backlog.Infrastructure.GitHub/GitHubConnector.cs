using System.Globalization;

using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// GitHub issues as linked tasks: the <see cref="ITaskConnector"/> for a configured
/// repository (local ADR 0020, §3).
/// <para>
/// A target is a repository from <see cref="GitHubSettings"/>, spelled
/// <c>owner/name</c>. The fetch goes out through <see cref="IGitHubClient"/>'s issue
/// search, whose <c>repo:</c> qualifier routes it to whichever account the
/// repository is bound to, so this class never sees a credential.
/// </para>
/// <para>
/// The item an issue becomes: its node id is the external id, because the number
/// and the repository change when an issue is transferred; <c>#number</c> is the
/// display key; open is Open, closed as completed is Done, and closed as not planned
/// or as a duplicate is Dropped. Labels pass through as GitHub spells them, a
/// <c>points:&lt;n&gt;</c> label is the effort and a <c>blocked</c> label marks the
/// item blocked. Pull requests are never items.
/// </para>
/// <para>
/// The loop guard: an issue a Backlog task was pushed to already belongs to that
/// task, through its issue projection, and is left out, so a pushed task never comes
/// back as a second one.
/// </para>
/// </summary>
public sealed class GitHubConnector : ITaskConnector
{
    /// <summary>The connector id every GitHub linked task carries. Stored on each
    /// task, so it never changes.</summary>
    public const string ConnectorId = "github";

    /// <summary>
    /// How far before the last sync the closed query reaches back. Issue search is
    /// an index, and it lags: an issue closed just before a sync started — by the
    /// write-back that asked for that very sync, for one — can still read as open to
    /// it, and the next sync's "closed since" would then start after the close. Seen
    /// in neither query, the item would read as vanished and its task be archived.
    /// Reaching back further only returns an already-closed issue once more, which
    /// the sync takes as no change.
    /// </summary>
    public static readonly TimeSpan ClosedQueryOverlap = TimeSpan.FromMinutes(10);

    private const string PointsLabelPrefix = "points:";
    private const string BlockedLabel = "blocked";

    private readonly IGitHubClient _client;
    private readonly Func<GitHubSettings> _settings;
    private readonly Func<CancellationToken, Task<IReadOnlyList<EntryProjectionDto>>> _projections;

    /// <summary>
    /// The container's form. The projections are read through a scope per fetch,
    /// because the task port is scoped and a connector lives as long as the app.
    /// </summary>
    public GitHubConnector(
        IGitHubClient client,
        GitHubSettingsStore settings,
        IServiceScopeFactory scopes)
        : this(client, () => settings.Current, cancellationToken => ReadProjectionsAsync(scopes, cancellationToken))
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(scopes);
    }

    /// <summary>The composed form: the settings read per call, and the entries'
    /// projections from wherever the caller keeps them.</summary>
    internal GitHubConnector(
        IGitHubClient client,
        Func<GitHubSettings> settings,
        Func<CancellationToken, Task<IReadOnlyList<EntryProjectionDto>>> projections)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(projections);

        _client = client;
        _settings = settings;
        _projections = projections;
    }

    /// <summary>A target is a repository, typed the way Settings lists it.</summary>
    public TaskConnectorDescriptor Descriptor { get; } = new(ConnectorId, "GitHub", "github", "--color-text-primary")
    {
        TargetLabel = "Repository",
        TargetPlaceholder = "owner/repository",
    };

    /// <summary>Effort, from a points label, and completing an issue by closing it.
    /// GitHub issues name no dependencies this connector reads. A target is a
    /// repository, so its tasks are filed under it.</summary>
    public TaskConnectorCapabilities Capabilities { get; } = new(HasEffort: true, CanComplete: true, TargetIsRepository: true);

    /// <summary>The configured repositories, in the order they were configured.</summary>
    public Task<IReadOnlyList<string>> ListTargetsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([.. _settings().Repositories.Select(repository => repository.FullName)]);

    /// <summary>The configured repositories to pick from, each named by its
    /// <c>owner/name</c>. With none configured, the answer says where one is added:
    /// a repository typed here that Settings does not configure has no account to
    /// be reached as, and would fail its first sync.</summary>
    public async Task<ConnectorTargetChoices> ListTargetChoicesAsync(CancellationToken cancellationToken)
    {
        var targets = await ListTargetsAsync(cancellationToken).ConfigureAwait(false);

        return targets.Count == 0
            ? ConnectorTargetChoices.Unavailable("No repository is configured in Settings → GitHub yet. Add one there, then pick it here.")
            : new ConnectorTargetChoices([.. targets.Select(target => new ConnectorTargetChoice(target, target))]);
    }

    /// <summary>
    /// The repository's open issues, and those closed since the last sync.
    /// <para>
    /// A refusal is said as what it means for the person, through
    /// <see cref="TaskConnectorFetchException"/>: a repository Settings does not
    /// configure, nobody to sign in as, and a repository the account it is worked as
    /// cannot see — issue search answers that with a 422 rather than a 404, so a
    /// private repository bound to the wrong account read as GitHub being down. A
    /// failure with no status is the network, and is left as it was thrown.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        var repository = RepositoryFor(target);

        GitHubIssueSearchRead read;
        try
        {
            read = await _client.SearchIssuesAsync(repository, since - ClosedQueryOverlap, cancellationToken).ConfigureAwait(false);
        }
        catch (GitHubNotConfiguredException ex)
        {
            throw new TaskConnectorFetchException(TaskConnectorFetchFailure.SignInRequired, ex.Message, ex);
        }
        catch (GitHubException ex) when (Classify(ex, repository) is { } recognised)
        {
            throw recognised;
        }

        // A partial answer would read as every issue past the cut having vanished,
        // and the sync would archive their tasks. Failing writes nothing.
        if (read.Truncated)
        {
            throw new GitHubException(
                $"GitHub's issue search would not return every issue in {repository.FullName}, so nothing was synced.");
        }

        var pushed = await PushedIssuesAsync(repository, cancellationToken).ConfigureAwait(false);

        return [.. read.Issues
            .Where(issue => !pushed.Contains(issue.Number))
            .Select(ToItem)];
    }

    /// <summary>
    /// Closes the item's issue as completed, as the repository's account. The number
    /// is the one in the display key, <c>#412</c>, or else the last segment of the
    /// issue's URL; the node id the item is known by is not what the REST call takes.
    /// <para>
    /// The issue is read first, and one already closed is left as it is and answered
    /// as completed: it is finished at the source, and closing it again would rewrite
    /// the reason it closed with — a "not planned" turned into "completed". The sync
    /// brings its real state in.
    /// </para>
    /// <para>
    /// Every refusal is answered in words — the repository is no longer configured,
    /// there is no way to sign in, or GitHub said no, with the status it said it with.
    /// </para>
    /// </summary>
    public async Task<string?> CompleteAsync(SourceRef item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (IssueNumberOf(item) is not { } number)
        {
            return $"{item.DisplayKey} does not name a GitHub issue number, so it could not be closed.";
        }

        var repository = ConfiguredRepository(item.Target);
        if (repository is null)
        {
            return $"{item.Target} is no longer one of the repositories configured in Settings, so {item.DisplayKey} could not be closed.";
        }

        try
        {
            var current = await _client.GetIssueAsync(repository, number, cancellationToken).ConfigureAwait(false);
            if (current.Issue.State != GitHubItemState.Closed)
            {
                await _client.CloseIssueAsync(repository, number, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
        catch (GitHubNotConfiguredException ex)
        {
            return ex.Message;
        }
        catch (GitHubException ex)
        {
            return ex.Status is { } status ? $"GitHub refused ({(int)status}): {ex.Message}" : ex.Message;
        }
    }

    /// <summary>The issue number of a linked item: <c>#412</c> in the display key,
    /// else the trailing <c>/issues/412</c> of its URL; null when neither has
    /// one.</summary>
    internal static int? IssueNumberOf(SourceRef item)
    {
        var key = item.DisplayKey.Trim();
        if (key.StartsWith('#') && int.TryParse(key[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var fromKey) && fromKey > 0)
        {
            return fromKey;
        }

        var segments = item.Url.TrimEnd('/').Split('/');
        return segments is [.., "issues", var last]
            && int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var fromUrl)
            && fromUrl > 0
                ? fromUrl
                : null;
    }

    /// <summary>The configured repository a target names. Anything else is refused:
    /// a repository that is not configured has no account to be reached as.</summary>
    private GitHubRepositoryRef RepositoryFor(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        return ConfiguredRepository(target)
            ?? throw new TaskConnectorFetchException(
                TaskConnectorFetchFailure.NotConfigured,
                $"{target} is not one of the repositories configured in Settings → GitHub, so its issues cannot be synced. Add it there first.");
    }

    /// <summary>
    /// What a status GitHub refused the search with means for the person, or null
    /// for one this connector has no reading of. A 404, or the 422 search answers
    /// for a repository it will not search, is a repository the account cannot see
    /// or that does not exist — GitHub does not say which, so neither does this. A
    /// 403 is an account that may not read it, and a 401 a sign-in GitHub no longer
    /// honours.
    /// </summary>
    private static TaskConnectorFetchException? Classify(GitHubException failure, GitHubRepositoryRef repository) => failure.Status switch
    {
        System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.UnprocessableEntity => new(
            TaskConnectorFetchFailure.NotFound,
            $"GitHub cannot find {repository.FullName}: it does not exist, or the account this repository is worked as cannot see it. Pick the account in Settings → GitHub.",
            failure),
        System.Net.HttpStatusCode.Forbidden => new(
            TaskConnectorFetchFailure.NoAccess,
            $"The account {repository.FullName} is worked as may not read its issues. Pick the account in Settings → GitHub.",
            failure),
        System.Net.HttpStatusCode.Unauthorized => new(
            TaskConnectorFetchFailure.SignInRequired,
            $"GitHub no longer accepts the sign-in {repository.FullName} is worked as. Sign in again in Settings → GitHub.",
            failure),
        _ => null,
    };

    /// <summary>The configured repository a target names, compared without regard to
    /// case, or null.</summary>
    private GitHubRepositoryRef? ConfiguredRepository(string target) =>
        _settings().Repositories.FirstOrDefault(repository =>
            string.Equals(repository.FullName, target.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The numbers of this repository's issues a Backlog task was pushed
    /// to.</summary>
    private async Task<HashSet<int>> PushedIssuesAsync(GitHubRepositoryRef repository, CancellationToken cancellationToken)
    {
        var projections = await _projections(cancellationToken).ConfigureAwait(false);

        return [.. projections
            .Where(projection =>
                string.Equals(projection.TargetType, EntryProjectionDto.IssueTargetType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(projection.RepoId, repository.FullName, StringComparison.OrdinalIgnoreCase))
            .Select(projection => int.TryParse(projection.ExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0)
            .Where(number => number > 0)];
    }

    private static async Task<IReadOnlyList<EntryProjectionDto>> ReadProjectionsAsync(
        IServiceScopeFactory scopes,
        CancellationToken cancellationToken)
    {
        var scope = scopes.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        var entries = await scope.ServiceProvider.GetRequiredService<ITaskItems>().ListAsync(cancellationToken).ConfigureAwait(false);

        return [.. entries.SelectMany(entry => entry.Projections)];
    }

    internal static SourceItem ToItem(GitHubSearchedIssue issue)
    {
        var (state, stateName) = StateOf(issue);

        return new SourceItem(
            issue.NodeId,
            $"#{issue.Number}",
            issue.Title,
            issue.Url,
            issue.Body,
            state,
            stateName,
            issue.AssigneeLogin,
            issue.UpdatedAt,
            issue.Labels,
            Effort: issue.Labels.Select(PointsOf).FirstOrDefault(points => points is not null),
            IsBlocked: issue.Labels.Any(label => string.Equals(label.Trim(), BlockedLabel, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Open is Open. A closed issue is Done unless it closed as not planned or as a
    /// duplicate, which is Dropped: closed without the work being done. No reason at
    /// all is what an issue closed before GitHub recorded reasons answers with, and
    /// closing used to mean completing.
    /// </summary>
    private static (NormalisedSourceState State, string Name) StateOf(GitHubSearchedIssue issue)
    {
        if (issue.IsOpen) return (NormalisedSourceState.Open, "open");

        return issue.StateReason?.Trim().ToLowerInvariant() switch
        {
            "not_planned" => (NormalisedSourceState.Dropped, "closed as not planned"),
            "duplicate" => (NormalisedSourceState.Dropped, "closed as duplicate"),
            "completed" => (NormalisedSourceState.Done, "closed as completed"),
            _ => (NormalisedSourceState.Done, "closed"),
        };
    }

    /// <summary>The <c>n</c> of a <c>points:n</c> label, or null for any other
    /// label or a number that is not a size.</summary>
    private static int? PointsOf(string label)
    {
        var trimmed = label.Trim();
        if (!trimmed.StartsWith(PointsLabelPrefix, StringComparison.OrdinalIgnoreCase)) return null;

        return int.TryParse(trimmed[PointsLabelPrefix.Length..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var points)
            ? points
            : null;
    }
}
