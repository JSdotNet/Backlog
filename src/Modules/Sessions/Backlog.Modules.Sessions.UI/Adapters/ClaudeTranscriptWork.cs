using System.Text.Json;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UI.Adapters;

/// <summary>
/// What a Claude transcript says about the work a session did, gathered on the same
/// pass that counts its turns: where it ran, the pull requests it linked, and the tokens
/// it spent per model.
/// <para>
/// Every line is tested with a substring before it is parsed, the way the turn count is:
/// three lines in four are none of these, and parsing every line of a large transcript is
/// the cost the facts cache exists to pay once rather than on every read.
/// </para>
/// </summary>
internal sealed class ClaudeTranscriptWork
{
    private readonly Dictionary<string, AgentPullRequest> _pullRequests = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The last usage seen per message id — the lines of one message repeat it.</summary>
    private readonly Dictionary<string, (string Model, long Input, long Output, long CacheCreation, long CacheRead)> _messages = new(StringComparer.Ordinal);

    public string? Entrypoint { get; private set; }

    public void Observe(string line)
    {
        if (line.Length == 0 || line[0] is not '{') return;

        var entrypoint = Entrypoint is null && line.Contains("\"entrypoint\":\"", StringComparison.Ordinal);
        var pullRequest = line.Contains("\"type\":\"pr-link\"", StringComparison.Ordinal);
        var usage = line.Contains("\"type\":\"assistant\"", StringComparison.Ordinal) && line.Contains("\"usage\":{", StringComparison.Ordinal);

        if (!entrypoint && !pullRequest && !usage) return;

        try
        {
            using var document = JsonDocument.Parse(line);

            var root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object) return;

            if (entrypoint) Entrypoint = Text(root, "entrypoint");
            if (pullRequest) PullRequest(root);
            if (usage) Usage(root);
        }
        catch (JsonException)
        {
            // A half-written last line: it costs this line's facts and nothing else.
        }
    }

    public IReadOnlyList<AgentPullRequest> PullRequests =>
        [.. _pullRequests.Values.OrderBy(pullRequest => pullRequest.LinkedAt ?? DateTimeOffset.MaxValue)];

    public IReadOnlyList<AgentModelUsage> ModelUsage =>
    [
        .. _messages.Values
            .GroupBy(message => message.Model, StringComparer.Ordinal)
            .Select(model => new AgentModelUsage(
                model.Key,
                model.Sum(message => message.Input),
                model.Sum(message => message.Output),
                model.Sum(message => message.CacheCreation),
                model.Sum(message => message.CacheRead)))
            .OrderBy(model => model.Model, StringComparer.Ordinal)
    ];

    private void PullRequest(JsonElement root)
    {
        if (Text(root, "type") is not "pr-link") return;

        var url = Text(root, "prUrl");
        var repository = Text(root, "prRepository");

        if (url is null || repository is null) return;
        if (!root.TryGetProperty("prNumber", out var number) || number.ValueKind is not JsonValueKind.Number || !number.TryGetInt32(out var value)) return;

        var at = root.TryGetProperty("timestamp", out var stamp) && stamp.ValueKind is JsonValueKind.String && stamp.TryGetDateTimeOffset(out var parsed)
            ? parsed
            : (DateTimeOffset?)null;

        // The first time a pull request was linked is when the session took it on; a
        // later line naming it again is the same link.
        _pullRequests.TryAdd(url, new AgentPullRequest(repository, value, url, at));
    }

    private void Usage(JsonElement root)
    {
        if (Text(root, "type") is not "assistant") return;

        // A sidechain's spend is its own agent's, and written to its own transcript too.
        if (root.TryGetProperty("isSidechain", out var sidechain) && sidechain.ValueKind is JsonValueKind.True) return;

        if (!root.TryGetProperty("message", out var message) || message.ValueKind is not JsonValueKind.Object) return;

        // "<synthetic>" is the assistant's own bookkeeping — a refusal, an interruption —
        // and every figure on it is zero.
        var model = Text(message, "model");
        var id = Text(message, "id");

        if (model is null || id is null || model.StartsWith('<')) return;
        if (!message.TryGetProperty("usage", out var usage) || usage.ValueKind is not JsonValueKind.Object) return;

        _messages[id] = (
            model,
            Count(usage, "input_tokens"),
            Count(usage, "output_tokens"),
            Count(usage, "cache_creation_input_tokens"),
            Count(usage, "cache_read_input_tokens"));
    }

    private static long Count(JsonElement usage, string name) =>
        usage.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Number && value.TryGetInt64(out var count) ? count : 0;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}
