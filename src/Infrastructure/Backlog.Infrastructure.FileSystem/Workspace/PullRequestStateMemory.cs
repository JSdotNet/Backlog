using System.Text.Json;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The disk half of <see cref="IPullRequestStateMemory"/>: one small JSON file per
/// pull request, under one folder per repository.
/// <para>
/// A file per pull request, as <see cref="PullRequestDetailCache"/> keeps, because the
/// folder is shared by every process on the machine that runs the app — two
/// worktrees' harnesses at once — and one file per repository rewritten by each would
/// lose the other's entry. Each file is written beside its final name and moved over
/// it, so a reader never sees half of one.
/// </para>
/// <para>
/// The repository folder is named after the repository in lower case: GitHub compares
/// owner and name without regard to case, and an entry that recorded
/// <c>jsdotnet/backlog</c> names the same pull request as one that recorded
/// <c>JSdotNet/Backlog</c>.
/// </para>
/// </summary>
public sealed class PullRequestStateMemory(Func<string> root) : IPullRequestStateMemory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The shape stored on disk, bumped whenever a field is added, removed or
    /// changes meaning. A file of any other version is a miss: it costs one read of
    /// that pull request, where a guess at what it meant could draw a link merged
    /// that is not.
    /// <para>
    /// 2 added the branches a merge went into and the entries whose finish on it is
    /// settled. A 1 has neither, and reading it as "no entry settled" would move an
    /// entry the person had already taken back out of Done, so it is a miss.
    /// </para>
    /// </summary>
    private const int Version = 2;

    /// <summary>The folder under the activity cache this memory keeps to. Not a
    /// repository folder name: those end in a digest.</summary>
    public const string FolderName = "pull-request-states";

    private readonly Func<string> _root = root ?? throw new ArgumentNullException(nameof(root));

    public IReadOnlyList<RememberedPullRequest> ReadAll()
    {
        var remembered = new List<RememberedPullRequest>();

        string[] files;
        try
        {
            var root = _root();
            if (!Directory.Exists(root)) return remembered;
            files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return remembered;
        }

        foreach (var file in files)
        {
            if (TryRead(file) is { } pullRequest) remembered.Add(pullRequest);
        }

        return remembered;
    }

    public void Remember(RememberedPullRequest pullRequest)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);

        var path = EntryPath(pullRequest.Repository, pullRequest.Number);
        var written = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(written, JsonSerializer.Serialize(
                new StoredPullRequest
                {
                    Version = Version,
                    Repository = pullRequest.Repository,
                    Number = pullRequest.Number,
                    Outcome = pullRequest.Outcome.ToString(),
                    ReadAt = pullRequest.ReadAt,
                    BaseRefName = pullRequest.BaseRefName,
                    DefaultBranch = pullRequest.DefaultBranch,
                    SettledEntries = [.. pullRequest.SettledEntries]
                },
                JsonOptions));
            File.Move(written, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not remembered: the next launch reads it from GitHub once more, which is
            // what it did before there was a memory at all.
            TryDelete(written);
        }
    }

    public void Forget(string repository, int number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        TryDelete(EntryPath(repository, number));
    }

    private static RememberedPullRequest? TryRead(string file)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<StoredPullRequest>(File.ReadAllText(file), JsonOptions);

            if (stored is null
                || stored.Version != Version
                || string.IsNullOrWhiteSpace(stored.Repository)
                || !Enum.TryParse<PullRequestOutcome>(stored.Outcome, ignoreCase: false, out var outcome)
                || !Enum.IsDefined(outcome))
            {
                return null;
            }

            return new RememberedPullRequest(stored.Repository, stored.Number, outcome, stored.ReadAt)
            {
                BaseRefName = stored.BaseRefName,
                DefaultBranch = stored.DefaultBranch,
                SettledEntries = stored.SettledEntries ?? []
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
            // Left behind: a closed pull request still remembered is asked about again
            // at the recheck interval anyway.
        }
    }

    private string EntryPath(string repository, int number)
    {
        var key = repository.Trim().ToLowerInvariant();
        return Path.Combine(_root(), CachePaths.Safe(key.Replace('/', '-'), key), $"{number}.json");
    }

    /// <summary>The stored shape, separate from <see cref="RememberedPullRequest"/> so
    /// the file format is this class's decision. The outcome is stored by name, so
    /// reordering the enum never changes what a file says.</summary>
    private sealed record StoredPullRequest
    {
        public int Version { get; init; }

        public string? Repository { get; init; }

        public int Number { get; init; }

        public string? Outcome { get; init; }

        public DateTimeOffset ReadAt { get; init; }

        public string? BaseRefName { get; init; }

        public string? DefaultBranch { get; init; }

        public List<Guid>? SettledEntries { get; init; }
    }
}
