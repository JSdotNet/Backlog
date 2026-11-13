using System.Text.Json;
using System.Text.Json.Nodes;

using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Sessions.UI;
using Backlog.SharedKernel.Mcp;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// Retires the shared MCP token (local ADR 0052, Phase 1), once, on the first start after the
/// upgrade. It makes the three grants the app's own installs use in its place — "Backlog
/// delivery", "Question hook" and "Telemetry" — records their ids in <c>settings.json</c>, and
/// rewrites what still names a retired token in Claude Code's own files: the <c>backlog</c>
/// server the Dev PC row registered, and the telemetry block the Settings page had a person
/// paste. Then it drops the retired tokens from <c>settings.json</c>.
/// <para>
/// <b>Only what names a retired token is touched.</b> An entry is rewritten when the bearer it
/// carries is, byte for byte, one of <see cref="WorkspaceSettingsStore.RetiredMcpTokens"/> — that
/// is the entry this app installed — and nothing else in either file changes. The question hook
/// needs no rewrite: the app writes its script on every start, and the script reads its grant
/// from <c>mcpServer.grants.questionHook</c> beside it.
/// </para>
/// <para>
/// <b>Idempotent.</b> The grants are made only while <see cref="WorkspaceSettingsStore.McpServerGrants"/>
/// is unrecorded, so a revoked one stays revoked; the rewrite runs only while a retired token is
/// still recorded, and a failed write keeps it recorded so the next start tries again.
/// </para>
/// </summary>
public static class McpTokenRetirement
{
    /// <summary>The delivery grant's name on the Agents card.</summary>
    public const string DeliveryGrantName = "Backlog delivery";

    /// <summary>The question hook's grant's name.</summary>
    public const string QuestionHookGrantName = "Question hook";

    /// <summary>The telemetry exporter's grant's name.</summary>
    public const string TelemetryGrantName = "Telemetry";

    /// <summary>What the three grants record as their creator.</summary>
    public const string CreatedBy = "Backlog, in place of the shared token";

    /// <summary>The delivery grant's scopes: the Delivery flow role's preset.</summary>
    public static IReadOnlyList<string> DeliveryScopes { get; } = McpAgents.PresetOf("Delivery flow")
        ?? throw new InvalidOperationException("McpAgents has no Delivery flow role.");

    /// <summary>The two environment keys a pasted telemetry block carried the token in.</summary>
    private static readonly string[] OtelHeaderKeys = ["OTEL_EXPORTER_OTLP_LOGS_HEADERS", "OTEL_EXPORTER_OTLP_HEADERS"];

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        // Written back as Claude Code writes it: no escapes for quotes, angle brackets or
        // non-ASCII text that a person's own entries carry.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>The server name the Dev PC row registers Backlog under.</summary>
    private const string DevPcServerName = "backlog";

    /// <summary>
    /// The three grants, made and recorded when <paramref name="settings"/> has none recorded,
    /// else the recorded ones — revoked or not, they are the person's to keep or revoke.
    /// </summary>
    public static McpInstalledGrants EnsureGrants(WorkspaceSettingsStore settings, IMcpGrantStore grants)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(grants);

        if (settings.McpServerGrants is { } recorded) return recorded;

        var made = new McpInstalledGrants(
            grants.Create(DeliveryGrantName, DeliveryScopes, CreatedBy).Id,
            grants.Create(QuestionHookGrantName, [McpScopes.NotifyAsk], CreatedBy).Id,
            grants.Create(TelemetryGrantName, [McpScopes.TelemetryWrite], CreatedBy).Id);

        _ = settings.SetMcpServerGrants(made);
        return made;
    }

    /// <summary>
    /// Makes the grants when none are recorded, rewrites what names a retired token in
    /// <paramref name="files"/> to run the installed headers helper at
    /// <paramref name="helperScriptPath"/>, and drops the retired tokens once both files are done.
    /// Answers what it did and every file it could not change.
    /// </summary>
    public static McpTokenRetirementResult Run(
        WorkspaceSettingsStore settings,
        IMcpGrantStore grants,
        ClaudeConfigFiles files,
        string helperScriptPath,
        bool debug)
    {
        ArgumentNullException.ThrowIfNull(files);

        var installed = EnsureGrants(settings, grants);
        var retired = settings.RetiredMcpTokens;

        if (retired.Count == 0) return new McpTokenRetirementResult(installed, 0, false, []);

        var problems = new List<string>();

        var registrations = 0;
        Edit(files.UserConfigPath, root => (registrations = RewriteRegistrations(
            root, retired, McpHeadersHelperScript.Command(helperScriptPath, installed.Delivery, debug))) > 0, problems);

        var telemetry = false;
        Edit(files.UserSettingsPath, root => telemetry = RewriteTelemetry(
            root, retired, McpHeadersHelperScript.Command(helperScriptPath, installed.Telemetry, debug)), problems);

        if (problems.Count == 0 && settings.ForgetRetiredMcpTokens() is { } kept) problems.Add(kept);

        return new McpTokenRetirementResult(installed, registrations, telemetry, problems);
    }

    /// <summary>
    /// Points every <c>backlog</c> server in a Claude Code configuration whose
    /// <c>Authorization</c> header carries a retired token at <paramref name="headersHelper"/>
    /// instead: the user-scope one in <c>mcpServers</c> and any project-scope one under
    /// <c>projects</c>. The header goes, the other headers stay. Answers how many it rewrote.
    /// </summary>
    internal static int RewriteRegistrations(JsonObject root, IReadOnlyList<string> retired, string headersHelper)
    {
        ArgumentNullException.ThrowIfNull(root);

        var servers = new List<JsonObject?> { root["mcpServers"] as JsonObject };
        if (root["projects"] is JsonObject projects)
        {
            servers.AddRange(projects.Select(project => (project.Value as JsonObject)?["mcpServers"] as JsonObject));
        }

        var rewritten = 0;

        foreach (var server in servers.OfType<JsonObject>().Select(registered => registered[DevPcServerName] as JsonObject).OfType<JsonObject>())
        {
            if (server["headers"] is not JsonObject headers) continue;

            var authorization = headers.FirstOrDefault(header => string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase));
            if (authorization.Value is not JsonValue value || !value.TryGetValue<string>(out var bearer)) continue;
            if (!retired.Any(token => McpLoopbackGuard.IsAuthorized(bearer, token))) continue;

            headers.Remove(authorization.Key);
            if (headers.Count == 0) server.Remove("headers");
            server["headersHelper"] = headersHelper;
            rewritten++;
        }

        return rewritten;
    }

    /// <summary>
    /// Swaps a Claude Code settings file's telemetry header for <paramref name="headersHelper"/>,
    /// when the header carries a retired token: the <c>OTEL_EXPORTER_OTLP_*HEADERS</c> entry goes,
    /// <c>otelHeadersHelper</c> runs the helper for the telemetry grant, and
    /// <c>CLAUDE_CODE_OTEL_HEADERS_HELPER_DEBOUNCE_MS</c> keeps the token it prints fresh. An
    /// <c>env</c> <c>BACKLOG_MCP_TOKEN</c> holding a retired token goes too. Answers whether it
    /// changed anything.
    /// </summary>
    internal static bool RewriteTelemetry(JsonObject root, IReadOnlyList<string> retired, string headersHelper)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (root["env"] is not JsonObject env) return false;

        // A BACKLOG_MCP_TOKEN copied in by hand from settings.json: a retired secret left on disk,
        // which every Backlog client would send ahead of nothing. It goes, whatever else is here.
        var copied = env[WorkspaceSettingsStore.EnvironmentTokenVariable] is JsonValue environment
            && environment.TryGetValue<string>(out var copiedToken)
            && retired.Contains(copiedToken.Trim(), StringComparer.Ordinal)
            && env.Remove(WorkspaceSettingsStore.EnvironmentTokenVariable);

        var removed = false;
        foreach (var key in OtelHeaderKeys)
        {
            if (env[key] is not JsonValue value || !value.TryGetValue<string>(out var header)) continue;
            if (!retired.Any(token => header.Contains("Bearer " + token, StringComparison.Ordinal))) continue;

            env.Remove(key);
            removed = true;
        }

        if (!removed) return copied;

        root["otelHeadersHelper"] = headersHelper;
        env[ClaudeCodeTelemetrySnippet.HeadersRefreshKey] =
            ClaudeCodeTelemetrySnippet.HeadersRefreshMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>Reads <paramref name="path"/>, runs <paramref name="edit"/> on it and writes it back
    /// when it answers true, through a temporary file. A file that is not there has nothing to
    /// rewrite; one that does not parse, or cannot be written, is a problem.</summary>
    private static void Edit(string path, Func<JsonObject, bool> edit, List<string> problems)
    {
        if (!File.Exists(path)) return;

        try
        {
            if (JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is not JsonObject root)
            {
                problems.Add($"{path} is not a JSON object, so what in it still names the retired MCP token was left as it was.");
                return;
            }

            if (!edit(root)) return;

            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporary, root.ToJsonString(Indented));
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
#pragma warning disable CA1031 // Whatever stops the rewrite, the app starts; the next start tries again.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            problems.Add($"{path} could not be rewritten: {ex.Message}");
        }
    }
}

/// <summary>Claude Code's two files the migration may rewrite: <c>~/.claude.json</c>, where the
/// user-scope <c>backlog</c> server is registered, and <c>~/.claude/settings.json</c>, where a
/// telemetry block is pasted.</summary>
public sealed record ClaudeConfigFiles(string UserConfigPath, string UserSettingsPath)
{
    /// <summary>The files of this Windows user: under <c>CLAUDE_CONFIG_DIR</c> when it is set, as
    /// Claude Code reads it, else the home folder.</summary>
    public static ClaudeConfigFiles ForCurrentUser()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");

        return string.IsNullOrWhiteSpace(configured)
            ? new ClaudeConfigFiles(Path.Combine(home, ".claude.json"), Path.Combine(home, ".claude", "settings.json"))
            : new ClaudeConfigFiles(Path.Combine(configured, ".claude.json"), Path.Combine(configured, "settings.json"));
    }
}

/// <summary>What one run of <see cref="McpTokenRetirement.Run"/> did.</summary>
/// <param name="Grants">The three grants, made now or recorded before.</param>
/// <param name="RegistrationsRewritten">How many <c>backlog</c> registrations now run the headers helper.</param>
/// <param name="TelemetryRewritten">Whether the telemetry block now runs it.</param>
/// <param name="Problems">Every file left as it was, and why; the retired tokens stay recorded while there is one.</param>
public sealed record McpTokenRetirementResult(
    McpInstalledGrants Grants,
    int RegistrationsRewritten,
    bool TelemetryRewritten,
    IReadOnlyList<string> Problems);
