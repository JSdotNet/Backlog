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

    private static bool IsGitRepository(string path) =>
        Directory.Exists(path)
        && (Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git")));

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
