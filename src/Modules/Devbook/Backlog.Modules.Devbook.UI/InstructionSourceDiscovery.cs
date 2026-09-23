using Backlog.Modules.Devbook.Abstractions;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Devbook;

public sealed class InstructionSourceDiscovery
{
    /// <summary>
    /// The instruction files that sit at the repository root rather than inside one
    /// of the instruction folders.
    ///
    /// <para>Public because the Devbook menu builds its roots out of folders and
    /// so has no node to hang these off. It listed only <c>.github</c>, <c>.claude</c>
    /// and <c>.agents</c>, which left <c>CLAUDE.md</c> discovered, listed in the
    /// comparison, and impossible to open. Shared rather than copied, so the menu
    /// cannot drift from what discovery actually reads.</para>
    /// </summary>
    public static IReadOnlyList<string> RootFileNames { get; } = DevbookInstructionSources.RootFileNames;

    /// <summary>
    /// What a branch has to carry on disk before this area can be discovered:
    /// the three agent folders whole, the root files, and every
    /// <c>AGENTS.md</c> wherever it sits. Spelled in the folder port's selection
    /// language and handed to <see cref="IDevbookFolderSource.PrepareContentAsync"/>
    /// by the panel, because the instructions area is the one whose folder is the
    /// repository root — and "fetch the whole area" would be the whole repository,
    /// which is exactly the download branch loading exists to avoid.
    /// </summary>
    public static IReadOnlyList<string> BranchPaths { get; } =
    [
        ".github/",
        ".claude/",
        ".agent/",
        ".agents/",
        .. RootFileNames,
        "**/AGENTS.md"
    ];

    /// <summary>
    /// Reads each repository's instruction documents.
    /// <para>
    /// <paramref name="folders"/> is how a repository read from a branch finds
    /// its root. Instructions are the one area whose folder <em>is</em> the
    /// repository root — the setting carries an empty relative path — so the
    /// devbook-folder port already answers exactly the question this needed to
    /// ask, and asking it is what stops this from being a second, independent
    /// clone-directory gate that branch loading silently walks past. Omitting it
    /// falls back to the clone, which is what every caller did before branch
    /// loading existed.
    /// </para>
    /// </summary>
    public IReadOnlyList<InstructionRepositoryView> Discover(
        IEnumerable<GitHubRepositoryRef> repositories,
        IDevbookFolderSource? folders = null) =>
    [
        .. repositories.Select(repository => DiscoverRepository(repository, folders))
    ];

    private static InstructionRepositoryView DiscoverRepository(GitHubRepositoryRef repository, IDevbookFolderSource? folders)
    {
        var instructions = DevbookFolderSetting.Normalize(repository.DevbookFolders)
            .FirstOrDefault(folder => string.Equals(folder.Key, "instructions", StringComparison.OrdinalIgnoreCase));
        if (instructions is { Enabled: false })
        {
            return new InstructionRepositoryView(
                repository,
                null,
                [],
                "Instructions are turned off for this repository.");
        }

        var location = folders?.Resolve("instructions", repository.Alias);
        var fromBranch = location?.Source is DevbookSourceKind.Branch;

        if (fromBranch && location is not { Available: true })
        {
            return new InstructionRepositoryView(
                repository,
                location?.RootPath,
                [],
                location?.Message ?? "This repository's branch is being fetched.",
                CanEdit: false);
        }

        var configuredRoot = fromBranch ? location!.FullPath : repository.CloneDirectory;

        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return new InstructionRepositoryView(
                repository,
                null,
                [],
                "Set a local clone directory in Settings before instructions can be read.");
        }

        var root = Path.GetFullPath(configuredRoot);
        if (!Directory.Exists(root))
        {
            return new InstructionRepositoryView(
                repository,
                root,
                [],
                fromBranch
                    ? "The fetched copy of this branch is no longer on disk."
                    : "The configured local clone directory was not found.",
                CanEdit: !fromBranch);
        }

        var documents = new List<InstructionDocument>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDocument(root, ".github/copilot-instructions.md", "GitHub Copilot", "Repository-wide instructions", documents, seen);
        AddDocuments(root, ".github/instructions", "*.instructions.md", "GitHub Copilot", "Path-specific instructions", documents, seen);
        AddDocuments(root, ".github/skills", "SKILL.md", "GitHub Copilot", "Agent skills", documents, seen);
        AddDocument(root, "CLAUDE.md", "Claude Code", "Project instructions", documents, seen);
        AddDocument(root, ".claude/CLAUDE.md", "Claude Code", "Project instructions", documents, seen);
        AddDocuments(root, ".claude/rules", "*.md", "Claude Code", "Path-specific rules", documents, seen);
        // Skills before the folder walk, so they are labelled as skills rather
        // than as whatever else lives under .claude.
        AddDocuments(root, ".claude/skills", "SKILL.md", "Claude Code", "Agent skills", documents, seen);
        AddDocuments(root, ".claude", "*.md", "Claude Code", "Claude workspace files", documents, seen);
        AddDocuments(root, ".agents/skills", "SKILL.md", "Shared agent convention", "Agent skills", documents, seen);
        // Nested project files, read when Claude Code works under their folder.
        // The root one is already in the set, so this only ever adds the rest.
        AddDocuments(root, string.Empty, "CLAUDE.md", "Claude Code", "Directory-scoped project instructions", documents, seen);
        AddAgentsDocuments(root, documents, seen);

        documents.Sort(static (left, right) =>
        {
            var agent = AgentOrder(left.Agent).CompareTo(AgentOrder(right.Agent));
            return agent != 0
                ? agent
                : string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase);
        });

        return new InstructionRepositoryView(
            repository,
            root,
            documents,
            documents.Count switch
            {
                0 when fromBranch => "No recognized instruction documents were found on this branch.",
                0 => "No recognized instruction documents were found in this clone.",
                _ => null
            },
            CanEdit: !fromBranch);
    }

    private static int AgentOrder(string agent) => agent switch
    {
        "GitHub Copilot" => 0,
        "Claude Code" => 1,
        _ => 2
    };

    private static void AddDocument(
        string root,
        string relativePath,
        string agent,
        string scope,
        List<InstructionDocument> documents,
        HashSet<string> seen)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath)) return;

        AddResolvedDocument(root, fullPath, agent, scope, documents, seen);
    }

    private static void AddDocuments(
        string root,
        string relativeDirectory,
        string pattern,
        string agent,
        string scope,
        List<InstructionDocument> documents,
        HashSet<string> seen)
    {
        var directory = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory)) return;

        // Pruned as it walks, never filtered afterwards: the excluded folders are
        // where the bulk of a clone's directories are, and a walk that enters them
        // to discard what it finds pays for the whole clone to list the handful
        // of files it keeps. InstructionFileWalk says which and why.
        foreach (var fullPath in InstructionFileWalk.EnumerateFiles(root, directory, pattern))
        {
            AddResolvedDocument(root, fullPath, agent, scope, documents, seen);
        }
    }

    private static void AddAgentsDocuments(string root, List<InstructionDocument> documents, HashSet<string> seen)
    {
        foreach (var fullPath in InstructionFileWalk.EnumerateFiles(root, root, "AGENTS.md"))
        {
            AddResolvedDocument(root, fullPath, "Shared agent convention", "Directory-scoped agent instructions", documents, seen);
        }
    }

    private static void AddResolvedDocument(
        string root,
        string fullPath,
        string agent,
        string scope,
        List<InstructionDocument> documents,
        HashSet<string> seen)
    {
        var normalizedFullPath = Path.GetFullPath(fullPath);
        if (!seen.Add(normalizedFullPath)) return;

        var info = new FileInfo(normalizedFullPath);
        var relativePath = Path.GetRelativePath(root, normalizedFullPath);

        // The same predicate the MCP tool refuses a chapter with, asked here so
        // the two cannot drift: what this panel shows is what a session may read,
        // and a source added to the calls above without being added to the
        // predicate disappears here rather than quietly becoming readable there.
        if (!DevbookInstructionSources.IsInstructionSource(relativePath)) return;
        documents.Add(new InstructionDocument(
            Path.GetFileName(relativePath),
            relativePath,
            agent,
            scope,
            File.ReadAllText(normalizedFullPath),
            info.Length,
            info.LastWriteTimeUtc));
    }
}

/// <param name="CanEdit">Whether these documents may be written to. False for a
/// branch snapshot, which is a copy of somebody's commit — an edit to it would
/// survive exactly until the next fetch. Defaults to true so that every caller
/// predating branch loading keeps offering the edit it always offered.</param>
public sealed record InstructionRepositoryView(
    GitHubRepositoryRef Repository,
    string? LocalRoot,
    IReadOnlyList<InstructionDocument> Documents,
    string? Message,
    bool CanEdit = true);

public sealed record InstructionDocument(
    string Title,
    string RelativePath,
    string Agent,
    string Scope,
    string Content,
    long SizeBytes,
    DateTime LastModifiedUtc);
