using System.Text.Json;
using System.Text.RegularExpressions;

namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// What a run's stage executions say about the session that owned the run.
/// <para>
/// A stage's <see cref="DeliveryRunStage.Execution"/> is the object <c>update_stage</c>
/// last sent for it — how the stage ran, inline or delegated, to which agent, on which
/// model and effort. A stage that ran <b>inline</b> ran in the owner session, so the
/// effort it recorded is the owner session's own; a delegated or forked stage's effort
/// is its sub-agent's and says nothing about the session. This is the one place that
/// reads the effort out of that object, so the row and the detail panel cannot read
/// it two ways.
/// </para>
/// <para>
/// Telemetry is the better source — Claude Code reports the effort on every request —
/// so <see cref="TelemetryEffort"/> is read first wherever a session's requests are
/// in, and the run file is the fallback. With neither, a surface says "not recorded"
/// rather than assuming a default.
/// </para>
/// </summary>
public static class DeliveryRunExecutions
{
    /// <summary>
    /// The effort the owner session ran at, as its own requests reported it: the most
    /// recent main-loop request of <paramref name="sessionId"/> that carried an effort,
    /// or null where none did.
    /// <para>
    /// Main loop only, because a sub-agent's request carries the sub-agent's effort and
    /// says nothing about the session — the same reason <see cref="OwnerEffort"/> reads
    /// only inline stages. Most recent, because the effort can be changed while the
    /// session runs.
    /// </para>
    /// <para>
    /// The main loop is a request whose <c>query_source</c> is the interactive loop's
    /// (<c>repl_main_thread</c>) or the SDK's (<c>sdk</c>); a side query Claude Code makes
    /// for itself — a title, a compaction, a classifier — is not the session's effort.
    /// Where <paramref name="model"/> is given, only requests on that model count, since
    /// such a side query can also run on a small model under the <c>sdk</c> source.
    /// </para>
    /// </summary>
    public static string? TelemetryEffort(string? sessionId, IEnumerable<ClaudeApiRequest> requests, string? model = null)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (string.IsNullOrWhiteSpace(sessionId)) return null;

        var label = string.IsNullOrWhiteSpace(model) ? null : AgentModels.Label(model);

        return requests
            .Where(request => string.Equals(request.SessionId, sessionId, StringComparison.Ordinal)
                && IsMainLoop(request)
                && (label is null || (request.Model is { } used && string.Equals(AgentModels.Label(used), label, StringComparison.OrdinalIgnoreCase)))
                && !string.IsNullOrWhiteSpace(request.Effort))
            .OrderByDescending(request => request.Timestamp)
            .Select(request => request.Effort!.Trim())
            .FirstOrDefault();
    }

    private static bool IsMainLoop(ClaudeApiRequest request) =>
        DeliveryRunCosts.SubAgentOf(request) is null
        && request.QuerySource?.Trim() is { } source
        && (source.StartsWith("repl_main_thread", StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, "sdk", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The effort the owner session ran at, as the latest inline stage that recorded
    /// one says, or null where none did.
    /// <para>
    /// Latest, because a session's effort can be changed while it runs and the most
    /// recent stage is the nearest thing to "now". A stage that names no mode and no
    /// agent is read as inline: it ran where the run did. Anything that is not an
    /// object, or not JSON at all, is no record.
    /// </para>
    /// </summary>
    public static string? OwnerEffort(DeliveryRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        for (var index = run.Stages.Count - 1; index >= 0; index--)
        {
            if (InlineEffort(run.Stages[index].Execution) is { } effort) return effort;
        }

        return null;
    }

    private static string? InlineEffort(string? execution)
    {
        if (string.IsNullOrWhiteSpace(execution)) return null;

        try
        {
            using var document = JsonDocument.Parse(execution);
            var root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object) return null;

            var mode = Text(root, "mode");
            var inline = mode is not null
                ? string.Equals(mode, "inline", StringComparison.OrdinalIgnoreCase)
                : Text(root, "agent") is null;

            return inline ? Text(root, "effort") : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.String
        && value.GetString() is { } text
        && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
}

/// <summary>
/// What a model is called on screen.
/// </summary>
public static partial class AgentModels
{
    /// <summary>
    /// A Claude model id as a reader names it — <c>claude-opus-5-5</c> reads "Opus 5.5",
    /// a dated snapshot drops its date, a bare alias is capitalised, and a context
    /// suffix such as <c>[1m]</c> stays. Any other id is returned as written: guessing
    /// at another vendor's naming would be inventing a name.
    /// </summary>
    public static string Label(string model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var match = ClaudeId().Match(model.Trim());
        if (!match.Success) return model.Trim();

        var family = match.Groups["family"].Value;
        var name = char.ToUpperInvariant(family[0]) + family[1..].ToLowerInvariant();
        var version = match.Groups["version"].Success
            ? " " + string.Join('.', match.Groups["version"].Value.Split('-', StringSplitOptions.RemoveEmptyEntries))
            : string.Empty;
        var suffix = match.Groups["suffix"].Success ? " " + match.Groups["suffix"].Value : string.Empty;

        return name + version + suffix;
    }

    [GeneratedRegex(@"^(?:claude-)?(?<family>opus|sonnet|haiku|fable)(?:-(?<version>\d{1,2}(?:-\d{1,2})?))?(?:-\d{8})?(?<suffix>\[[^\]]+\])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ClaudeId();
}
