namespace Backlog.Tests;

/// <summary>
/// The repository root, resolved once, for tests that read repository files —
/// markup, stylesheets, scripts, samples, project layout — rather than compiled
/// behaviour.
///
/// <para>Those tests used to walk up from <see cref="AppContext.BaseDirectory"/>
/// until the file they wanted existed, with nothing to stop the walk. Inside a
/// git worktree — <c>&lt;checkout&gt;/.claude/worktrees/&lt;session&gt;</c> — that
/// walk climbs out of the worktree and into the parent checkout, which is a
/// different revision of the same repository. A path that no longer exists on
/// the branch under test then resolved against the parent's older copy and the
/// test passed for the wrong reason. Local runs were green, CI — which has no
/// checkout above the working directory — was the only place that told the
/// truth, and it told it a commit too late.</para>
///
/// <para>So the walk stops here. It looks for the folder that holds <c>src</c>,
/// <c>tests</c> and <c>Backlog.sln</c>, which is the worktree root rather than
/// the checkout above it, and every lookup is resolved beneath that one folder.
/// A path that is wrong now fails immediately and names the root it was resolved
/// against, instead of quietly finding something else.</para>
///
/// <para>This file is linked into every test project by
/// <c>tests/Directory.Build.props</c> rather than shared through a project
/// reference, because <c>Backlog.ArchitectureTests</c> deliberately has none —
/// it reads the repository layout, so a reference would be part of what it is
/// checking.</para>
/// </summary>
internal static class RepositoryRoot
{
    /// <summary>The folder holding <c>src</c>, <c>tests</c> and <c>Backlog.sln</c>.</summary>
    public static DirectoryInfo Root { get; } = Locate();

    /// <summary>
    /// The full path of a repository file the caller is about to read, verified
    /// to exist beneath <see cref="Root"/>.
    /// </summary>
    /// <exception cref="FileNotFoundException">
    /// No such file beneath the repository root — the path is stale or wrong.
    /// </exception>
    public static string File(params string[] relativePath)
    {
        var candidate = Combine(relativePath);

        if (!System.IO.File.Exists(candidate))
        {
            throw new FileNotFoundException(
                $"Could not locate '{Path.Combine(relativePath)}' under the repository root '{Root.FullName}'.",
                candidate);
        }

        return candidate;
    }

    /// <summary>
    /// The full path of a repository folder the caller is about to enumerate,
    /// verified to exist beneath <see cref="Root"/>.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">
    /// No such folder beneath the repository root — the path is stale or wrong.
    /// </exception>
    public static string Directory(params string[] relativePath)
    {
        var candidate = Combine(relativePath);

        if (!System.IO.Directory.Exists(candidate))
        {
            throw new DirectoryNotFoundException(
                $"Could not locate '{Path.Combine(relativePath)}' under the repository root '{Root.FullName}'.");
        }

        return candidate;
    }

    /// <summary>
    /// Where a repository-relative path resolves to, whether or not anything is
    /// there. For tests that assert on presence or absence themselves and want
    /// the answer as an assertion rather than an exception.
    /// </summary>
    public static string Combine(params string[] relativePath) =>
        Path.Combine([Root.FullName, .. relativePath]);

    private static DirectoryInfo Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (System.IO.Directory.Exists(Path.Combine(directory.FullName, "src"))
                && System.IO.Directory.Exists(Path.Combine(directory.FullName, "tests"))
                && System.IO.File.Exists(Path.Combine(directory.FullName, "Backlog.sln")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root above '{AppContext.BaseDirectory}'.");
    }
}
