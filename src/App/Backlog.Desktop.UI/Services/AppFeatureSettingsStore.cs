using System.Text.Json;

namespace Backlog.Desktop.UI.Services;

/// <summary>
/// A switchable part of the app. Most features ship on and are opt-out; a
/// feature that is not proven yet sets <paramref name="EnabledByDefault"/> to
/// false so nobody meets it without asking for it.
/// </summary>
public sealed record AppFeatureDefinition(
    string Key,
    string Name,
    string Description,
    bool AlwaysEnabled = false,
    bool EnabledByDefault = true);

/// <summary>
/// Which features have been switched away from their default. Two sets rather
/// than one, because "not mentioned" has to keep meaning "default" — otherwise
/// a default-off feature added after a settings file was written would silently
/// come on.
/// </summary>
public sealed class AppFeatureSettings
{
    public HashSet<string> DisabledFeatures { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> EnabledFeatures { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AppFeatureSettingsStore
{
    public const string Backlog = "backlog";
    public const string RepositoryKnowledge = "repository-knowledge";
    public const string KnowledgeSections = "knowledge-sections";
    public const string SystemTools = "system-tools";
    public const string AdditionalRepositories = "additional-repositories";
    public const string GitHubIntegration = "github-integration";
    public const string FeedbackReporting = "feedback-reporting";
    public const string CopilotCli = "copilot-cli";
    public const string AiAssistant = "ai-assistant";
    public const string UsageMetrics = "usage-metrics";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public AppFeatureSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog",
            "features.json"))
    {
    }

    public AppFeatureSettingsStore(string path)
    {
        _path = path;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        Current = Read();
    }

    public event Action? Changed;

    public AppFeatureSettings Current { get; private set; }

    public string SettingsPath => _path;

    public static IReadOnlyList<AppFeatureDefinition> Features { get; } =
    [
        new(Backlog, "Backlog", "Create, edit, filter, reorder, and store backlog entries.", AlwaysEnabled: true),
        new(KnowledgeSections, "Knowledge sections", "Show design, architecture, domain, technology, and instruction sections in the knowledge pane and header."),
        new(RepositoryKnowledge, "Repository knowledge", "Show the side pane for repository knowledge."),
        new(AdditionalRepositories, "Additional repositories", "Configure repositories beyond the primary repository and switch repository-specific knowledge."),
        new(SystemTools, "System tools", "Check, update, enable, and disable configured Copilot plugins, repository tools, and MCP servers."),
        new(GitHubIntegration, "GitHub integration", "Configure GitHub access, push entries to issues, and refresh issue or pull request state."),
        new(FeedbackReporting, "Feedback reporting", "Report Desktop app issues to GitHub with current-screen context and a screenshot."),
        new(CopilotCli, "Copilot CLI", "Start GitHub Copilot CLI from Backlog workflows."),
        new(AiAssistant, "AI assistant", "Ask questions about visible backlog content through Azure Foundry."),
        new(
            UsageMetrics,
            "AI usage metrics",
            "Read Claude and GitHub Copilot usage from their organization APIs as evidence for productivity metrics. Both are organization-scoped: Claude needs an Admin API key and GitHub needs organization-owner access.",
            EnabledByDefault: false)
    ];

    public bool IsEnabled(string key)
    {
        var feature = Find(key) ?? throw new ArgumentException($"Unknown feature '{key}'.", nameof(key));

        if (feature.AlwaysEnabled) return true;

        return feature.EnabledByDefault
            ? !Current.DisabledFeatures.Contains(feature.Key)
            : Current.EnabledFeatures.Contains(feature.Key);
    }

    public string? SetEnabled(string key, bool enabled)
    {
        var feature = Find(key);
        if (feature is null)
        {
            return $"Unknown feature '{key}'.";
        }

        if (feature.AlwaysEnabled && !enabled)
        {
            return $"{feature.Name} is always available.";
        }

        var disabled = new HashSet<string>(Current.DisabledFeatures, StringComparer.OrdinalIgnoreCase);
        var explicitlyEnabled = new HashSet<string>(Current.EnabledFeatures, StringComparer.OrdinalIgnoreCase);

        if (feature.EnabledByDefault)
        {
            if (enabled) disabled.Remove(feature.Key);
            else disabled.Add(feature.Key);
        }
        else
        {
            if (enabled) explicitlyEnabled.Add(feature.Key);
            else explicitlyEnabled.Remove(feature.Key);
        }

        return Save(new AppFeatureSettings
        {
            DisabledFeatures = disabled,
            EnabledFeatures = explicitlyEnabled
        });
    }

    private static AppFeatureDefinition? Find(string key) =>
        Features.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

    private string? Save(AppFeatureSettings settings)
    {
        Current = Normalize(settings);

        string? error = null;
        try
        {
            var dto = new FeatureSettingsDto
            {
                DisabledFeatures = [.. Current.DisabledFeatures.Order(StringComparer.OrdinalIgnoreCase)],
                EnabledFeatures = [.. Current.EnabledFeatures.Order(StringComparer.OrdinalIgnoreCase)]
            };

            File.WriteAllText(_path, JsonSerializer.Serialize(dto, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Changed, but the feature choice couldn't be saved for next time.";
        }

        Changed?.Invoke();
        return error;
    }

    private AppFeatureSettings Read()
    {
        try
        {
            if (!File.Exists(_path)) return new AppFeatureSettings();

            var dto = JsonSerializer.Deserialize<FeatureSettingsDto>(File.ReadAllText(_path), JsonOptions);
            if (dto is null) return new AppFeatureSettings();

            return Normalize(new AppFeatureSettings
            {
                DisabledFeatures = new HashSet<string>(dto.DisabledFeatures, StringComparer.OrdinalIgnoreCase),
                EnabledFeatures = new HashSet<string>(dto.EnabledFeatures, StringComparer.OrdinalIgnoreCase)
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppFeatureSettings();
        }
    }

    private static AppFeatureSettings Normalize(AppFeatureSettings settings)
    {
        var switchable = Features.Where(f => !f.AlwaysEnabled).ToList();
        var optOut = switchable.Where(f => f.EnabledByDefault).Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var optIn = switchable.Where(f => !f.EnabledByDefault).Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new AppFeatureSettings
        {
            DisabledFeatures = [.. settings.DisabledFeatures.Where(optOut.Contains)],
            EnabledFeatures = [.. settings.EnabledFeatures.Where(optIn.Contains)]
        };
    }

    private sealed class FeatureSettingsDto
    {
        public string[] DisabledFeatures { get; init; } = [];

        public string[] EnabledFeatures { get; init; } = [];
    }
}
