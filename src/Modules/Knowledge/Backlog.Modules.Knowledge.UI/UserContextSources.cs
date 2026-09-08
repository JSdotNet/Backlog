namespace Backlog.Desktop.UI.Knowledge;

/// <summary>Whether a user-level location is there, and whether it holds
/// anything. Absent and empty are different claims and the view says so: a
/// location nobody created is not the same as one somebody emptied.</summary>
public enum UserContextState
{
    /// <summary>Not on this machine.</summary>
    Absent,

    /// <summary>There, and holding nothing.</summary>
    Empty,

    /// <summary>There, with content that is loaded.</summary>
    Present,

    /// <summary>There, and this app could not read it.</summary>
    Unreadable
}

/// <summary>One place a host loads context from that is not in the repository.</summary>
public sealed record UserContextSource(
    string Host,
    string Label,
    string Path,
    UserContextState State,
    int Files,
    long Bytes)
{
    public long EstimatedTokens => (long)Math.Round(Bytes / 4d, MidpointRounding.AwayFromZero);
}

/// <summary>
/// The context each host loads from the machine rather than from the clone.
///
/// <para>This is the half of the answer the repository cannot give. Everything
/// the comparison beside it measures is checked in and the same for everybody;
/// what is read out of a home directory is neither, and it is loaded into every
/// session all the same. On the machine this was written for the per-project
/// memory file was larger than <c>CLAUDE.md</c>, so about half of what Claude
/// Code carried here was invisible to a reader looking only at the
/// repository.</para>
///
/// <para>Probing only. Well-known locations are looked at and their size taken;
/// nothing is read out of them and nothing is shown but a path and a size. The
/// question is what a host loads and how much of it, never what any of it
/// says.</para>
/// </summary>
public static class UserContextSources
{
    private const string ClaudeCode = "Claude Code";
    private const string Copilot = "GitHub Copilot";

    /// <summary>
    /// Every known user-level location, whether or not it exists.
    ///
    /// <para>Absent locations are returned rather than filtered out, because "your
    /// user-level file is not there" is the finding a reader most often comes for,
    /// and a list that quietly omitted it would read as though the question had
    /// never been asked.</para>
    /// </summary>
    /// <param name="home">The home directory to probe. Left out it is this
    /// machine's, which is the only answer the product wants; named, a test can
    /// put a folder in front of it, because every location below is otherwise
    /// whatever the machine running the suite happens to have.</param>
    public static IReadOnlyList<UserContextSource> Read(string? repositoryRoot, string? home = null)
    {
        home ??= Home();

        if (string.IsNullOrWhiteSpace(home)) return [];

        return
        [
            ReadFile(ClaudeCode, "User instructions", System.IO.Path.Combine(home, ".claude", "CLAUDE.md")),
            ReadFolder(ClaudeCode, "User rules", System.IO.Path.Combine(home, ".claude", "rules"), "*.md"),
            ReadMemory(home, repositoryRoot),
            ReadFolder(Copilot, "User prompt files", VsCodeUserFolder(home, "prompts"), "*.instructions.md")
        ];
    }

    /// <summary>What every host carries from this machine, per host.</summary>
    public static InstructionLoadTotals Total(IEnumerable<UserContextSource> sources, string host)
    {
        var loaded = sources
            .Where(source => source.State == UserContextState.Present && string.Equals(source.Host, host, StringComparison.Ordinal))
            .ToList();

        return loaded.Count == 0
            ? InstructionLoadTotals.None
            : new InstructionLoadTotals(loaded.Sum(source => source.Files), 0, loaded.Sum(source => source.Bytes));
    }

    /// <summary>
    /// Claude Code's own notes about this project, kept per project outside the
    /// clone.
    ///
    /// <para>The folder is named after the project path with its separators
    /// flattened, so the name is derived rather than looked up. Every ancestor of
    /// the clone is tried and not the clone alone, because a worktree sits several
    /// folders below the repository it belongs to while the memory stays filed
    /// under the repository — checking only the clone would report "not found"
    /// from inside every worktree, while that same memory was being loaded into
    /// the session doing the checking.</para>
    ///
    /// <para><b>The index only, not the folder.</b> <c>MEMORY.md</c> is what a
    /// session reads every time; the notes beside it are recalled when something
    /// makes them relevant and are not carried otherwise. Measuring the folder
    /// counted sixty files and 124 KB here against an index of 11 KB, and put a
    /// number on screen that overstated what Claude Code actually carries by more
    /// than tenfold. A figure that confident has to be about something real.</para>
    /// </summary>
    private static UserContextSource ReadMemory(string home, string? repositoryRoot)
    {
        var projects = System.IO.Path.Combine(home, ".claude", "projects");

        foreach (var candidate in Ancestors(repositoryRoot))
        {
            var folder = System.IO.Path.Combine(projects, Slug(candidate), "memory");

            if (Directory.Exists(folder)) return ReadFile(ClaudeCode, "Project memory", System.IO.Path.Combine(folder, "MEMORY.md"));
        }

        return new UserContextSource(ClaudeCode, "Project memory", projects, UserContextState.Absent, 0, 0);
    }

    private static IEnumerable<string> Ancestors(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) yield break;

        DirectoryInfo? directory;

        try
        {
            directory = new DirectoryInfo(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            yield break;
        }

        while (directory is not null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }

    /// <summary>The folder name a project's memory is filed under: the full path
    /// with everything that is not a letter, a digit or a dash turned into a
    /// dash.</summary>
    private static string Slug(string path) =>
        string.Concat(path.Select(character => char.IsLetterOrDigit(character) || character == '-' ? character : '-'));

    private static UserContextSource ReadFile(string host, string label, string path)
    {
        try
        {
            var info = new FileInfo(path);

            if (!info.Exists) return new UserContextSource(host, label, path, UserContextState.Absent, 0, 0);

            return info.Length == 0
                ? new UserContextSource(host, label, path, UserContextState.Empty, 1, 0)
                : new UserContextSource(host, label, path, UserContextState.Present, 1, info.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new UserContextSource(host, label, path, UserContextState.Unreadable, 0, 0);
        }
    }

    private static UserContextSource ReadFolder(string host, string label, string? path, string pattern)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new UserContextSource(host, label, "not known on this platform", UserContextState.Absent, 0, 0);
        }

        try
        {
            if (!Directory.Exists(path)) return new UserContextSource(host, label, path, UserContextState.Absent, 0, 0);

            var files = Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories)
                .Select(file => new FileInfo(file))
                .ToList();

            return files.Count == 0
                ? new UserContextSource(host, label, path, UserContextState.Empty, 0, 0)
                : new UserContextSource(host, label, path, UserContextState.Present, files.Count, files.Sum(file => file.Length));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new UserContextSource(host, label, path, UserContextState.Unreadable, 0, 0);
        }
    }

    /// <summary>Where the editor keeps a user's own profile files: a different
    /// folder on each platform, and no folder at all where the editor was never
    /// installed.
    /// <para>Built from the home directory on every platform, Windows included,
    /// rather than from <c>%APPDATA%</c>. The two are the same folder on a default
    /// install, and reading the environment variable instead would make this one
    /// row of the table the only one a caller could not point somewhere else —
    /// which is to say the only one no test could check.</para></summary>
    private static string VsCodeUserFolder(string home, string leaf)
    {
        if (OperatingSystem.IsWindows()) return System.IO.Path.Combine(home, "AppData", "Roaming", "Code", "User", leaf);

        return OperatingSystem.IsMacOS()
            ? System.IO.Path.Combine(home, "Library", "Application Support", "Code", "User", leaf)
            : System.IO.Path.Combine(home, ".config", "Code", "User", leaf);
    }

    private static string? Home() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } profile
            ? profile
            : Environment.GetEnvironmentVariable("HOME");
}
