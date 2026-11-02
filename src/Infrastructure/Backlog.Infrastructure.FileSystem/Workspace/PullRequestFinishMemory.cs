using System.Text.Json;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The disk half of <see cref="IPullRequestFinishMemory"/>: one small JSON file per
/// pull request, under one folder per repository.
/// <para>
/// The folder and the files the task list's outcome memory kept before Repository
/// Management took the merge itself over (local ADR 0042 §3), read as they are: their
/// <c>settledEntries</c> are this memory's whole answer. The read time and branches beside
/// them are passed over and written back unchanged, and the outcome is written as merged —
/// settling is only done for a merge — so no entry settled before the move is moved to
/// Done again, and a build from before it reading the same shared folder reads the merge
/// it settled. The folder and every property name are
/// persisted identifiers, never renamed with this class.
/// </para>
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
public sealed class PullRequestFinishMemory(Func<string> root) : IPullRequestFinishMemory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The shape stored on disk, the outcome memory's last. A file of any other
    /// version is a miss: a 1 has no settled entries, and reading it as "no entry
    /// settled" would move an entry the person had already taken back out of Done.
    /// </summary>
    private const int Version = 2;

    /// <summary>What every file this memory writes says ended: settling is only ever done
    /// for a merge, and a build from before the move reads the file as one.</summary>
    private const string MergedOutcome = "Merged";

    /// <summary>The folder under the activity cache this memory keeps to — the outcome
    /// memory's, unchanged. Not a repository folder name: those end in a digest.</summary>
    public const string FolderName = "pull-request-states";

    private readonly Func<string> _root = root ?? throw new ArgumentNullException(nameof(root));

    public IReadOnlyList<PullRequestFinish> ReadAll()
    {
        var finishes = new List<PullRequestFinish>();

        string[] files;
        try
        {
            var root = _root();
            if (!Directory.Exists(root)) return finishes;
            files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return finishes;
        }

        foreach (var file in files)
        {
            if (TryRead(file) is { } stored)
            {
                finishes.Add(new PullRequestFinish(stored.Repository!, stored.Number, stored.SettledEntries ?? []));
            }
        }

        return finishes;
    }

    public void Settle(string repository, int number, Guid entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);

        var path = EntryPath(repository, number);
        var known = TryRead(path);
        var settled = known?.SettledEntries ?? [];
        if (settled.Contains(entry)) return;

        var stored = (known ?? new StoredPullRequest
        {
            Version = Version,
            Repository = repository.Trim(),
            Number = number
        }) with
        {
            // Merged whatever the file said: settling is only done for a merge, and a
            // build from before the move reading "Closed" here would read it again, find
            // it merged, and rewrite the file without its settled entries.
            Outcome = MergedOutcome,
            SettledEntries = [.. settled, entry]
        };

        var written = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(written, JsonSerializer.Serialize(stored, JsonOptions));
            File.Move(written, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not kept: the next launch owes the entry the move once more, and the
            // entry's own status — Done already — is what stops it moving twice.
            TryDelete(written);
        }
    }

    /// <summary>One file as stored, or null when it is missing, half-written, of another
    /// version, or names no repository.</summary>
    private static StoredPullRequest? TryRead(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;

            var stored = JsonSerializer.Deserialize<StoredPullRequest>(File.ReadAllText(file), JsonOptions);

            return stored is null || stored.Version != Version || string.IsNullOrWhiteSpace(stored.Repository)
                ? null
                : stored;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // Half-written or unreadable: a miss, so one corrupt file never costs the
            // rest of what is kept.
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
            // Left behind: a temporary file nothing reads.
        }
    }

    private string EntryPath(string repository, int number)
    {
        var key = repository.Trim().ToLowerInvariant();
        return Path.Combine(_root(), CachePaths.Safe(key.Replace('/', '-'), key), $"{number}.json");
    }

    /// <summary>The stored shape — the outcome memory's, every property name kept, so a
    /// file either build wrote reads in the other. Only <see cref="SettledEntries"/> is
    /// this memory's; the rest is carried as it was found.</summary>
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
