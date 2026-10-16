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
    /// the remote has moved on to — so it is asked rarely: once per repository per
    /// session, when the Devbook pane opens on it and loads the branch overview,
    /// and again when somebody presses the version control. Nothing polls it.
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
    /// <summary>
    /// How long the status may wait on git when the config cannot answer. The
    /// status is asked for synchronously, on the UI thread, so a git that hangs
    /// must not hang the screen with it.
    /// </summary>
    private static readonly TimeSpan StatusGitTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The exit code <see cref="RunGit"/> reports when git did not answer in
    /// time, told apart from git's own failures so a timeout is never remembered.
    /// </summary>
    internal const int TimedOutExitCode = -1;

    /// <summary>
    /// Asks git, from wherever the app runs, for every url rewrite and every
    /// conditional include it reads, each with the scope it came from. A rewrite
    /// in the user's or the system's config changes what get-url prints for every
    /// clone, and the repository's own config cannot show it.
    /// </summary>
    private static readonly string[] GlobalRewriteProbe =
        ["config", "--show-scope", "--get-regexp", @"^(url\..*\.insteadof|includeif\..*\.path)$"];

    /// <summary>
    /// Bounds the remembered git answers, since the clone directory is typed into
    /// Settings a keystroke at a time and every prefix that is a clone is a path.
    /// </summary>
    private const int MaxRememberedOrigins = 64;

    private readonly Func<IReadOnlyList<string>, GitCommandResult> _runGit;
    private readonly Func<IReadOnlyList<string>> _globalConfigFiles;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, RememberedOrigin> _rememberedOrigins = new(StringComparer.Ordinal);
    private string? _globalStamp;
    private bool _globalRewritesUrls;

    public LocalGitRepositoryService()
        : this(RunGit)
    {
    }

    /// <summary>
    /// The seam the status's git launches go through, so a test can count them.
    /// Only <see cref="GetStatus"/> uses it; the clone, check and pull paths are
    /// asked for by a person and start git as they always have.
    /// </summary>
    internal LocalGitRepositoryService(Func<IReadOnlyList<string>, GitCommandResult> runGit)
        : this(runGit, GlobalConfigFiles)
    {
    }

    /// <param name="runGit">Runs one git command for the status.</param>
    /// <param name="globalConfigFiles">
    /// The user-level git config files whose last-write times decide when the
    /// global config is asked about url rewrites again.
    /// </param>
    internal LocalGitRepositoryService(
        Func<IReadOnlyList<string>, GitCommandResult> runGit,
        Func<IReadOnlyList<string>> globalConfigFiles)
    {
        ArgumentNullException.ThrowIfNull(runGit);
        ArgumentNullException.ThrowIfNull(globalConfigFiles);
        _runGit = runGit;
        _globalConfigFiles = globalConfigFiles;
    }

    /// <summary>
    /// The user-level config files git reads, where they would be: the one
    /// <c>GIT_CONFIG_GLOBAL</c> names, else <c>~/.gitconfig</c> and the XDG
    /// <c>git/config</c>. Only their last-write times are read, never their content.
    /// The system config and files these include are not watched: a rewrite added
    /// there is seen after a restart or the next write to one of these files.
    /// </summary>
    private static IReadOnlyList<string> GlobalConfigFiles()
    {
        var explicitGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        if (!string.IsNullOrEmpty(explicitGlobal)) return [explicitGlobal];

        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(home)) home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var xdgConfig = string.IsNullOrEmpty(xdg)
            ? Path.Combine(home, ".config", "git", "config")
            : Path.Combine(xdg, "git", "config");

        return [Path.Combine(home, ".gitconfig"), xdgConfig];
    }

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

    /// <summary>
    /// The clone's origin: from its own config when that answers exactly as git
    /// would and no user or system config rewrites urls, else from
    /// <c>git remote get-url origin</c>, and when git cannot say either, from
    /// whatever url the config holds — the answer this gave before git was asked
    /// second rather than first.
    /// <para>
    /// Git's answer is remembered per path until the clone's config, its
    /// <c>.git</c> file or the user's config changes, because Settings asks on
    /// every render and the answer only moves when one of those does.
    /// </para>
    /// </summary>
    private string? GetOriginUrl(string path)
    {
        var config = GitOriginConfig.Read(path);
        var globalStamp = Stamp(_globalConfigFiles());
        if (config.Answered && !GlobalConfigRewritesUrls(globalStamp)) return config.Url;

        var stamp = $"{GitPointerStamp(path)}|{Stamp(config.ConfigPath is null ? [] : [config.ConfigPath])}|{globalStamp}";
        lock (_gate)
        {
            if (_rememberedOrigins.TryGetValue(path, out var remembered) && remembered.Stamp == stamp) return remembered.Url;
        }

        var result = _runGit(["-C", path, "remote", "get-url", "origin"]);
        var url = result.ExitCode == 0 ? result.StandardOutput.Trim() : config.Url;

        if (result.ExitCode != TimedOutExitCode)
        {
            lock (_gate)
            {
                if (_rememberedOrigins.Count >= MaxRememberedOrigins && !_rememberedOrigins.ContainsKey(path)) _rememberedOrigins.Clear();
                _rememberedOrigins[path] = new RememberedOrigin(stamp, url);
            }
        }

        return url;
    }

    /// <summary>
    /// Whether the user's or the system's git config rewrites urls, or includes
    /// a file on a condition that might. Asked once, and again only when a
    /// user-level config file is written. A probe git cannot answer counts as a
    /// rewrite, which only costs the clones a git call each.
    /// </summary>
    private bool GlobalConfigRewritesUrls(string globalStamp)
    {
        lock (_gate)
        {
            if (_globalStamp == globalStamp) return _globalRewritesUrls;
        }

        var result = _runGit(GlobalRewriteProbe);
        var rewrites = result.ExitCode switch
        {
            0 => AnyOutsideTheRepository(result.StandardOutput),
            1 => false,
            _ => true
        };

        // A probe that ran out of time is asked again next call rather than
        // sending every clone in the session to git.
        if (result.ExitCode == TimedOutExitCode) return rewrites;

        lock (_gate)
        {
            _globalStamp = globalStamp;
            _globalRewritesUrls = rewrites;
        }

        return rewrites;
    }

    /// <summary>
    /// <c>--show-scope</c> prefixes each line with where it came from. The app may
    /// run inside a clone of its own, whose local config is that clone's business
    /// and is read for each clone separately.
    /// </summary>
    private static bool AnyOutsideTheRepository(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', 2)[0].Trim())
            .Any(scope => scope.Length > 0
                && !string.Equals(scope, "local", StringComparison.Ordinal)
                && !string.Equals(scope, "worktree", StringComparison.Ordinal));

    /// <summary>
    /// A <c>.git</c> file's last write, since it decides which config is read; a
    /// <c>.git</c> folder is written on every commit and says nothing about the origin.
    /// </summary>
    private static string GitPointerStamp(string path)
    {
        var dotGit = Path.Combine(path, ".git");
        return File.Exists(dotGit) ? Stamp([dotGit]) : "folder";
    }

    private static string Stamp(IReadOnlyList<string> files)
    {
        var parts = new string[files.Count];
        for (var index = 0; index < files.Count; index++)
        {
            long ticks;
            try
            {
                ticks = File.GetLastWriteTimeUtc(files[index]).Ticks;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                ticks = 0;
            }

            parts[index] = $"{files[index]}={ticks}";
        }

        return string.Join('|', parts);
    }

    private sealed record RememberedOrigin(string Stamp, string? Url);

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

    /// <summary>
    /// Runs git for a caller that cannot await, bounded by
    /// <see cref="StatusGitTimeout"/>: past it the process is killed and the call
    /// reads as a failed git, which the status already knows how to answer.
    /// </summary>
    internal static GitCommandResult RunGit(IReadOnlyList<string> arguments)
    {
        var startInfo = CreateGitStartInfo(arguments);

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("git could not be started.");

            var elapsed = Stopwatch.StartNew();
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();

            // One deadline for the exit and the output together: a child that
            // inherited the pipes can hold them open after git itself has exited.
            if (!process.WaitForExit(StatusGitTimeout)
                || !Task.WhenAll(standardOutput, standardError).Wait(Remaining(elapsed)))
            {
                StopQuietly(process);
                return new GitCommandResult(TimedOutExitCode, string.Empty, $"git did not answer within {StatusGitTimeout.TotalSeconds:0} seconds.");
            }

            return new GitCommandResult(process.ExitCode, standardOutput.Result, standardError.Result);
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

    private static TimeSpan Remaining(Stopwatch elapsed)
    {
        var remaining = StatusGitTimeout - elapsed.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static void StopQuietly(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already gone, or not ours to stop: either way there is nothing to wait for.
        }
    }

    internal sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);
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
