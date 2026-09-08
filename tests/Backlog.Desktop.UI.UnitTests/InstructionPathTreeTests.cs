using Backlog.UI.Components.Menus;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class InstructionPathTreeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-path-tree-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public void Reads_folders_before_files_each_in_name_order()
    {
        Write("src/App/Home.razor", "<h1/>");
        Write("src/Program.cs", "// program");
        Write("README.md", "# Read me");

        var tree = InstructionPathTree.Read(_root);

        Assert.Equal(["src", "README.md"], tree.Select(node => node.Label));

        var source = Assert.Single(tree, node => node.Label == "src");
        Assert.Equal(TreeNodeKind.Folder, source.Kind);
        Assert.Equal(["App", "Program.cs"], source.Children.Select(node => node.Label));
    }

    /// <summary>The id is what the matcher is handed, so it is the path as the
    /// globs are written and not as this file system spells it.</summary>
    [Fact]
    public void Identifies_a_row_by_its_repository_relative_path()
    {
        Write("src/App/Home.razor", "<h1/>");

        var file = Assert.Single(Descendants(InstructionPathTree.Read(_root)), node => node.Label == "Home.razor");

        Assert.Equal("src/App/Home.razor", file.Id);
        Assert.Equal(TreeNodeKind.Item, file.Kind);
    }

    [Fact]
    public void Leaves_out_build_output_and_dependencies()
    {
        Write("src/App/Home.razor", "<h1/>");
        Write("src/App/bin/Debug/Backlog.dll", "binary");
        Write("src/App/obj/project.assets.json", "{}");
        Write("node_modules/left-pad/index.js", "module");
        Write(".vs/state.json", "{}");
        Write(".git/HEAD", "ref: refs/heads/main");

        var labels = Descendants(InstructionPathTree.Read(_root)).Select(node => node.Label).ToList();

        Assert.Contains("Home.razor", labels);
        Assert.DoesNotContain("bin", labels);
        Assert.DoesNotContain("obj", labels);
        Assert.DoesNotContain("node_modules", labels);
        Assert.DoesNotContain(".vs", labels);
        Assert.DoesNotContain(".git", labels);
    }

    /// <summary>
    /// In a worktree <c>.git</c> is a pointer file rather than a folder, so a
    /// filter that only looked at directory names let it through — and only in a
    /// worktree, which is the sort of difference that survives review because the
    /// clone everybody tests in does not have it.
    /// </summary>
    [Fact]
    public void Leaves_out_a_git_pointer_file_as_well_as_a_git_folder()
    {
        Write("README.md", "# Read me");
        Write(".git", "gitdir: D:/Repos/Backlog/.git/worktrees/example");

        var labels = Descendants(InstructionPathTree.Read(_root)).Select(node => node.Label).ToList();

        Assert.Contains("README.md", labels);
        Assert.DoesNotContain(".git", labels);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Reads_nothing_from_nothing(string? root) =>
        Assert.Empty(InstructionPathTree.Read(root));

    [Fact]
    public void Reads_nothing_from_a_folder_that_is_not_there() =>
        Assert.Empty(InstructionPathTree.Read(Path.Combine(_root, "absent")));

    private static IEnumerable<TreeNode> Descendants(IEnumerable<TreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Descendants(node.Children))
            {
                yield return child;
            }
        }
    }

    private void Write(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
