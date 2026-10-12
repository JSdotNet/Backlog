using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Backlog.Modules.Sessions.UI;

/// <summary>
/// The Claude Code settings block that points its OpenTelemetry logs exporter at this
/// app — what the Settings page shows to paste.
/// <para>
/// The logs signal's own variables rather than the generic ones: a person who already
/// exports metrics somewhere keeps doing so, and the bearer token is sent to this
/// endpoint and to no other. Protobuf, because it is the OTLP default and the smaller of
/// the two encodings the endpoint reads.
/// </para>
/// </summary>
public static class ClaudeCodeTelemetrySnippet
{
    /// <summary>The path OTLP/HTTP fixes for logs, relative to a listener's root.</summary>
    public const string LogsPath = "v1/logs";

    /// <summary>What the page draws in the token's place.</summary>
    public const string HiddenToken = "••••••••";

    /// <summary>The <c>env</c> block, as indented JSON, for <paramref name="endpoint"/>
    /// and — when the host checks one — <paramref name="token"/>.</summary>
    public static string Settings(Uri endpoint, string? token)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var env = new JsonObject
        {
            ["CLAUDE_CODE_ENABLE_TELEMETRY"] = "1",
            ["OTEL_LOGS_EXPORTER"] = "otlp",
            ["OTEL_EXPORTER_OTLP_LOGS_PROTOCOL"] = "http/protobuf",
            ["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"] = endpoint.ToString()
        };

        if (!string.IsNullOrEmpty(token)) env["OTEL_EXPORTER_OTLP_LOGS_HEADERS"] = $"Authorization=Bearer {token}";

        // Relaxed escaping, so the block reads as a person would type it: the default
        // encoder writes the hidden token's dots, and a '+' in a token, as escapes.
        return new JsonObject { ["env"] = env }.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }
}
