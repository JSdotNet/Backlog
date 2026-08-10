using Backlog.Desktop.UI.Services;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class InstructionSourceDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-instructions-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public void Finds_copilot_claude_and_agents_instruction_sources()
    {
        Write(".github/copilot-instructions.md", "# Copilot");
        Write(".github/instructions/csharp.instructions.md", "# C#");
        Write("CLAUDE.md", "# Claude");
        Write(".claude/CLAUDE.md", "# Claude project");
        Write(".claude/rules/tests.md", "# Test rules");
        Write(".claude/commands/release.md", "# Release command");
        Write("AGENTS.md", "# Agents");
        Write("src/AGENTS.md", "# Nested agents");

        var repository = new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog") { CloneDirectory = _root };
        var result = Assert.Single(new InstructionSourceDiscovery().Discover([repository]));

        Assert.Null(result.Message);
        Assert.Equal(
            [
                Path.Combine(".github", "copilot-instructions.md"),
                Path.Combine(".github", "instructions", "csharp.instructions.md"),
                Path.Combine(".claude", "CLAUDE.md"),
                Path.Combine(".claude", "commands", "release.md"),
                Path.Combine(".claude", "rules", "tests.md"),
                "CLAUDE.md",
                "AGENTS.md",
                Path.Combine("src", "AGENTS.md"),
            ],
            result.Documents.Select(d => d.RelativePath));
        Assert.Contains(result.Documents, d => d.Agent == "GitHub Copilot" && d.Scope == "Repository-wide instructions");
        Assert.Contains(result.Documents, d => d.Agent == "Claude Code" && d.Scope == "Path-specific rules");
        Assert.Contains(result.Documents, d => d.Agent == "Shared agent convention");
        Assert.Contains(result.Documents, d => d.RelativePath == Path.Combine(".github", "copilot-instructions.md") && d.Content == "# Copilot");
        Assert.Contains(result.Documents, d => d.RelativePath == Path.Combine("src", "AGENTS.md") && d.Content == "# Nested agents");
    }

    [Fact]
    public void Ignores_generated_and_dependency_agents_files()
    {
        Write("AGENTS.md", "# Root agents");
        Write(".git/AGENTS.md", "# Git internals");
        Write("bin/AGENTS.md", "# Build output");
        Write("node_modules/package/AGENTS.md", "# Dependency");

        var repository = new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog") { CloneDirectory = _root };
        var result = Assert.Single(new InstructionSourceDiscovery().Discover([repository]));

        Assert.Equal(["AGENTS.md"], result.Documents.Select(d => d.RelativePath));
    }

    [Fact]
    public void Reports_missing_clone_directory_without_throwing()
    {
        var repository = new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog");

        var result = Assert.Single(new InstructionSourceDiscovery().Discover([repository]));

        Assert.Empty(result.Documents);
        Assert.Contains("local clone directory", result.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
