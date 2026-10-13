using Backlog.Infrastructure.Devbook.Instructions;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The instructions panel's read of a file the menu picked that discovery did not
/// list. It used to open the file itself; it asks the adapter now (issue #738), and
/// these hold the read to what the panel did: inside the root, inside one of the
/// agent folders, and <c>.agent/</c> falling back to <c>.agents/</c>.
/// </summary>
public sealed class InstructionDocumentReadTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "backlog-instruction-read-tests", Guid.NewGuid().ToString("n"));

    private string Root => Path.Combine(_folder, "repository");

    [Fact]
    public void Reads_a_file_under_an_agent_folder()
    {
        Write(".github/prompts/release.prompt.md", "# Release");

        var document = InstructionSourceDiscovery.ReadDocument(Root, ".github/prompts/release.prompt.md");

        Assert.NotNull(document);
        Assert.Equal("release.prompt.md", document.Title);
        Assert.Equal(Path.Combine(".github", "prompts", "release.prompt.md"), document.RelativePath);
        Assert.Equal("GitHub Copilot", document.Agent);
        Assert.Equal("Repository file", document.Scope);
        Assert.Equal("# Release", document.Content);
        Assert.Equal(9, document.SizeBytes);
    }

    [Fact]
    public void Names_the_agent_by_the_folder_the_file_sits_in()
    {
        Write(".claude/commands/ship.md", "# Ship");
        Write(".agents/notes.md", "# Notes");

        Assert.Equal("Claude Code", InstructionSourceDiscovery.ReadDocument(Root, ".claude/commands/ship.md")?.Agent);
        Assert.Equal("Agent instructions", InstructionSourceDiscovery.ReadDocument(Root, ".agents/notes.md")?.Agent);
    }

    /// <summary>The menu spells the shared convention's folder <c>.agent</c>; a
    /// clone that has only <c>.agents</c> still opens the file.</summary>
    [Fact]
    public void Falls_back_from_the_agent_folder_to_the_agents_folder()
    {
        Write(".agents/skills/run/SKILL.md", "# Run");

        var document = InstructionSourceDiscovery.ReadDocument(Root, ".agent/skills/run/SKILL.md");

        Assert.NotNull(document);
        Assert.Equal("# Run", document.Content);
    }

    [Theory]
    [InlineData("src/Program.cs")]
    [InlineData("README.md")]
    [InlineData(".github/../../outside.md")]
    public void Refuses_a_path_outside_the_agent_folders_or_theRoot(string selectedPath)
    {
        Write("src/Program.cs", "// program");
        Write("README.md", "# Read me");
        File.WriteAllText(Path.Combine(_folder, "outside.md"), "# Outside");

        Assert.Null(InstructionSourceDiscovery.ReadDocument(Root, selectedPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reads_nothing_without_aRoot(string? root) =>
        Assert.Null(InstructionSourceDiscovery.ReadDocument(root, ".github/copilot-instructions.md"));

    [Fact]
    public void Reads_nothing_for_a_file_that_is_not_there() =>
        Assert.Null(InstructionSourceDiscovery.ReadDocument(Root, ".github/copilot-instructions.md"));

    private void Write(string relativePath, string content)
    {
        var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
