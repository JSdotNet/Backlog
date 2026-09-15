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

    /// <summary>
    /// Claude Code keeps its worktrees under <c>.claude/worktrees</c>, and each one
    /// is a whole checkout — instruction files, dependencies and all. Walking
    /// into them counted a repository with nine instruction files as having two
    /// and a half thousand, nearly all of them somebody's <c>node_modules</c>.
    /// </summary>
    [Fact]
    public void Ignores_other_worktrees_checked_out_under_the_claude_folder()
    {
        Write("CLAUDE.md", "# Root");
        Write("AGENTS.md", "# Root agents");
        Write(".claude/worktrees/feature-a/CLAUDE.md", "# Another checkout");
        Write(".claude/worktrees/feature-a/AGENTS.md", "# Another checkout");
        Write(".claude/worktrees/feature-a/.claude/rules/tests.md", "# Another checkout");
        Write(".claude/worktrees/feature-a/src/frontend/node_modules/pkg/README.md", "# Dependency");
        Write(".claude/node_modules/pkg/README.md", "# Dependency");

        var repository = new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog") { CloneDirectory = _root };
        var result = Assert.Single(new InstructionSourceDiscovery().Discover([repository]));

        Assert.Equal(["CLAUDE.md", "AGENTS.md"], result.Documents.Select(d => d.RelativePath));
    }

    /// <summary>
    /// A nested <c>CLAUDE.md</c> is read when Claude Code works under its folder,
    /// and in a repository that keeps its conventions in <c>AGENTS.md</c> files it
    /// is the one-line import that gets them to Claude at all. Undiscovered, every
    /// nested <c>AGENTS.md</c> read as unreachable from Claude's side.
    /// </summary>
    [Fact]
    public void Finds_nested_claude_files_beside_the_root_one()
    {
        Write("CLAUDE.md", "@AGENTS.md");
        Write("src/backend/CLAUDE.md", "@AGENTS.md");
        Write("src/backend/AGENTS.md", "# Backend");
        Write("src/frontend/node_modules/pkg/CLAUDE.md", "# Dependency");

        var repository = new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog") { CloneDirectory = _root };
        var result = Assert.Single(new InstructionSourceDiscovery().Discover([repository]));

        Assert.Equal(
            [
                "CLAUDE.md",
                Path.Combine("src", "backend", "CLAUDE.md"),
                Path.Combine("src", "backend", "AGENTS.md"),
            ],
            result.Documents.Select(d => d.RelativePath));

        var nested = result.Documents[1];
        Assert.Equal("Claude Code", nested.Agent);
        Assert.Equal("Directory-scoped project instructions", nested.Scope);
        Assert.Equal("Project instructions", result.Documents[0].Scope);
    }

    /// <summary>Skills, wherever a host looks for them — and labelled as skills
    /// rather than as whatever folder they happen to sit in.</summary>
    [Fact]
    public void Finds_skills_in_every_folder_a_host_reads_them_from()
    {
        Write(".github/skills/release/SKILL.md", "# Release");
        Write(".claude/skills/deploy/SKILL.md", "# Deploy");
        Write(".claude/skills/deploy/reference.md", "# Not the skill itself");
        Write(".agents/skills/tenants/SKILL.md", "# Tenants");
        Write(".agents/skills/tenants/scripts/notes.md", "# Not a skill");

        var repository = new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog") { CloneDirectory = _root };
        var result = Assert.Single(new InstructionSourceDiscovery().Discover([repository]));

        Assert.Equal(
            [
                Path.Combine(".github", "skills", "release", "SKILL.md"),
                Path.Combine(".claude", "skills", "deploy", "reference.md"),
                Path.Combine(".claude", "skills", "deploy", "SKILL.md"),
                Path.Combine(".agents", "skills", "tenants", "SKILL.md"),
            ],
            result.Documents.Select(d => d.RelativePath));
        Assert.All(
            result.Documents.Where(d => d.Title == "SKILL.md"),
            skill => Assert.Equal("Agent skills", skill.Scope));
        Assert.Equal("GitHub Copilot", result.Documents[0].Agent);
        Assert.Equal("Claude Code", result.Documents[1].Agent);
        Assert.Equal("Shared agent convention", result.Documents[3].Agent);
    }

    [Fact]
    public void Reports_missing_clone_directory_without_throwing()
    {
        var repository = new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog");

        var result = Assert.Single(new InstructionSourceDiscovery().Discover([repository]));

        Assert.Empty(result.Documents);
        Assert.Contains("local clone directory", result.Message);
    }


    [Fact]
    public void Skips_instruction_discovery_when_repository_instructions_are_disabled()
    {
        Write(".github/copilot-instructions.md", "# Copilot");
        var repository = new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog")
        {
            CloneDirectory = _root,
            DevbookFolders =
            [
                .. DevbookFolderSetting.Defaults().Select(folder => folder.Key == "instructions"
                    ? folder with { Enabled = false }
                    : folder)
            ]
        };

        var result = Assert.Single(new InstructionSourceDiscovery().Discover([repository]));

        Assert.Empty(result.Documents);
        Assert.Contains("turned off", result.Message);
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
