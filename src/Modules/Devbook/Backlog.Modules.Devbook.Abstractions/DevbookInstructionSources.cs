namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// Which files of a repository are instruction documents, and which directories
/// nothing looks in — the one place that says so.
/// <para>
/// The Instructions folder is the one knowledge area whose root <em>is</em> the
/// repository (<see cref="DevbookFolderSetting.DefaultRelativePath"/> is empty
/// for it), so "inside the area" is no answer at all there: every file of the
/// clone is inside it, <c>.git/config</c> and a <c>.env</c> among them. What
/// makes a file an instruction document is that the product recognises it as one,
/// and the product already has a list — the sources
/// <c>InstructionSourceDiscovery</c> reads.
/// </para>
/// <para>
/// This is that list, as a predicate, so the two readers of it cannot disagree:
/// the panel discovers exactly what this accepts, and the MCP tool serves exactly
/// what this accepts. The same reasoning <c>InstructionFileWalk</c> gives for
/// owning the excluded directories once — "the two cannot disagree about what a
/// repository is if only one of them says" — one level up, and the excluded
/// directories move here with it so a walker and a reader share those too.
/// </para>
/// <para>
/// Nothing here touches a disk. It is a rule about a path, asked before a file is
/// opened and asked of a path a client sent, which is why it lives in the
/// published surface rather than beside either reader.
/// </para>
/// </summary>
public static class DevbookInstructionSources
{
    /// <summary>Version control, editor state, build output, dependencies. No
    /// walk enters a directory of one of these names, wherever it sits, and no
    /// path through one is an instruction document.</summary>
    public static readonly string[] ExcludedDirectoryNames = [".git", ".vs", "bin", "obj", "node_modules"];

    /// <summary>
    /// Claude Code's worktrees, each a whole checkout of the repository under the
    /// folder the instructions area walks. Skipped by position rather than by
    /// name, because a repository is free to have a folder called
    /// <c>worktrees</c> of its own.
    /// </summary>
    public const string ClaudeWorktrees = ".claude/worktrees";

    /// <summary>The two instruction files that sit at the repository root, and
    /// which Claude Code and the shared agent convention also read from any
    /// directory below it.</summary>
    public static readonly string[] RootFileNames = ["CLAUDE.md", "AGENTS.md"];

    /// <summary>Whether a directory of this name is one nothing ever enters.</summary>
    public static bool IsExcludedName(string? name) =>
        ExcludedDirectoryNames.Any(excluded => string.Equals(excluded, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether a repository-relative path names an instruction document.
    /// <para>
    /// The list, exactly as <c>InstructionSourceDiscovery</c> reads it:
    /// <c>CLAUDE.md</c> and <c>AGENTS.md</c> at any depth; anything under
    /// <c>.claude/</c> that is Markdown; <c>.github/copilot-instructions.md</c>;
    /// <c>*.instructions.md</c> under <c>.github/instructions/</c>; and
    /// <c>SKILL.md</c> under <c>.github/skills/</c> or <c>.agents/skills/</c>.
    /// Nothing else, and never a file that is not Markdown — which is the whole
    /// of what keeps <c>.git/config</c>, a <c>.env</c> and a build output from
    /// being served as a chapter.
    /// </para>
    /// </summary>
    public static bool IsInstructionSource(string? repositoryRelativePath)
    {
        var path = (repositoryRelativePath ?? string.Empty).Replace('\\', '/').Trim().Trim('/');

        if (path.Length == 0) return false;
        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return false;

        var segments = path.Split('/');

        // A relative segment never reaches a file inside the clone by a route
        // anything here can reason about, and the containment check that catches
        // it runs later. Refused here so the rule is decidable on the spelling.
        if (segments.Any(segment => segment is "." or ".." || segment.Length == 0)) return false;

        if (segments.Any(IsExcludedName)) return false;
        if (path.StartsWith(ClaudeWorktrees + "/", StringComparison.OrdinalIgnoreCase)) return false;

        var name = segments[^1];

        if (RootFileNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;

        // Everything Markdown under .claude: the project file, the rules, the
        // skills and the workspace files the panel labels as such.
        if (Under(segments, ".claude")) return true;

        if (string.Equals(path, ".github/copilot-instructions.md", StringComparison.OrdinalIgnoreCase)) return true;

        if (Under(segments, ".github", "instructions")
            && name.EndsWith(".instructions.md", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (Under(segments, ".github", "skills") || Under(segments, ".agents", "skills"))
            && string.Equals(name, "SKILL.md", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether the path opens with these folders and has a file below
    /// them — at any depth, because every one of these sources is walked
    /// recursively.</summary>
    private static bool Under(string[] segments, params string[] folders)
    {
        if (segments.Length <= folders.Length) return false;

        for (var index = 0; index < folders.Length; index++)
        {
            if (!string.Equals(segments[index], folders[index], StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }
}
