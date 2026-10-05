using System.Globalization;

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

    private const string PointsLabelPrefix = "points:";
    private const string BlockedLabel = "blocked";

    private readonly IGitHubClient _client;
    private readonly Func<GitHubSettings> _settings;
    private readonly IGitHubIdentityClient _identity;
    private readonly Func<CancellationToken, Task<IReadOnlyList<EntryProjectionDto>>> _projections;

    /// <summary>
    /// The container's form. The projections are read through a scope per fetch,
    /// because the task port is scoped and a connector lives as long as the app.
    /// </summary>
    public GitHubConnector(
        IGitHubClient client,
        GitHubSettingsStore settings,
        IGitHubIdentityClient identity,
        IServiceScopeFactory scopes)
        : this(client, () => settings.Current, identity, cancellationToken => ReadProjectionsAsync(scopes, cancellationToken))
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(scopes);
    }

    /// <summary>The composed form: the settings read per call, and the entries'
    /// projections from wherever the caller keeps them.</summary>
    internal GitHubConnector(
        IGitHubClient client,
        Func<GitHubSettings> settings,
        IGitHubIdentityClient identity,
        Func<CancellationToken, Task<IReadOnlyList<EntryProjectionDto>>> projections)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(projections);

        _client = client;
        _settings = settings;
        _identity = identity;
        _projections = projections;
    }

    public TaskConnectorDescriptor Descriptor { get; } = new(ConnectorId, "GitHub", "github", "--color-text-primary");

    /// <summary>Effort, from a points label. GitHub issues name no dependencies this
    /// connector reads, and nothing is written back yet.</summary>
    public TaskConnectorCapabilities Capabilities { get; } = new(HasEffort: true);

    /// <summary>The configured repositories, in the order they were configured.</summary>
    public Task<IReadOnlyList<string>> ListTargetsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([.. _settings().Repositories.Select(repository => repository.FullName)]);

    public async Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        var repository = RepositoryFor(target);

        var read = await _client.SearchIssuesAsync(repository, since, cancellationToken).ConfigureAwait(false);

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
    /// The login every configured repository is bound to, when they agree on one;
    /// otherwise whoever this machine is signed in to GitHub as.
    /// <para>
    /// The contract asks once rather than per target, so a workspace whose
    /// repositories are bound to different accounts has no single answer; the
    /// signed-in login is the one an unbound repository is reached as.
    /// </para>
    /// </summary>
    public async Task<string?> WhoAmIAsync(CancellationToken cancellationToken)
    {
        var repositories = _settings().Repositories;
        var bound = repositories
            .Select(repository => repository.Account)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (repositories.Count > 0 && bound is [{ } login]) return login;

        return await _identity.GetLoginAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The configured repository a target names. Anything else is refused:
    /// a repository that is not configured has no account to be reached as.</summary>
    private GitHubRepositoryRef RepositoryFor(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        return _settings().Repositories.FirstOrDefault(repository =>
                string.Equals(repository.FullName, target.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new GitHubNotConfiguredException(
                $"{target} is not one of the repositories configured in Settings, so its issues cannot be synced.");
    }

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
