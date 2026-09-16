using System.IO.Enumeration;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// How the instructions area walks a repository: which directories it never
/// enters, and an enumeration that turns back at them.
/// <para>
/// Discovery and the path picker both walk the clone and both skip the same
/// folders — version control, editor state, build output, dependencies, and
/// Claude Code's own worktrees, each of which is a whole second checkout. They
/// used to skip them differently. The picker pruned as it descended; discovery
/// asked <c>Directory.EnumerateFiles</c> for <c>AllDirectories</c> and threw
/// the excluded results away afterwards, which is a filter on the answer and
/// not on the walk. On this repository that walk visited seventy thousand
/// directories to keep the files in four hundred, three times per load, on the
/// thread that draws the pane. Pruning where the picker already pruned brings it
/// back to the four hundred.
/// </para>
/// <para>
/// One list, one predicate, both walkers: the two cannot disagree about what a
/// repository is if only one of them says.
/// </para>
/// </summary>
internal static class InstructionFileWalk
{
    /// <summary>Version control, editor state, build output, dependencies. Skipped
    /// by name wherever they sit.</summary>
    public static readonly string[] ExcludedDirectoryNames = [".git", ".vs", "bin", "obj", "node_modules"];

    /// <summary>
    /// Claude Code's worktrees, each a whole checkout of the repository under the
    /// folder this walks for instructions. Read as part of the clone they counted
    /// every instruction file once per worktree and every dependency's readme
    /// besides — and they are skipped by position rather than by name, because a
    /// repository is free to have a folder called <c>worktrees</c> of its own.
    /// </summary>
    public const string ClaudeWorktrees = ".claude/worktrees";

    /// <summary>Whether a directory of this name is one the walk never enters.</summary>
    public static bool IsExcludedName(string name) =>
        ExcludedDirectoryNames.Any(excluded => string.Equals(excluded, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the walk goes into <paramref name="directory"/>, a directory below
    /// <paramref name="root"/>.
    /// </summary>
    public static bool Descends(string root, string directory) =>
        !IsExcludedName(Path.GetFileName(directory)) && !IsClaudeWorktrees(root, directory);

    /// <summary>
    /// Every file under <paramref name="directory"/> whose name matches
    /// <paramref name="pattern"/>, without ever entering a directory
    /// <see cref="Descends"/> refuses. The same names
    /// <c>Directory.EnumerateFiles</c> would have returned for the directories
    /// that are walked: hidden and system entries included, unreadable ones
    /// skipped rather than thrown, matched the way the platform matches names.
    /// </summary>
    public static IEnumerable<string> EnumerateFiles(string root, string directory, string pattern)
    {
        var ignoreCase = !OperatingSystem.IsLinux();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            MatchType = MatchType.Simple
        };

        return new FileSystemEnumerable<string>(directory, (ref FileSystemEntry entry) => entry.ToFullPath(), options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                !entry.IsDirectory && FileSystemName.MatchesSimpleExpression(pattern, entry.FileName, ignoreCase),
            ShouldRecursePredicate = (ref FileSystemEntry entry) => Descends(root, entry.ToFullPath())
        };
    }

    private static bool IsClaudeWorktrees(string root, string directory)
    {
        var relative = Path.GetRelativePath(root, directory).Replace('\\', '/').TrimEnd('/');
        return string.Equals(relative, ClaudeWorktrees, StringComparison.OrdinalIgnoreCase);
    }
}
