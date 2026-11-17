using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Backlog.Modules.Sessions.UI;

/// <summary>
/// The Claude Code settings block that points its OpenTelemetry logs exporter at this
/// app — what the Settings page shows to paste.
/// <para>
/// The logs signal's own variables rather than the generic ones: a person who already
/// exports metrics somewhere keeps doing so. Protobuf, because it is the OTLP default and
/// the smaller of the two encodings the endpoint reads, and because Claude Code applies an
/// <c>otelHeadersHelper</c>'s headers to the HTTP protocols alone.
/// </para>
/// <para>
/// <b>No token in it.</b> Since the shared tokens were retired (local ADR 0052, Phase 1) the
/// block names a command, <c>otelHeadersHelper</c>, that asks the app's token broker for a
/// short-lived token for the Telemetry grant — so the block is no secret, and a copy of it on
/// another machine grants nothing there.
/// </para>
/// </summary>
public static class ClaudeCodeTelemetrySnippet
{
    /// <summary>The path OTLP/HTTP fixes for logs, relative to a listener's root.</summary>
    public const string LogsPath = "v1/logs";

    /// <summary>The environment key that sets how often Claude Code reruns
    /// <c>otelHeadersHelper</c>.</summary>
    public const string HeadersRefreshKey = "CLAUDE_CODE_OTEL_HEADERS_HELPER_DEBOUNCE_MS";

    /// <summary>How often it reruns it here: under the 15-minute life of a grant token, where
    /// Claude Code's own default of 29 minutes would leave every export refused for the second
    /// half of each interval.</summary>
    public const int HeadersRefreshMilliseconds = 10 * 60 * 1000;

    /// <summary>The block, as indented JSON, for <paramref name="endpoint"/> and — when the host
    /// asks for a token — the <paramref name="headersHelper"/> command that fetches one.</summary>
    public static string Settings(Uri endpoint, string? headersHelper)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var env = new JsonObject
        {
            ["CLAUDE_CODE_ENABLE_TELEMETRY"] = "1",
            ["OTEL_LOGS_EXPORTER"] = "otlp",
            ["OTEL_EXPORTER_OTLP_LOGS_PROTOCOL"] = "http/protobuf",
            ["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"] = endpoint.ToString(),
            // Without it Claude Code redacts each request's agent.name and skill.name,
            // so no request can be told to a stage's sub-agent and a stage's reported
            // cost would be its main loop alone.
            ["OTEL_LOG_TOOL_DETAILS"] = "1"
        };

        var root = new JsonObject();

        if (!string.IsNullOrEmpty(headersHelper))
        {
            env[HeadersRefreshKey] = HeadersRefreshMilliseconds.ToString(CultureInfo.InvariantCulture);
            root["otelHeadersHelper"] = headersHelper;
        }

        root["env"] = env;

        // Relaxed escaping, so the block reads as a person would type it: the default encoder
        // writes the command's quotes as escapes.
        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }
}
