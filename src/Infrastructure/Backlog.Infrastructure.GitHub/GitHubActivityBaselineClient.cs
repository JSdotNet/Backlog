using System.Globalization;
using System.Text.Json;

namespace Backlog.Infrastructure.GitHub;

/// <summary>One contiguous stretch of time the baseline is asked about.</summary>
public sealed record ActivityBlock(DateTimeOffset From, DateTimeOffset To);

/// <summary>What one block came back with: how many pull requests were merged and
/// how many issues were closed in it, across every repository asked about.</summary>
public sealed record ActivityBlockCounts(
    DateTimeOffset From,
    DateTimeOffset To,
    int MergedPullRequests,
    int ClosedIssues);

/// <summary>
/// A whole baseline: one row per block, in the order they were asked for.
/// <para>
/// <see cref="Complete"/> false means at least one query was refused or came back
/// unreadable, so at least one row is a floor. Reported rather than thrown, because
/// eleven weeks of history with one gap is worth drawing and an exception is not.
/// </para>
/// </summary>
public sealed record GitHubActivityBaseline(IReadOnlyList<ActivityBlockCounts> Blocks, bool Complete)
{
    public static GitHubActivityBaseline Empty { get; } = new([], true);
}

/// <summary>
/// Counts, and nothing else: how much was merged and closed in each of a series of
/// time blocks.
/// <para>
/// Deliberately a different port from <see cref="IGitHubActivityClient"/> rather
/// than a mode of it. That one answers "which pull requests, and what happened
/// inside them", which costs a page walk plus several calls per pull request and is
/// therefore affordable only over a recent window. This one answers "how many", at
/// two calls per block per owner regardless of how many pull requests the block
/// holds, which is what makes a year of history drawable at all.
/// </para>
/// </summary>
public interface IGitHubActivityBaselineClient
{
    /// <summary>
    /// Merged pull request and closed issue counts per block, for work authored by
    /// <paramref name="author"/> in <paramref name="repositories"/>.
    /// </summary>
    Task<GitHubActivityBaseline> GetBaselineAsync(
        IReadOnlyList<GitHubRepositoryRef> repositories,
        IReadOnlyList<ActivityBlock> blocks,
        string author,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IGitHubActivityBaselineClient"/> over the search API's
/// <c>total_count</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Search is the right tool here, and the comment in
/// <c>GitHubActivityClient.ReadPullRequestsAsync</c> rejecting it is not being
/// contradicted.</strong> That comment rejects search for <em>listing</em>, and
/// both of its objections are about the results array: search caps at a thousand
/// hits with no way to tell that it did, and its index lags behind by up to a
/// minute. Neither bites a historical count. The cap applies to the rows search
/// will hand back, not to <c>total_count</c>, which is exact and comes with
/// <c>incomplete_results: false</c> — verified live against a repository well past
/// a thousand matches. And index lag is a fact about the last minute, while every
/// block here closed days or weeks ago. So please do not "fix" this back to the
/// list endpoints: doing so would turn two calls per block into a page walk over
/// the entire history of the repository.
/// </para>
/// <para>
/// One query per owner rather than one query for everything. A single query naming
/// repositories under two owners has to be authenticated by one credential, and no
/// credential is guaranteed to satisfy both — the result would not be an error, it
/// would be a count silently missing whichever repositories that credential cannot
/// see. Split, each query goes out on the identity <c>GitHubSettings.AccountForPath</c>
/// binds to its owner.
/// </para>
/// </remarks>
public sealed class GitHubActivityBaselineClient(IGitHubTransport transport) : IGitHubActivityBaselineClient
{
    /// <summary>How many search queries are in flight at once. The same reasoning
    /// as the activity client's batch size: the <c>gh</c> transport launches a
    /// process per call, and a year of weekly blocks is over a hundred of them.</summary>
    private const int MaxConcurrentQueries = 4;

    public async Task<GitHubActivityBaseline> GetBaselineAsync(
        IReadOnlyList<GitHubRepositoryRef> repositories,
        IReadOnlyList<ActivityBlock> blocks,
        string author,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);

        if (blocks.Count == 0 || repositories.Count == 0) return GitHubActivityBaseline.Empty;

        var owners = repositories
            .GroupBy(repository => repository.Owner, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Select(repository => repository.FullName).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            .ToList();

        var queries = new List<Query>(blocks.Count * owners.Count * 2);

        for (var index = 0; index < blocks.Count; index++)
        {
            foreach (var owned in owners)
            {
                queries.Add(new Query(index, Kind.PullRequest, PullRequestPath(author, blocks[index], owned)));
                queries.Add(new Query(index, Kind.Issue, IssuePath(author, blocks[index], owned)));
            }
        }

        var merged = new int[blocks.Count];
        var closed = new int[blocks.Count];
        var complete = true;

        foreach (var batch in queries.Chunk(MaxConcurrentQueries))
        {
            var answers = await Task
                .WhenAll(batch.Select(query => CountAsync(query, cancellationToken)))
                .ConfigureAwait(false);

            foreach (var answer in answers)
            {
                if (answer.Count is not { } count)
                {
                    complete = false;
                    continue;
                }

                if (answer.Query.Of == Kind.PullRequest) merged[answer.Query.Block] += count;
                else closed[answer.Query.Block] += count;
            }
        }

        return new GitHubActivityBaseline(
            [.. blocks.Select((block, index) =>
                new ActivityBlockCounts(block.From, block.To, merged[index], closed[index]))],
            complete);
    }

    private static string PullRequestPath(string author, ActivityBlock block, IEnumerable<string> repositories) =>
        SearchPath($"author:{author}+is:pr+is:merged+merged:{Range(block)}", repositories);

    private static string IssuePath(string author, ActivityBlock block, IEnumerable<string> repositories) =>
        SearchPath($"author:{author}+is:issue+is:closed+closed:{Range(block)}", repositories);

    /// <summary>
    /// One search, asking for a single row.
    /// <para>
    /// <c>per_page=1</c> because the rows are not wanted at all — only
    /// <c>total_count</c> is, and asking for a hundred rows to read one number
    /// would be a hundred times the response for nothing.
    /// </para>
    /// </summary>
    private static string SearchPath(string qualifiers, IEnumerable<string> repositories) =>
        $"search/issues?q={qualifiers}{string.Concat(repositories.Select(name => $"+repo:{name}"))}&per_page=1";

    private static string Range(ActivityBlock block) => $"{Rfc3339(block.From)}..{Rfc3339(block.To)}";

    private static string Rfc3339(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>
    /// One query's count, or null when it could not be answered.
    /// <para>
    /// Null rather than zero, and that distinction is the whole of
    /// <see cref="GitHubActivityBaseline.Complete"/>: a refused query and a block
    /// in which nothing was merged look identical once they are both a zero.
    /// </para>
    /// </summary>
    private async Task<(Query Query, int? Count)> CountAsync(Query query, CancellationToken cancellationToken)
    {
        try
        {
            var response = await transport
                .SendAsync(HttpMethod.Get, query.Path, body: null, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (response.ValueKind != JsonValueKind.Object) return (query, null);

            return response.TryGetProperty("total_count", out var total)
                && total.ValueKind == JsonValueKind.Number
                && total.TryGetInt32(out var count)
                    ? (query, count)
                    : (query, null);
        }
        catch (Exception exception) when (exception is GitHubException or GitHubNotConfiguredException)
        {
            return (query, null);
        }
    }

    private enum Kind
    {
        PullRequest,
        Issue
    }

    private sealed record Query(int Block, Kind Of, string Path);
}
