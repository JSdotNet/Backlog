using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// Reads what a file in a working tree looked like at the last commit, so a screen
/// can put the edit somebody is about to commit next to what is already there.
/// <para>
/// The caller has an absolute path to a file it is showing and nothing else — it
/// does not know which repository that path belongs to, and on this machine it may
/// well be a worktree whose root is nowhere near the one the app was started from.
/// So resolving the repository is this adapter's job, not the caller's.
/// </para>
/// </summary>
public interface IGitFileHistoryService
{
    Task<GitFileAtRevisionResult> ReadAtHeadAsync(string absoluteFilePath, CancellationToken cancellationToken = default);
}

public sealed class GitFileHistoryService : IGitFileHistoryService
{
    public async Task<GitFileAtRevisionResult> ReadAtHeadAsync(string absoluteFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);

        var fullPath = Path.GetFullPath(absoluteFilePath.Trim());
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return GitFileAtRevisionResult.NotARepository($"{fullPath} is not inside a git working tree.");
        }

        var located = await LocateAsync(directory, cancellationToken).ConfigureAwait(false);
        if (located is null)
        {
            return GitFileAtRevisionResult.NotARepository($"{directory} is not inside a git working tree.");
        }

        var (repositoryRoot, prefix) = located.Value;
        var relativePath = prefix + Path.GetFileName(fullPath);

        // A repository nobody has committed to yet has no HEAD to read. From the
        // caller's side that is the same answer as a brand new file: there is
        // nothing committed to compare against, and that is not a failure.
        var head = await RunGitAsync(["-C", repositoryRoot, "rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken).ConfigureAwait(false);
        if (head.ExitCode != 0 || string.IsNullOrWhiteSpace(head.StandardOutput))
        {
            return GitFileAtRevisionResult.NotTracked();
        }

        // ls-tree answers "is this path in that commit" with an empty line rather
        // than an error, so the new-file case is decided by the shape of the
        // output instead of by matching git's fatal text — which is translated on
        // a localized machine and reworded between versions.
        var listed = await RunGitAsync(["-C", repositoryRoot, "ls-tree", "--name-only", "HEAD", "--", relativePath], cancellationToken).ConfigureAwait(false);
        if (listed.ExitCode != 0)
        {
            return GitFileAtRevisionResult.Failed(Describe(listed, "git ls-tree"));
        }

        if (string.IsNullOrWhiteSpace(listed.StandardOutput))
        {
            return GitFileAtRevisionResult.NotTracked();
        }

        return await ReadBlobAsync(repositoryRoot, relativePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The repository root and the file's folder within it, both straight from git.
    /// <para>
    /// Walking up the tree looking for <c>.git</c> would find the wrong answer in a
    /// worktree, where <c>.git</c> is a file pointing elsewhere, and subtracting the
    /// root from the absolute path afterwards would compare two path spellings that
    /// need not match — git reports the resolved real path with forward slashes,
    /// while the caller's path may run through a junction or a short name. Asking
    /// git for the prefix instead means no path arithmetic happens at all.
    /// </para>
    /// </summary>
    private static async Task<(string Root, string Prefix)?> LocateAsync(string directory, CancellationToken cancellationToken)
    {
        var located = await RunGitAsync(["-C", directory, "rev-parse", "--show-toplevel", "--show-prefix"], cancellationToken).ConfigureAwait(false);
        if (located.ExitCode != 0) return null;

        var lines = located.StandardOutput.Split('\n');
        var root = lines.Length > 0 ? lines[0].Trim() : string.Empty;
        if (root.Length == 0) return null;

        // Empty at the repository root, and always slash-terminated otherwise,
        // which is exactly what a HEAD:<path> revision spec wants.
        var prefix = lines.Length > 1 ? lines[1].Trim() : string.Empty;
        return (root, prefix);
    }

    private static async Task<GitFileAtRevisionResult> ReadBlobAsync(string repositoryRoot, string relativePath, CancellationToken cancellationToken)
    {
        // cat-file hands back the bytes as they are stored. git show would put the
        // path through the same end-of-line and filter machinery a checkout uses,
        // and a diff of a file whose line endings were rewritten on the way out is
        // a diff of git's configuration rather than of the edit. The bytes are read
        // off the raw stream for the same reason: a StreamReader over stdout would
        // be one more place for the text to be reshaped.
        var startInfo = CreateGitStartInfo(["-C", repositoryRoot, "cat-file", "blob", $"HEAD:{relativePath}"]);

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("git could not be started.");

            using var buffer = new MemoryStream();
            var content = process.StandardOutput.BaseStream.CopyToAsync(buffer, cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await content.ConfigureAwait(false);

            var result = new GitCommandResult(process.ExitCode, string.Empty, await standardError.ConfigureAwait(false));
            return result.ExitCode == 0
                ? GitFileAtRevisionResult.Committed(Decode(buffer.ToArray()))
                : GitFileAtRevisionResult.Failed(Describe(result, "git cat-file"));
        }
        catch (Win32Exception ex)
        {
            return GitFileAtRevisionResult.Failed($"git could not be started: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return GitFileAtRevisionResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Committed bytes read as UTF-8 with a leading byte order mark dropped. The
    /// working copy this gets compared against is read the same way, so keeping the
    /// mark would show up as a change to the first line that nobody made.
    /// </summary>
    private static string Decode(byte[] bytes)
    {
        var span = bytes.AsSpan();
        if (span.StartsWith(Encoding.UTF8.Preamble))
        {
            span = span[Encoding.UTF8.Preamble.Length..];
        }

        return Encoding.UTF8.GetString(span);
    }

    private static string Describe(GitCommandResult result, string command)
    {
        var details = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        return string.IsNullOrWhiteSpace(details)
            ? $"{command} failed with exit code {result.ExitCode}."
            : details.Trim();
    }

    // The process plumbing repeats what LocalGitRepositoryService does next door.
    // Hoisting it into a shared runner is worth doing once a third caller wants it;
    // with two, the copy costs less than a helper that both have to bend to.
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
        // UseShellExecute stays off: git is looked up on PATH by the process
        // launcher, never handed to a shell that would reinterpret the arguments.
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

public enum GitFileAtRevisionState
{
    Committed,
    NotTracked,
    NotARepository,
    Failed
}

/// <summary>
/// What git had to say about one file at one revision.
/// <para>
/// The four states are kept apart deliberately. An empty committed file and a file
/// that has never been committed are both "no text", and a compare view has to say
/// something different about each — so <see cref="Content"/> is null for everything
/// except <see cref="GitFileAtRevisionState.Committed"/>, and an empty string there
/// means the committed file really is empty. <see cref="GitFileAtRevisionState.NotTracked"/>
/// is an ordinary state a new file sits in, not a failure; only
/// <see cref="GitFileAtRevisionState.Failed"/> means something went wrong, and it
/// carries what git printed rather than a message of our own invention.
/// </para>
/// </summary>
public sealed record GitFileAtRevisionResult(
    GitFileAtRevisionState State,
    string? Content,
    string? Message)
{
    public bool HasCommittedContent => State == GitFileAtRevisionState.Committed;

    public static GitFileAtRevisionResult Committed(string content) => new(GitFileAtRevisionState.Committed, content, null);

    public static GitFileAtRevisionResult NotTracked() => new(GitFileAtRevisionState.NotTracked, null, null);

    public static GitFileAtRevisionResult NotARepository(string message) => new(GitFileAtRevisionState.NotARepository, null, message);

    public static GitFileAtRevisionResult Failed(string message) => new(GitFileAtRevisionState.Failed, null, message);
}
