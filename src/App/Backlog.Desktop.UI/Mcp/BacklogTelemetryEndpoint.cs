using System.Text.Json;

using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// The hook telemetry endpoint beside the MCP one: where the <c>backlog-tools</c>
/// plugin's forwarder posts each Claude Code hook event, for
/// <see cref="IDeliveryRunTelemetry"/> to attribute to the run in progress.
/// <para>
/// Beside <c>/mcp</c> and behind exactly its gate — the same listener, the same
/// <c>Origin</c> check and, in the desktop head, the same bearer token — because it is
/// the same trust boundary: a local session reporting on itself to the application
/// that serves its tools. A second port or a second token would be a second thing to
/// register for no second question answered.
/// </para>
/// <para>
/// Here rather than in either head because both heads map it, and the handling is the
/// part worth testing: <c>Microsoft.AspNetCore.App</c> is referenced by the desktop
/// head alone (local ADR 0012), so each head maps the route and hands the body over.
/// </para>
/// </summary>
public static class BacklogTelemetryEndpoint
{
    /// <summary>Where the endpoint is served, under the listener's address.</summary>
    public const string RoutePath = "/telemetry";

    /// <summary>
    /// The largest body accepted. The forwarder drops the tool's response before
    /// posting, so what is left is the event and the tool's input; a <c>Write</c> of a
    /// large file is the biggest of those, and nothing this endpoint records reads the
    /// content anyway. A bound because the endpoint parses what it is sent.
    /// </summary>
    public const int MaxBodyBytes = 4 * 1024 * 1024;

    /// <summary>Reads one hook event from <paramref name="body"/> and records it,
    /// answering the status code to send: 204 when it was taken, 400 for a body that
    /// is not a JSON object, 413 for one over <see cref="MaxBodyBytes"/>.</summary>
    public static async Task<int> AcceptAsync(
        Stream body,
        long? contentLength,
        IDeliveryRunTelemetry telemetry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(telemetry);

        if (contentLength > MaxBodyBytes) return 413;

        // Read to a bound rather than trusting the header: a chunked body has none.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes) return 413;

            buffer.Write(chunk, 0, read);
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(buffer.ToArray());
        }
        catch (JsonException)
        {
            return 400;
        }

        using (document)
        {
            if (document.RootElement.ValueKind is not JsonValueKind.Object) return 400;

            await telemetry.RecordAsync(document.RootElement, cancellationToken).ConfigureAwait(false);
        }

        return 204;
    }
}
