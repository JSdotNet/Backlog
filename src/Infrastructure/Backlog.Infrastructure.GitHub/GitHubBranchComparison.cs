using System.Globalization;
using System.Text.Json;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// What a branch changed, as GitHub answers it: the whole branch against its
/// base, and one commit against its first parent.
/// <para>
/// Separate from <see cref="IGitHubTreeClient"/> and
/// <see cref="IGitHubBranchCatalog"/> because it answers a different question —
/// not "what does this commit contain" or "where does this branch point", but
/// "what moved between these two". It reads lists of changed paths and the blob
/// id each one ends at; the text on either side is the tree client's to read,
/// one file at a time, when somebody opens it.
/// </para>
/// </summary>
public interface IGitHubBranchComparison
{
    /// <summary>
    /// The three-dot comparison GitHub draws for a pull request: what
    /// <paramref name="headRef"/> has that <paramref name="baseRef"/> has not,
    /// measured from the commit they last shared.
    /// </summary>
    /// <exception cref="GitHubException">Either ref is not there, the two share no
    /// history, there is no access, or the network refused.</exception>
    Task<GitHubComparison> CompareAsync(
        GitHubRepositoryRef repository,
        string baseRef,
        string headRef,
        CancellationToken cancellationToken = default);

    /// <summary>One commit and the files it changed against its first parent.</summary>
    /// <exception cref="GitHubException">No such commit, no access, or the network
    /// refused.</exception>
    Task<GitHubCommitChange> ReadCommitAsync(
        GitHubRepositoryRef repository,
        string commitSha,
        CancellationToken cancellationToken = default);
}

/// <summary>How a file moved between the two sides.</summary>
public enum GitHubFileStatus
{
    Added,
    Removed,
    Modified,
    Renamed
}

/// <summary>One commit in a comparison.</summary>
/// <param name="Sha">The commit id.</param>
/// <param name="ParentSha">The first parent, or null for a root commit. The
/// first because a merge commit is read against the line it was made on.</param>
/// <param name="Subject">The message's first line.</param>
/// <param name="AuthorName">Who wrote it, as the commit says.</param>
/// <param name="AuthoredAt">When it was written, as the commit says.</param>
public sealed record GitHubComparedCommit(
    string Sha,
    string? ParentSha,
    string Subject,
    string? AuthorName,
    DateTimeOffset? AuthoredAt);

/// <summary>One changed path.</summary>
/// <param name="Path">Where the file is after the change, or where it was for a
/// removed file.</param>
/// <param name="PreviousPath">Where a renamed file was before; null otherwise.</param>
/// <param name="Status">What happened to it.</param>
/// <param name="BlobSha">The blob the file ends at; null for a removed file,
/// which ends at nothing.</param>
public sealed record GitHubChangedFile(
    string Path,
    string? PreviousPath,
    GitHubFileStatus Status,
    string? BlobSha);

/// <summary>
/// A branch against its base.
/// <para>
/// <see cref="Commits"/> are in GitHub's order, oldest first, and GitHub stops
/// listing them at 250. <see cref="TotalCommits"/> is how many there really are,
/// so <see cref="IsTruncated"/> is carried rather than thrown: a partial list
/// still reviews every commit it names, and the caller decides how to say so.
/// </para>
/// </summary>
public sealed record GitHubComparison(
    string MergeBaseSha,
    int AheadBy,
    int BehindBy,
    int TotalCommits,
    IReadOnlyList<GitHubComparedCommit> Commits,
    IReadOnlyList<GitHubChangedFile> Files)
{
    public bool IsTruncated => Commits.Count < TotalCommits;

    /// <summary>Whether GitHub may have stopped listing files: it lists at most
    /// <see cref="GitHubBranchComparison.FileListLimit"/> and says nothing about
    /// the rest, so a full list is the only sign.</summary>
    public bool FilesMayBeTruncated => Files.Count >= GitHubBranchComparison.FileListLimit;
}

/// <summary>One commit and what it changed against its first parent.</summary>
public sealed record GitHubCommitChange(GitHubComparedCommit Commit, IReadOnlyList<GitHubChangedFile> Files)
{
    /// <inheritdoc cref="GitHubComparison.FilesMayBeTruncated"/>
    public bool FilesMayBeTruncated => Files.Count >= GitHubBranchComparison.FileListLimit;
}

/// <inheritdoc />
public sealed class GitHubBranchComparison(IGitHubTransport transport) : IGitHubBranchComparison
{
    /// <summary>How many files GitHub lists for a comparison or one commit's
    /// first page before it stops.</summary>
    public const int FileListLimit = 300;

    private readonly IGitHubTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public async Task<GitHubComparison> CompareAsync(
        GitHubRepositoryRef repository,
        string baseRef,
        string headRef,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(headRef);

        // Each side escaped on its own, so a branch called release/2.0 stays one
        // segment rather than turning the three dots into a path.
        var payload = await _transport.SendAsync(
            HttpMethod.Get,
            $"repos/{repository.Owner}/{repository.Name}/compare/{Uri.EscapeDataString(baseRef.Trim())}...{Uri.EscapeDataString(headRef.Trim())}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var mergeBase = payload.ValueKind is JsonValueKind.Object
                        && payload.TryGetProperty("merge_base_commit", out var baseCommit)
            ? String(baseCommit, "sha")
            : null;

        if (string.IsNullOrWhiteSpace(mergeBase))
        {
            throw new GitHubException($"GitHub did not return a comparison of {baseRef} and {headRef} in {repository.FullName}.");
        }

        var commits = Array(payload, "commits").Select(ReadCommit).OfType<GitHubComparedCommit>().ToList();

        return new GitHubComparison(
            mergeBase,
            Int(payload, "ahead_by"),
            Int(payload, "behind_by"),
            Math.Max(Int(payload, "total_commits"), commits.Count),
            commits,
            ReadFiles(payload, rootCommit: false));
    }

    public async Task<GitHubCommitChange> ReadCommitAsync(
        GitHubRepositoryRef repository,
        string commitSha,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);

        var payload = await _transport.SendAsync(
            HttpMethod.Get,
            $"repos/{repository.Owner}/{repository.Name}/commits/{Uri.EscapeDataString(commitSha.Trim())}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (ReadCommit(payload) is not { } commit)
        {
            throw new GitHubException($"GitHub did not return commit {commitSha} in {repository.FullName}.");
        }

        return new GitHubCommitChange(commit, ReadFiles(payload, rootCommit: commit.ParentSha is null));
    }

    private static GitHubComparedCommit? ReadCommit(JsonElement element)
    {
        var sha = String(element, "sha");
        if (string.IsNullOrWhiteSpace(sha)) return null;

        var parent = Array(element, "parents").Select(p => String(p, "sha")).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

        var details = element.TryGetProperty("commit", out var commit) ? commit : default;
        var message = String(details, "message") ?? string.Empty;
        var author = details.ValueKind is JsonValueKind.Object && details.TryGetProperty("author", out var a) ? a : default;

        return new GitHubComparedCommit(
            sha.Trim(),
            parent?.Trim(),
            Subject(message),
            String(author, "name"),
            Date(String(author, "date")));
    }

    /// <summary>
    /// The changed files. A root commit has nothing before it, so every file it
    /// holds is added whatever GitHub wrote on the row; a removed file ends at no
    /// blob, whatever id GitHub put beside it.
    /// </summary>
    private static List<GitHubChangedFile> ReadFiles(JsonElement payload, bool rootCommit)
    {
        var files = new List<GitHubChangedFile>();

        foreach (var element in Array(payload, "files"))
        {
            var path = String(element, "filename");
            if (string.IsNullOrWhiteSpace(path)) continue;

            var status = rootCommit ? GitHubFileStatus.Added : Status(String(element, "status"));
            var previous = status is GitHubFileStatus.Renamed ? String(element, "previous_filename") : null;
            var blob = status is GitHubFileStatus.Removed ? null : String(element, "sha");

            files.Add(new GitHubChangedFile(
                path,
                string.IsNullOrWhiteSpace(previous) ? null : previous,
                status,
                string.IsNullOrWhiteSpace(blob) || blob.All(c => c == '0') ? null : blob));
        }

        return files;
    }

    /// <summary>GitHub's statuses folded to four. <c>copied</c> is a new file
    /// as far as either side's text is concerned; <c>changed</c> and
    /// <c>unchanged</c> (a mode change) read as modified.</summary>
    private static GitHubFileStatus Status(string? status) => status switch
    {
        "added" or "copied" => GitHubFileStatus.Added,
        "removed" => GitHubFileStatus.Removed,
        "renamed" => GitHubFileStatus.Renamed,
        _ => GitHubFileStatus.Modified
    };

    private static string Subject(string message)
    {
        var end = message.IndexOfAny(['\r', '\n']);
        return (end < 0 ? message : message[..end]).Trim();
    }

    private static DateTimeOffset? Date(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : null;

    private static IEnumerable<JsonElement> Array(JsonElement element, string property) =>
        element.ValueKind is JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    private static int Int(JsonElement element, string property) =>
        element.ValueKind is JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : 0;

    private static string? String(JsonElement element, string property) =>
        element.ValueKind is JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}
