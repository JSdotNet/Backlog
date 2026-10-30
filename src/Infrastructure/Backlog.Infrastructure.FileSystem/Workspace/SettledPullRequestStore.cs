using System.Text.Json;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The disk half of <see cref="ISettledPullRequestStore"/>: Repository Management's
/// merged pull requests, remembered across a restart in app storage and never in a
/// repository (local ADR 0004, 0015, 0042 §3).
/// <para>
/// Laid out as <see cref="PullRequestStateMemory"/> lays out its files — one small JSON
/// file per pull request, under one folder per repository named in lower case —
/// because the folder is shared by every process on the machine that runs the app, and
/// one file per repository rewritten by each would lose the other's entry. Each file is
/// written beside its final name and moved over it, so a reader never sees half of one.
/// </para>
/// <para>
/// Only the fields of <see cref="SettledPullRequest"/> are written: where it merged,
/// when, what a reader draws it with, and the head branch and closing issues its task
/// and session are found by. Never a check, a review, auto-merge, mergeability or how
/// far behind — a stale one would offer a merge GitHub no longer allows.
/// </para>
/// </summary>
public sealed class SettledPullRequestStore(Func<string> root) : ISettledPullRequestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The shape stored on disk, bumped whenever a field is added, removed or
    /// changes meaning. A file of a version not listed in <see cref="Readable"/> is a
    /// miss: it costs one read of that pull request.
    /// <para>
    /// 2 added the head branch, the closing issues and the labels. A 1 lacks them and is
    /// still read — every other field means what it meant — so the pull request draws
    /// merged at once, without what only those three find.
    /// </para>
    /// </summary>
    private const int Version = 2;

    private static readonly int[] Readable = [1, Version];

    /// <summary>The folder under the activity cache this store keeps to. Not a repository
    /// folder name: those end in a digest.</summary>
    public const string FolderName = "settled-pull-requests";

    private readonly Func<string> _root = root ?? throw new ArgumentNullException(nameof(root));

    public IReadOnlyList<SettledPullRequest> ReadAll()
    {
        var settled = new List<SettledPullRequest>();

        string[] files;
        try
        {
            var folder = _root();
            if (!Directory.Exists(folder)) return settled;
            files = Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return settled;
        }

        foreach (var file in files)
        {
            if (TryRead(file) is { } pullRequest) settled.Add(pullRequest);
        }

        return settled;
    }

    public void Keep(SettledPullRequest pullRequest)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);

        var stored = new StoredPullRequest
        {
            Version = Version,
            Repository = pullRequest.RepositoryFullName.Trim(),
            Number = pullRequest.Number,
            MergedAt = pullRequest.MergedAt,
            BaseRefName = pullRequest.BaseRefName,
            DefaultBranch = pullRequest.DefaultBranch,
            Title = pullRequest.Title,
            Url = pullRequest.Url,
            Author = pullRequest.AuthorLogin,
            HeadRefName = pullRequest.HeadRefName,
            ClosingIssues = [.. pullRequest.ClosingIssues.Select(issue => new StoredIssue { Repository = issue.RepositoryFullName, Number = issue.Number })],
            Labels = [.. pullRequest.Labels]
        };

        string? written = null;
        try
        {
            var path = EntryPath(pullRequest.RepositoryFullName, pullRequest.Number);
            var json = JsonSerializer.Serialize(stored, JsonOptions);

            // Unchanged: nothing to write, so a pull request read again by another
            // window costs no disk.
            if (File.Exists(path) && string.Equals(File.ReadAllText(path), json, StringComparison.Ordinal)) return;

            written = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(written, json);
            File.Move(written, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not remembered: the next launch reads it from GitHub once more, which is
            // what it did before there was a memory at all.
            if (written is not null) TryDelete(written);
        }
    }

    private static SettledPullRequest? TryRead(string file)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<StoredPullRequest>(File.ReadAllText(file), JsonOptions);

            if (stored is null
                || !Readable.Contains(stored.Version)
                || string.IsNullOrWhiteSpace(stored.Repository)
                || stored.Number <= 0)
            {
                return null;
            }

            return new SettledPullRequest(
                stored.Repository,
                stored.Number,
                stored.MergedAt,
                stored.BaseRefName,
                stored.DefaultBranch,
                stored.Title ?? "",
                stored.Url ?? "",
                stored.Author)
            {
                HeadRefName = stored.HeadRefName ?? "",
                ClosingIssues =
                [
                    .. (stored.ClosingIssues ?? [])
                        .Where(issue => !string.IsNullOrWhiteSpace(issue.Repository) && issue.Number > 0)
                        .Select(issue => new IssueReference(issue.Repository!, issue.Number))
                ],
                Labels = [.. (stored.Labels ?? []).Where(label => !string.IsNullOrEmpty(label))]
            };
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // Half-written or unreadable: a miss, so one corrupt file never costs the
            // rest of what is remembered.
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left behind: a stray temporary file is never read, since it does not end
            // in .json.
        }
    }

    private string EntryPath(string repository, int number)
    {
        var key = repository.Trim().ToLowerInvariant();
        return Path.Combine(_root(), CachePaths.Safe(key.Replace('/', '-'), key), $"{number}.json");
    }

    /// <summary>The stored shape, separate from <see cref="SettledPullRequest"/> so the
    /// file format is this class's decision.</summary>
    private sealed record StoredPullRequest
    {
        public int Version { get; init; }

        public string? Repository { get; init; }

        public int Number { get; init; }

        public DateTimeOffset? MergedAt { get; init; }

        public string? BaseRefName { get; init; }

        public string? DefaultBranch { get; init; }

        public string? Title { get; init; }

        public string? Url { get; init; }

        public string? Author { get; init; }

        public string? HeadRefName { get; init; }

        public List<StoredIssue>? ClosingIssues { get; init; }

        public List<string>? Labels { get; init; }
    }

    private sealed record StoredIssue
    {
        public string? Repository { get; init; }

        public int Number { get; init; }
    }
}
