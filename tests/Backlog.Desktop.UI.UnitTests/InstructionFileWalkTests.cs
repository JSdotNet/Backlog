namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The walk the instructions area reads a clone with: which directories it
/// turns back at, and that discovery and the path picker turn back at the same
/// ones.
///
/// <para>Result-based, because a pruned walk and a filtered one hand back the
/// same files. What tells them apart is where the excluded matches sit: deep
/// inside the folders a walk must not enter, where a filter would have paid to
/// find them first.</para>
/// </summary>
public sealed class InstructionFileWalkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-instruction-walk-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public void The_walk_turns_back_at_build_output_dependencies_and_version_control()
    {
        Write("AGENTS.md");
        Write("src/AGENTS.md");
        Write("bin/Debug/net10.0/deep/AGENTS.md");
        Write("obj/AGENTS.md");
        Write("node_modules/pkg/nested/AGENTS.md");
        Write(".git/AGENTS.md");
        Write(".vs/AGENTS.md");

        Assert.Equal(
            ["AGENTS.md", Path.Combine("src", "AGENTS.md")],
            Relative(InstructionFileWalk.EnumerateFiles(_root, _root, "AGENTS.md")));
    }

    /// <summary>By position, not by name: the worktrees under <c>.claude</c> are
    /// other checkouts, and a repository's own folder called <c>worktrees</c> is
    /// still the repository.</summary>
    [Fact]
    public void The_walk_turns_back_at_claude_worktrees_and_nowhere_else_called_that()
    {
        Write(".claude/rules/tests.md");
        Write(".claude/worktrees/feature-a/CLAUDE.md");
        Write(".claude/worktrees/feature-a/.claude/rules/tests.md");
        Write("docs/worktrees/notes.md");

        Assert.Equal(
            [Path.Combine(".claude", "rules", "tests.md")],
            Relative(InstructionFileWalk.EnumerateFiles(_root, Path.Combine(_root, ".claude"), "*.md")));
        Assert.Equal(
            [Path.Combine(".claude", "rules", "tests.md"), Path.Combine("docs", "worktrees", "notes.md")],
            Relative(InstructionFileWalk.EnumerateFiles(_root, _root, "*.md")));

        Assert.False(InstructionFileWalk.Descends(_root, Path.Combine(_root, ".claude", "worktrees")));
        Assert.True(InstructionFileWalk.Descends(_root, Path.Combine(_root, "docs", "worktrees")));
        Assert.False(InstructionFileWalk.Descends(_root, Path.Combine(_root, "src", "node_modules")));
    }

    /// <summary>The picker prunes with the same predicate, so a clone with
    /// worktrees offers its own files and not thirty checkouts' worth.</summary>
    [Fact]
    public void The_path_picker_leaves_out_the_worktrees_under_the_claude_folder()
    {
        Write("src/Program.cs");
        Write(".claude/rules/tests.md");
        Write(".claude/worktrees/feature-a/src/Program.cs");
        Write("node_modules/pkg/index.js");

        var ids = new List<string>();
        Collect(InstructionPathTree.Read(_root), ids);

        Assert.Contains("src/Program.cs", ids);
        Assert.Contains(".claude/rules/tests.md", ids);
        Assert.DoesNotContain(ids, id => id.StartsWith(".claude/worktrees", StringComparison.Ordinal));
        Assert.DoesNotContain(ids, id => id.StartsWith("node_modules", StringComparison.Ordinal));
    }

    private static void Collect(IEnumerable<Backlog.UI.Components.Menus.TreeNode> nodes, List<string> ids)
    {
        foreach (var node in nodes)
        {
            ids.Add(node.Id);
            Collect(node.Children, ids);
        }
    }

    private IEnumerable<string> Relative(IEnumerable<string> paths) =>
        paths.Select(path => Path.GetRelativePath(_root, path)).Order(StringComparer.Ordinal);

    private void Write(string relativePath)
    {
        var fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "# " + relativePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
