using System.Text.Json;

using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Keeps the reader's Inbox routing rules — which items to suggest which
/// repository for — in a JSON file next to the app's other per-user settings.
/// <para>
/// Its own file, for the reason <see cref="CaptureSourcesSettingsStore"/> gives
/// for having one: <c>settings.json</c> is the pointer to the workspace, and the
/// rules change far more often than the pointer moves. Local to the machine and
/// never synced, like the organiser the rules sit beside.
/// </para>
/// </summary>
public sealed class InboxRoutingRulesStore : IInboxRoutingRules
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public InboxRoutingRulesStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Backlog",
                "inbox-routing-rules.json"))
    {
    }

    /// <summary>Names the file separately from the per-user location, for a test
    /// or for the web harness, which keeps its settings beside its content root.</summary>
    public InboxRoutingRulesStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Current = Read();
    }

    public event Action? Changed;

    public IReadOnlyList<InboxRoutingRule> Current { get; private set; }

    public string SettingsPath => _path;

    public string? SetRules(string? text)
    {
        var (rules, error) = InboxRoutingRule.Parse(text);
        if (error is not null) return error;

        if (rules.SequenceEqual(Current)) return null;

        Current = rules;

        string? saveError = null;
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(new InboxRoutingRulesDto
            {
                Rules = [.. rules.Select(rule => new InboxRoutingRuleDto { Pattern = rule.Pattern, Repository = rule.Repository })]
            }, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            saveError = "Changed, but the routing rules couldn't be saved for next time.";
        }

        Changed?.Invoke();
        return saveError;
    }

    private IReadOnlyList<InboxRoutingRule> Read()
    {
        try
        {
            if (!File.Exists(_path)) return [];

            var dto = JsonSerializer.Deserialize<InboxRoutingRulesDto>(File.ReadAllText(_path), JsonOptions);
            if (dto is null) return [];

            // Read back through the same grammar the field is held to, one rule at
            // a time, so a line somebody broke by hand drops that rule rather than
            // every rule in the file.
            return
            [
                .. dto.Rules
                    .Select(rule => InboxRoutingRule.Parse($"{rule.Pattern} {InboxRoutingRule.Arrow} {rule.Repository}").Rules)
                    .SelectMany(parsed => parsed)
                    .Distinct()
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreachable file must never stop the app from opening:
            // no rules is what an absent file means too.
            return [];
        }
    }

    private sealed class InboxRoutingRulesDto
    {
        public List<InboxRoutingRuleDto> Rules { get; set; } = [];
    }

    private sealed class InboxRoutingRuleDto
    {
        public string Pattern { get; set; } = string.Empty;

        public string Repository { get; set; } = string.Empty;
    }
}
