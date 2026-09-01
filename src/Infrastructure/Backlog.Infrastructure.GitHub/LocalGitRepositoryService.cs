using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Backlog.Infrastructure.GitHub;

public interface ILocalGitRepositoryService
{
    LocalGitRepositoryStatus GetStatus(GitHubRepositoryRef repository, string? cloneDirectory);

    Task<LocalGitRepositoryCloneResult> CloneAsync(
        GitHubRepositoryRef repository,
        string? cloneDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ask the remote whether the clone is still on the latest version.
    /// <para>
    /// This contacts the network — it has to, because nothing on disk knows what
    /// the remote has moved on to — so it runs only when somebody asks. Nothing
    /// in the product polls it.
    /// </para>
    /// </summary>
    Task<LocalGitRepositoryUpdateCheck> CheckForUpdatesAsync(
        GitHubRepositoryRef repository,
        string? cloneDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bring the clone up to the latest version, fast-forward only.
    /// <para>
    /// Refuses rather than improvises: local changes, a branch tracking nothing,
    /// and a history that has diverged all come back as a failed result carrying
    /// the reason, because every one of them is a decision for the person whose
    /// clone it is rather than for this adapter.
    /// </para>
    /// </summary>
    Task<LocalGitRepositoryPullResult> PullAsync(
        GitHubRepositoryRef repository,
        string? cloneDirectory,
        CancellationToken cancellationToken = default);
}

public sealed class LocalGitRepositoryService : ILocalGitRepositoryService
{
    public LocalGitRepositoryStatus GetStatus(GitHubRepositoryRef repository, string? cloneDirectory)
    {
        var path = CleanPath(cloneDirectory);
        if (path is null)
        {
            return new LocalGitRepositoryStatus(
                path,
                IsCloned: false,
                CanClone: false,
                Summary: "No local clone directory configured yet.");
        }

        if (IsGitRepository(path))
        {
            var origin = GetOriginUrl(path);
            if (IsRepositoryOrigin(repository, origin))
            {
                return new LocalGitRepositoryStatus(
                    path,
                    IsCloned: true,
                    CanClone: false,
                    Summary: $"Local clone is ready: {path}");
            }

            return new LocalGitRepositoryStatus(
                path,
                IsCloned: false,
                CanClone: false,
                Summary: $"Not cloned: {path} is a git clone, but its origin is not {repository.FullName}.");
        }

        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            return new LocalGitRepositoryStatus(
                path,
                IsCloned: false,
                CanClone: false,
                Summary: $"Not cloned: {path} exists but is not a git clone. Pick an empty folder or an existing clone.");
        }

        return new LocalGitRepositoryStatus(
            path,
            IsCloned: false,
            CanClone: true,
            Summary: $"Not cloned yet: {path}");
    }

    public async Task<LocalGitRepositoryCloneResult> CloneAsync(
        GitHubRepositoryRef repository,
        string? cloneDirectory,
        CancellationToken cancellationToken = default)
    {
        var status = GetStatus(repository, cloneDirectory);
        if (status.CloneDirectory is null)
        {
            return LocalGitRepositoryCloneResult.Failed(status.Summary);
        }

        if (status.IsCloned)
        {
            return LocalGitRepositoryCloneResult.Succeeded(status.Summary, status.CloneDirectory);
        }

        if (!status.CanClone)
        {
            return LocalGitRepositoryCloneResult.Failed(status.Summary);
        }

        var parent = Path.GetDirectoryName(status.CloneDirectory);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var result = await RunGitAsync(["clone", repository.Url, status.CloneDirectory], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0)
        {
            return LocalGitRepositoryCloneResult.Succeeded($"Cloned {repository.FullName} to {status.CloneDirectory}.", status.CloneDirectory);
        }

        var details = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        return LocalGitRepositoryCloneResult.Failed(string.IsNullOrWhiteSpace(details)
            ? $"git clone failed with exit code {result.ExitCode}."
            : details.Trim());
    }

    public async Task<LocalGitRepositoryUpdateCheck> CheckForUpdatesAsync(
        GitHubRepositoryRef repository,
        string? cloneDirectory,
        CancellationToken cancellationToken = default)
    {
        var path = CleanPath(cloneDirectory);
        if (path is null) return LocalGitRepositoryUpdateCheck.Undetermined("No local clone directory configured yet.");

        // Deliberately IsGitRepository rather than GetStatus().IsCloned: what can
        // be pulled is the clone the folders are actually read out of, and whether
        // its origin is the repository somebody typed into settings is a different
        // question, answered elsewhere and reported elsewhere. Refusing to check a
        // real clone because its remote is spelled unexpectedly would leave the
        // person no way to find out they are behind.
        if (!IsGitRepository(path)) return LocalGitRepositoryUpdateCheck.Undetermined($"There is no git clone at {path} to check.");

        var upstream = await ReadUpstreamAsync(path, cancellationToken).ConfigureAwait(false);
        if (upstream.Name is null) return upstream.Failure!;

        var fetch = await RunGitAsync(["-C", path, "fetch", "--quiet"], cancellationToken).ConfigureAwait(false);
        if (fetch.ExitCode != 0)
        {
            return LocalGitRepositoryUpdateCheck.Undetermined($"Could not reach the remote for {repository.FullName}. {Details(fetch)}");
        }

        return await MeasureAsync(path, upstream.Name, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalGitRepositoryPullResult> PullAsync(
        GitHubRepositoryRef repository,
        string? cloneDirectory,
        CancellationToken cancellationToken = default)
    {
        var path = CleanPath(cloneDirectory);
        if (path is null) return LocalGitRepositoryPullResult.Failed("No local clone directory configured yet.");
        if (!IsGitRepository(path)) return LocalGitRepositoryPullResult.Failed($"There is no git clone at {path} to pull into.");

        if (await HasLocalChangesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            return LocalGitRepositoryPullResult.Failed(
                $"There are local changes in {path}. Commit or discard them before pulling the latest version.");
        }

        // --ff-only is the flag the rest of the product already pulls with, and it
        // is what makes this safe to offer as one button: a history that has
        // diverged is refused by git itself, with git's own explanation, instead
        // of being merged by a screen that was never asked to merge anything.
        var pull = await RunGitAsync(["-C", path, "pull", "--ff-only"], cancellationToken).ConfigureAwait(false);
        if (pull.ExitCode != 0) return LocalGitRepositoryPullResult.Failed(Details(pull));

        // Measured again, but without a second fetch: the refs that were just
        // fetched are the newest anybody has seen, so the answer is on disk.
        var upstream = await ReadUpstreamAsync(path, cancellationToken).ConfigureAwait(false);
        var state = upstream.Name is null
            ? upstream.Failure!
            : await MeasureAsync(path, upstream.Name, cancellationToken).ConfigureAwait(false);

        return LocalGitRepositoryPullResult.Succeeded($"Pulled the latest {repository.FullName} into {path}.", state);
    }

    /// <summary>
    /// How far apart the clone and its upstream are, from refs already on disk.
    /// </summary>
    private static async Task<LocalGitRepositoryUpdateCheck> MeasureAsync(
        string path,
        string upstream,
        CancellationToken cancellationToken)
    {
        var counts = await RunGitAsync(["-C", path, "rev-list", "--left-right", "--count", "HEAD...@{upstream}"], cancellationToken).ConfigureAwait(false);
        if (counts.ExitCode != 0 || !TryReadCounts(counts.StandardOutput, out var ahead, out var behind))
        {
            return LocalGitRepositoryUpdateCheck.Undetermined($"Could not compare this clone with {upstream}. {Details(counts)}");
        }

        var hasLocalChanges = await HasLocalChangesAsync(path, cancellationToken).ConfigureAwait(false);

        return (ahead, behind) switch
        {
            (0, 0) => new LocalGitRepositoryUpdateCheck(
                LocalGitRepositoryCurrency.UpToDate, 0, 0, hasLocalChanges, upstream,
                $"On the latest version of {upstream}."),

            (0, _) when hasLocalChanges => new LocalGitRepositoryUpdateCheck(
                LocalGitRepositoryCurrency.Behind, 0, behind, true, upstream,
                $"{Commits(behind)} behind {upstream}, but there are local changes in the clone. Commit or discard them first."),

            (0, _) => new LocalGitRepositoryUpdateCheck(
                LocalGitRepositoryCurrency.Behind, 0, behind, false, upstream,
                $"{Commits(behind)} behind {upstream}."),

            (_, 0) => new LocalGitRepositoryUpdateCheck(
                LocalGitRepositoryCurrency.Ahead, ahead, 0, hasLocalChanges, upstream,
                $"{Commits(ahead)} ahead of {upstream}, with nothing to pull."),

            _ => new LocalGitRepositoryUpdateCheck(
                LocalGitRepositoryCurrency.Diverged, ahead, behind, hasLocalChanges, upstream,
                $"This clone has diverged from {upstream}: {Commits(ahead)} here that the remote does not have, and {behind} the other way. Resolve it in git.")
        };
    }

    /// <summary>
    /// The upstream branch the clone tracks, or the check that explains why the
    /// question has no answer.
    /// </summary>
    private static async Task<UpstreamLookup> ReadUpstreamAsync(string path, CancellationToken cancellationToken)
    {
        var upstream = await RunGitAsync(
            ["-C", path, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"],
            cancellationToken).ConfigureAwait(false);

        if (upstream.ExitCode == 0 && !string.IsNullOrWhiteSpace(upstream.StandardOutput))
        {
            return new UpstreamLookup(upstream.StandardOutput.Trim(), null);
        }

        // Two different situations arrive as the same failure, and they read
        // differently to the person holding the clone: a branch that tracks
        // nothing, and a checkout that is not on a branch at all.
        var branch = await RunGitAsync(["-C", path, "symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken).ConfigureAwait(false);

        return branch.ExitCode == 0 && !string.IsNullOrWhiteSpace(branch.StandardOutput)
            ? new UpstreamLookup(null, new LocalGitRepositoryUpdateCheck(
                LocalGitRepositoryCurrency.NoUpstream, 0, 0, false, null,
                $"Branch {branch.StandardOutput.Trim()} tracks no remote branch, so there is no latest version to compare against."))
            : new UpstreamLookup(null, new LocalGitRepositoryUpdateCheck(
                LocalGitRepositoryCurrency.Detached, 0, 0, false, null,
                "This clone is not on a branch, so there is no latest version to compare against."));
    }

    private static async Task<bool> HasLocalChangesAsync(string path, CancellationToken cancellationToken)
    {
        var status = await RunGitAsync(["-C", path, "status", "--porcelain"], cancellationToken).ConfigureAwait(false);
        return status.ExitCode == 0 && !string.IsNullOrWhiteSpace(status.StandardOutput);
    }

    /// <summary>
    /// Reads the two numbers <c>rev-list --left-right --count</c> prints. Left is
    /// HEAD's side of the three-dot range, so left is ahead and right is behind.
    /// </summary>
    private static bool TryReadCounts(string output, out int ahead, out int behind)
    {
        ahead = 0;
        behind = 0;

        var fields = output.Split(['\t', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        return fields.Length >= 2
            && int.TryParse(fields[0], out ahead)
            && int.TryParse(fields[1], out behind);
    }

    private static string Commits(int count) => count == 1 ? "1 commit" : $"{count} commits";

    private static string Details(GitCommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        return string.IsNullOrWhiteSpace(details) ? $"git exited with code {result.ExitCode}." : details.Trim();
    }

    private static bool IsGitRepository(string path) =>
        Directory.Exists(path)
        && (Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git")));

    private sealed record UpstreamLookup(string? Name, LocalGitRepositoryUpdateCheck? Failure);

    private static string? CleanPath(string? path) => string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    private static string? GetOriginUrl(string path)
    {
        var result = RunGit(["-C", path, "remote", "get-url", "origin"]);
        return result.ExitCode == 0 ? result.StandardOutput.Trim() : ReadOriginUrlFromConfig(path);
    }

    private static string? ReadOriginUrlFromConfig(string path)
    {
        var configPath = Path.Combine(path, ".git", "config");
        if (!File.Exists(configPath)) return null;

        var inOrigin = false;
        foreach (var line in File.ReadLines(configPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                inOrigin = trimmed.Equals("[remote \"origin\"]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inOrigin && trimmed.StartsWith("url", StringComparison.OrdinalIgnoreCase))
            {
                var separator = trimmed.IndexOf('=', StringComparison.Ordinal);
                if (separator >= 0) return trimmed[(separator + 1)..].Trim();
            }
        }

        return null;
    }

    private static bool IsRepositoryOrigin(GitHubRepositoryRef repository, string? origin) =>
        string.Equals(NormalizeGitHubRemote(origin), NormalizeGitHubRemote(repository.Url), StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeGitHubRemote(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return null;

        var normalized = origin.Trim().Replace('\\', '/');
        if (normalized.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "https://github.com/" + normalized["git@github.com:".Length..];
        }
        else if (normalized.StartsWith("ssh://git@github.com/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "https://github.com/" + normalized["ssh://git@github.com/".Length..];
        }

        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.TrimEnd('/');
    }

    private static GitCommandResult RunGit(IReadOnlyList<string> arguments)
    {
        var startInfo = CreateGitStartInfo(arguments);

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("git could not be started.");

            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new GitCommandResult(process.ExitCode, standardOutput, standardError);
        }
        catch (Win32Exception ex)
        {
            return new GitCommandResult(1, string.Empty, $"git could not be started: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return new GitCommandResult(1, string.Empty, ex.Message);
        }
    }

    private static async Task<GitCommandResult> RunGitAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = CreateGitStartInfo(arguments);

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("git could not be started.");

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new GitCommandResult(process.ExitCode, await standardOutput.ConfigureAwait(false), await standardError.ConfigureAwait(false));
        }
        catch (Win32Exception ex)
        {
            return new GitCommandResult(1, string.Empty, $"git could not be started: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return new GitCommandResult(1, string.Empty, ex.Message);
        }
    }

    private static ProcessStartInfo CreateGitStartInfo(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);
}

public sealed record LocalGitRepositoryStatus(
    string? CloneDirectory,
    bool IsCloned,
    bool CanClone,
    string Summary);

public sealed record LocalGitRepositoryCloneResult(
    bool Success,
    string Message,
    string? CloneDirectory)
{
    public static LocalGitRepositoryCloneResult Succeeded(string message, string cloneDirectory) => new(true, message, cloneDirectory);

    public static LocalGitRepositoryCloneResult Failed(string message) => new(false, message, null);
}

/// <summary>
/// How a local clone stands against the remote it tracks.
/// <para>
/// Seven states rather than a bool, because "not the latest version" is several
/// different situations and only one of them can be fixed by pulling. A screen
/// that only knew "stale" would offer the same button to a clone that is behind,
/// a clone that is ahead, and a clone that is not on a branch at all.
/// </para>
/// </summary>
public enum LocalGitRepositoryCurrency
{
    /// <summary>The question could not be answered — no clone, or the remote could not be reached.</summary>
    Unknown,

    /// <summary>The clone has everything the remote has.</summary>
    UpToDate,

    /// <summary>The remote has commits this clone does not. The one state a pull fixes.</summary>
    Behind,

    /// <summary>This clone has commits the remote does not, and nothing to pull.</summary>
    Ahead,

    /// <summary>Both sides have commits the other does not.</summary>
    Diverged,

    /// <summary>The checked-out branch tracks no remote branch.</summary>
    NoUpstream,

    /// <summary>The clone is not on a branch.</summary>
    Detached
}

/// <summary>
/// What asking the remote turned up. <see cref="Summary"/> is a finished sentence
/// for the person reading it, built here rather than in a screen, so every host
/// that shows this says the same thing about the same clone.
/// </summary>
public sealed record LocalGitRepositoryUpdateCheck(
    LocalGitRepositoryCurrency Currency,
    int Ahead,
    int Behind,
    bool HasLocalChanges,
    string? Upstream,
    string Summary)
{
    /// <summary>
    /// Whether a fast-forward pull would actually work. Local changes disqualify
    /// a clone that is otherwise plainly behind, because the pull would fail on
    /// them and a button that cannot work should not be offered.
    /// </summary>
    public bool CanPull => Currency is LocalGitRepositoryCurrency.Behind && !HasLocalChanges;

    public static LocalGitRepositoryUpdateCheck Undetermined(string summary) =>
        new(LocalGitRepositoryCurrency.Unknown, 0, 0, false, null, summary);
}

/// <summary>
/// The outcome of a fast-forward pull, with the clone's state after it where one
/// could be measured — measured from refs already on disk, so it costs nothing.
/// </summary>
public sealed record LocalGitRepositoryPullResult(
    bool Success,
    string Message,
    LocalGitRepositoryUpdateCheck? State)
{
    public static LocalGitRepositoryPullResult Succeeded(string message, LocalGitRepositoryUpdateCheck? state) => new(true, message, state);

    public static LocalGitRepositoryPullResult Failed(string message) => new(false, message, null);
}
