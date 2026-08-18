using System.Text.RegularExpressions;

namespace Backlog.Desktop.UI.Knowledge;

public sealed record KnowledgeIssueLink(string RepoFullName, int IssueNumber, string? LabelOverride = null)
{
    public string Url => $"https://github.com/{RepoFullName}/issues/{IssueNumber}";
    public string Label => LabelOverride ?? $"#{IssueNumber}";
}

public sealed record KnowledgeActionItem(
    string Title,
    string Kind,
    string Path,
    IReadOnlyDictionary<string, string> Metadata,
    string? Summary);

public static partial class KnowledgeActionMetadata
{
    private static readonly string[] IssueKeys = ["knowledge-issue", "github-issue", "issue"];

    public static string Status(IReadOnlyDictionary<string, string> metadata) =>
        metadata.TryGetValue("status", out var status) && !string.IsNullOrWhiteSpace(status)
            ? status.Trim().ToLowerInvariant()
            : "none";

    public static KnowledgeIssueLink? Issue(IReadOnlyDictionary<string, string> metadata, string? defaultRepository)
    {
        foreach (var key in IssueKeys)
        {
            if (!metadata.TryGetValue(key, out var value)) continue;
            if (TryParseIssue(value, defaultRepository, out var link)) return link;
        }

        return null;
    }

    public static string BuildPrompt(KnowledgeActionItem item) =>
        $"Work on this {item.Kind} knowledge item with GitHub Copilot CLI.\n\n"
        + "Use the knowledge metadata and summary as the task brief and preserve its intent.\n\n"
        + $"Knowledge item: {item.Title}\n"
        + $"Type: {item.Kind}\n"
        + $"Path: {item.Path}\n"
        + $"State: {Status(item.Metadata)}\n"
        + MetadataPrompt(item.Metadata)
        + SummaryPrompt(item.Summary);

    private static string MetadataPrompt(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.Count == 0) return string.Empty;

        var lines = metadata.Select(pair => $"- {pair.Key}: {pair.Value}");
        return "\nMetadata:\n" + string.Join('\n', lines) + "\n";
    }

    private static string SummaryPrompt(string? summary) =>
        string.IsNullOrWhiteSpace(summary) ? string.Empty : $"\nSummary:\n{summary.Trim()}\n";

    private static bool TryParseIssue(string? raw, string? defaultRepository, out KnowledgeIssueLink link)
    {
        link = default!;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var value = raw.Trim();
        if (value.Equals("null", StringComparison.OrdinalIgnoreCase) || value.Equals("none", StringComparison.OrdinalIgnoreCase)) return false;

        var match = IssueUrlRegex().Match(value);
        if (match.Success && int.TryParse(match.Groups["number"].Value, out var urlNumber))
        {
            var repo = $"{match.Groups["owner"].Value}/{match.Groups["repo"].Value}";
            link = new KnowledgeIssueLink(repo, urlNumber, $"{repo}#{urlNumber}");
            return true;
        }

        match = OwnerRepoIssueRegex().Match(value);
        if (match.Success && int.TryParse(match.Groups["number"].Value, out var repoNumber))
        {
            var repo = $"{match.Groups["owner"].Value}/{match.Groups["repo"].Value}";
            link = new KnowledgeIssueLink(repo, repoNumber, $"{repo}#{repoNumber}");
            return true;
        }

        match = IssueNumberRegex().Match(value);
        if (match.Success && int.TryParse(match.Groups["number"].Value, out var number) && !string.IsNullOrWhiteSpace(defaultRepository))
        {
            link = new KnowledgeIssueLink(defaultRepository, number);
            return true;
        }

        return false;
    }

    [GeneratedRegex("^https://github\\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/issues/(?<number>\\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IssueUrlRegex();

    [GeneratedRegex("^(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)#(?<number>\\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OwnerRepoIssueRegex();

    [GeneratedRegex("^#?(?<number>\\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex IssueNumberRegex();
}