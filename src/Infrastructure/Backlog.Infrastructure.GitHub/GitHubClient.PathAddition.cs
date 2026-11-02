using System.Text.Json;
using static Backlog.Infrastructure.IsoTimestamps;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// The commit that added a path to a repository's default branch, and the pull
/// request it belongs to.
/// </summary>
/// <param name="CommitSha">The oldest commit the default branch's history lists
/// for the path.</param>
/// <param name="CommittedAt">When that commit was committed — what an imported
/// state with no authored time is stamped with.</param>
/// <param name="PullRequest">The pull request the commit belongs to, the merged one
/// when GitHub names several; null when it belongs to none, as a commit pushed
/// straight to the default branch does.</param>
public sealed record GitHubPathAddition(string CommitSha, DateTimeOffset CommittedAt, int? PullRequest);

/// <summary>
/// What came of asking for a path's addition. Three outcomes, kept apart because a
/// caller retries only one of them:
/// <list type="bullet">
/// <item>the addition (<see cref="Addition"/>);</item>
/// <item>GitHub answered and the answer names none (<see cref="Addition"/> null,
/// <see cref="IsRead"/> true) — the path has no history, or one this lookup cannot
/// settle, which <see cref="Unanswered"/> then says. GitHub would answer the same
/// next time;</item>
/// <item>GitHub or the network refused (<see cref="Failure"/>) — a rate limit, a
/// timeout, a credential. That may go another way next time.</item>
/// </list>
/// A result rather than an exception (guideline ADR 0004): the caller runs in the
/// background and decides per path what each outcome means.
/// </summary>
public sealed record GitHubPathAdditionRead(GitHubPathAddition? Addition, string? Failure)
{
    /// <summary>Whether GitHub answered. False is <see cref="Failure"/>'s reason;
    /// true with a null <see cref="Addition"/> is a path with no history, or one
    /// <see cref="Unanswered"/> explains.</summary>
    public bool IsRead => Failure is null;

    /// <summary>Why GitHub's answer names no addition when it is not simply "no
    /// history": a history longer than the lookup reads, a commit listed without a
    /// date. Null otherwise.</summary>
    public string? Unanswered { get; private init; }

    public static GitHubPathAdditionRead Found(GitHubPathAddition addition) => new(addition, null);

    public static GitHubPathAdditionRead NotFound { get; } = new(null, null);

    /// <summary>GitHub answered, and the answer cannot settle the addition. Not
    /// a failure: asking again gets the same answer.</summary>
    public static GitHubPathAdditionRead Unsettled(string reason) => new(null, null) { Unanswered = reason };

    public static GitHubPathAdditionRead Failed(string failure) => new(null, failure);
}

public sealed partial class GitHubClient
{
    /// <summary>The page size GitHub allows at most for a commit listing.</summary>
    private const int CommitPageSize = 100;

    /// <summary>
    /// The most pages of a path's history read: two thousand commits. GitHub lists
    /// newest first and has no "oldest first", so the addition is the last row of
    /// the last page; a devbook record with more history than this is not one there
    /// is, and reading on would turn one lookup into dozens of calls.
    /// </summary>
    private const int CommitPageLimit = 20;

    public async Task<GitHubPathAdditionRead> FindPathAdditionAsync(
        GitHubRepositoryRef repository,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = DevbookSnapshotSelection.Normalize(path);
        var prefix = $"repos/{repository.Owner}/{repository.Name}";

        try
        {
            JsonElement? oldest = null;

            for (var page = 1; page <= CommitPageLimit; page++)
            {
                // No sha: the listing is the default branch's history, which is the
                // branch the import reads.
                var listed = await transport.SendAsync(
                    HttpMethod.Get,
                    $"{prefix}/commits?path={Uri.EscapeDataString(normalized)}&per_page={CommitPageSize}&page={page}",
                    body: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                var rows = listed.ValueKind == JsonValueKind.Array ? listed.GetArrayLength() : 0;
                if (rows > 0) oldest = listed[rows - 1];
                if (rows < CommitPageSize) break;

                if (page == CommitPageLimit)
                {
                    return GitHubPathAdditionRead.Unsettled(
                        $"{normalized} has more than {CommitPageLimit * CommitPageSize} commits on the default branch of {repository.FullName}; its first is not read.");
                }
            }

            if (oldest is not { } commit || String(commit, "sha") is not { Length: > 0 } sha) return GitHubPathAdditionRead.NotFound;

            var committedAt = commit.TryGetProperty("commit", out var details) && details.ValueKind == JsonValueKind.Object
                ? Dated(details, "committer") ?? Dated(details, "author")
                : null;

            if (committedAt is null)
            {
                return GitHubPathAdditionRead.Unsettled($"GitHub listed commit {sha} of {normalized} without a date.");
            }

            var pulls = await transport.SendAsync(
                HttpMethod.Get,
                $"{prefix}/commits/{Uri.EscapeDataString(sha)}/pulls",
                body: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return GitHubPathAdditionRead.Found(new GitHubPathAddition(sha, committedAt.Value, PullRequestOf(pulls)));
        }
        catch (GitHubException exception)
        {
            return GitHubPathAdditionRead.Failed(exception.Message);
        }
        catch (GitHubNotConfiguredException exception)
        {
            return GitHubPathAdditionRead.Failed(exception.Message);
        }
        catch (HttpRequestException exception)
        {
            return GitHubPathAdditionRead.Failed(exception.Message);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The transport's own timeout, not the caller stopping: a failure to
            // report like any other.
            return GitHubPathAdditionRead.Failed(exception.Message);
        }
    }

    /// <summary>The <c>date</c> of a commit's <c>committer</c> or
    /// <c>author</c>.</summary>
    private static DateTimeOffset? Dated(JsonElement details, string who) =>
        details.TryGetProperty(who, out var person) ? Timestamp(person, "date") : null;

    /// <summary>
    /// The pull request a commit belongs to: the merged one when GitHub lists
    /// several — a commit can sit on a pull request that was closed and opened
    /// again — else the first listed, else none.
    /// </summary>
    private static int? PullRequestOf(JsonElement pulls)
    {
        if (pulls.ValueKind != JsonValueKind.Array) return null;

        int? first = null;

        foreach (var pull in pulls.EnumerateArray())
        {
            if (!pull.TryGetProperty("number", out var number) || !number.TryGetInt32(out var value) || value <= 0) continue;

            if (Timestamp(pull, "merged_at") is not null) return value;

            first ??= value;
        }

        return first;
    }
}
