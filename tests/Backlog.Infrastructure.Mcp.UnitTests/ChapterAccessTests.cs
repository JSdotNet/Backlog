using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// What <c>read_knowledge_chapter</c> will and will not open, with the
/// Instructions folder configured — which is the configuration every real
/// repository has, and the one that turns the containment check into no check at
/// all.
/// <para>
/// <b>The hole these close.</b> Instructions is the one area whose root
/// <em>is</em> the repository (<c>DefaultRelativePath</c> is empty), it is enabled
/// by default, and it is what <c>ChapterPaths.Split</c> falls back to when no area
/// prefix claims a path. So <c>chapterPath: ".git/config"</c> resolved, stayed
/// inside the root, was read with no extension filter, and came back as a chapter
/// — an arbitrary file read over the whole clone, in a tool annotated read-only
/// and described as serving knowledge chapters.
/// <see cref="DevbookToolsTests"/> did not see it because its folder list has no
/// Instructions folder in it.
/// </para>
/// <para>
/// Traversal <em>out</em> of the root is a different check and still
/// <c>ChapterPaths.ResolveWithin</c>'s —
/// <c>DevbookToolsTests.A_path_that_escapes_the_folder_is_refused</c> holds that
/// one. These are about the root itself.
/// </para>
/// </summary>
public sealed class ChapterAccessTests : IDisposable
{
    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("backlog-mcp-access");

    public ChapterAccessTests()
    {
        // A clone, as one looks: knowledge areas, instruction documents, and the
        // things beside them that a session must never be able to ask for.
        Write(".arc42/adr/0012-mcp.md", "# The MCP server\n");
        Write(".domain/devbook/features.md", "# Remarks on a chapter\n");
        Write(".github/copilot-instructions.md", "# Copilot\n");
        Write(".claude/rules/naming.md", "# Naming\n");
        Write("CLAUDE.md", "# Claude\n");
        Write("src/AGENTS.md", "# Nested\n");

        Write(".git/config", "[remote \"origin\"]\n\turl = git@github.com:JSdotNet/Backlog.git\n");
        Write(".env", "BACKLOG_SYNC_TOKEN=not-yours\n");
        Write("src/App/Backlog.Desktop/MauiProgram.cs", "// source\n");
        Write("obj/local-development/github.settings.json", "{ \"token\": \"not-yours\" }\n");
        Write(".arc42/notes.txt", "not a chapter\n");
        Write(".claude/worktrees/other/CLAUDE.md", "# Another checkout\n");
    }

    public void Dispose() => _root.Delete(recursive: true);

    /// <summary>The chapters, which still read. A gate that refused these would
    /// have closed the tool rather than the hole.</summary>
    [Theory]
    [InlineData(".arc42/adr/0012-mcp.md")]
    [InlineData(".domain/devbook/features.md")]
    // The instruction documents the product itself recognises, which are what the
    // Instructions folder is for.
    [InlineData(".github/copilot-instructions.md")]
    [InlineData(".claude/rules/naming.md")]
    [InlineData("CLAUDE.md")]
    [InlineData("src/AGENTS.md")]
    public async Task A_chapter_still_reads(string chapterPath)
    {
        var answer = await Tools().ReadKnowledgeChapterAsync(
            "JSdotNet/Backlog", chapterPath, review: false, TestContext.Current.CancellationToken);

        Assert.NotEmpty(answer.Markdown);
    }

    /// <summary>
    /// And everything else is refused before a byte of it is read.
    /// <para>
    /// Every one of these resolved, existed and was answered with before the
    /// gate: the git remote configuration, a secrets file, a source file, this
    /// worktree's own settings — and a file under an area folder that is not a
    /// chapter at all.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(".git/config")]
    [InlineData(".env")]
    [InlineData("src/App/Backlog.Desktop/MauiProgram.cs")]
    [InlineData("obj/local-development/github.settings.json")]
    // Markdown, but under no area folder and not an instruction document either:
    // the fall-through to the Instructions folder is what used to claim it.
    [InlineData("readme-of-my-own.md")]
    [InlineData("src/notes/scratch.md")]
    // Inside a real area folder, and not a chapter.
    [InlineData(".arc42/notes.txt")]
    // Another checkout entirely, which the instructions walk has always skipped.
    [InlineData(".claude/worktrees/other/CLAUDE.md")]
    public async Task Anything_that_is_not_a_chapter_is_refused(string chapterPath)
    {
        var failure = await Assert.ThrowsAsync<McpException>(() => Tools().ReadKnowledgeChapterAsync(
            "JSdotNet/Backlog", chapterPath, review: false, TestContext.Current.CancellationToken));

        Assert.Contains("chapter.not_a_chapter", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The refusal happens before the fetch, which is the difference
    /// between a tool that will not answer and a tool that downloads a branch to
    /// find out it will not answer.</summary>
    [Fact]
    public async Task A_refused_path_never_reaches_the_folder_source()
    {
        var folders = Folders();
        var tools = new DevbookTools(folders, new FakeDevbookAnnotationStore(), new FakeRepositoryDirectory([Backlog]));

        await Assert.ThrowsAsync<McpException>(() => tools.ReadKnowledgeChapterAsync(
            "JSdotNet/Backlog", ".git/config", review: false, TestContext.Current.CancellationToken));

        Assert.Empty(folders.Prepared);
    }

    /// <summary>
    /// The predicate itself, which is the Instructions panel's own: what it
    /// discovers is what a session may read, and neither side holds a list of its
    /// own.
    /// </summary>
    [Theory]
    [InlineData("CLAUDE.md", true)]
    [InlineData("AGENTS.md", true)]
    [InlineData("src/Modules/Tasks/AGENTS.md", true)]
    [InlineData(".claude/CLAUDE.md", true)]
    [InlineData(".claude/rules/naming.md", true)]
    [InlineData(".claude/skills/thing/SKILL.md", true)]
    [InlineData(".github/copilot-instructions.md", true)]
    [InlineData(".github/instructions/csharp.instructions.md", true)]
    [InlineData(".github/skills/pr-jsdotnet/SKILL.md", true)]
    [InlineData(".agents/skills/thing/SKILL.md", true)]
    [InlineData(".git/config", false)]
    [InlineData(".env", false)]
    [InlineData("secrets.json", false)]
    [InlineData("README.md", false)]
    // Under .github, and neither an instruction nor a skill: the folder is not a
    // pass on its own.
    [InlineData(".github/workflows/ci.yml", false)]
    [InlineData(".github/copilot-orch-context.md", false)]
    // Rules under .agents are authored there and wrapped per host; the panel
    // reads the wrappers, not these, so this predicate must not either.
    [InlineData(".agents/rules/naming.md", false)]
    [InlineData("bin/Debug/AGENTS.md", false)]
    [InlineData("node_modules/pkg/AGENTS.md", false)]
    [InlineData(".claude/worktrees/other/.claude/rules/naming.md", false)]
    [InlineData("../outside/CLAUDE.md", false)]
    [InlineData("", false)]
    public void The_instruction_predicate_names_what_the_panel_discovers(string path, bool expected)
    {
        Assert.Equal(expected, DevbookInstructionSources.IsInstructionSource(path));
    }

    private FakeDevbookFolderSource Folders() =>
        new(
            _root.FullName,
            new DevbookFolderSetting(".arc42", "Architecture", ".arc42"),
            new DevbookFolderSetting(".domain", "Domain", ".domain"),
            // The one that matters: enabled, last, and rooted at the clone.
            new DevbookFolderSetting("instructions", "Instructions", string.Empty, SupportsPathOverride: false));

    private DevbookTools Tools() =>
        new(Folders(), new FakeDevbookAnnotationStore(), new FakeRepositoryDirectory([Backlog]));

    private void Write(string relativePath, string content)
    {
        var fullPath = Path.Combine(_root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
