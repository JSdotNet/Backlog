using Backlog.Modules.Knowledge.Abstractions;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Knowledge;

public sealed class InstructionSourceDiscovery
{
    private static readonly string[] ExcludedDirectoryNames =
    [
        ".git",
        ".vs",
        "bin",
        "obj",
        "node_modules"
    ];

    public IReadOnlyList<InstructionRepositoryView> Discover(IEnumerable<GitHubRepositoryRef> repositories) =>
    [
        .. repositories.Select(DiscoverRepository)
    ];

    private static InstructionRepositoryView DiscoverRepository(GitHubRepositoryRef repository)
    {
        var instructions = KnowledgeFolderSetting.Normalize(repository.KnowledgeFolders)
            .FirstOrDefault(folder => string.Equals(folder.Key, "instructions", StringComparison.OrdinalIgnoreCase));
        if (instructions is { Enabled: false })
        {
            return new InstructionRepositoryView(
                repository,
                null,
                [],
                "Instructions are turned off for this repository.");
        }

        if (string.IsNullOrWhiteSpace(repository.CloneDirectory))
        {
            return new InstructionRepositoryView(
                repository,
                null,
                [],
                "Set a local clone directory in Settings before instructions can be read.");
        }

        var root = Path.GetFullPath(repository.CloneDirectory);
        if (!Directory.Exists(root))
        {
            return new InstructionRepositoryView(
                repository,
                root,
                [],
                "The configured local clone directory was not found.");
        }

        var documents = new List<InstructionDocument>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDocument(root, ".github/copilot-instructions.md", "GitHub Copilot", "Repository-wide instructions", documents, seen);
        AddDocuments(root, ".github/instructions", "*.instructions.md", "GitHub Copilot", "Path-specific instructions", documents, seen);
        AddDocument(root, "CLAUDE.md", "Claude Code", "Project instructions", documents, seen);
        AddDocument(root, ".claude/CLAUDE.md", "Claude Code", "Project instructions", documents, seen);
        AddDocuments(root, ".claude/rules", "*.md", "Claude Code", "Path-specific rules", documents, seen);
        AddDocuments(root, ".claude", "*.md", "Claude Code", "Claude workspace files", documents, seen);
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
            documents.Count == 0 ? "No recognized instruction documents were found in this clone." : null);
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

        foreach (var fullPath in Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
        {
            AddResolvedDocument(root, fullPath, agent, scope, documents, seen);
        }
    }

    private static void AddAgentsDocuments(string root, List<InstructionDocument> documents, HashSet<string> seen)
    {
        foreach (var fullPath in Directory.EnumerateFiles(root, "AGENTS.md", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(root, fullPath);
            if (relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(IsExcludedDirectory))
            {
                continue;
            }

            AddResolvedDocument(root, fullPath, "Shared agent convention", "Directory-scoped agent instructions", documents, seen);
        }
    }

    private static bool IsExcludedDirectory(string part) =>
        ExcludedDirectoryNames.Any(excluded => string.Equals(excluded, part, StringComparison.OrdinalIgnoreCase));

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

public sealed record InstructionRepositoryView(
    GitHubRepositoryRef Repository,
    string? LocalRoot,
    IReadOnlyList<InstructionDocument> Documents,
    string? Message);

public sealed record InstructionDocument(
    string Title,
    string RelativePath,
    string Agent,
    string Scope,
    string Content,
    long SizeBytes,
    DateTime LastModifiedUtc);
