using System.IO.Compression;

using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// The OTLP/HTTP logs endpoint beside the MCP one (local ADR 0024): where Claude Code's
/// OpenTelemetry exporter posts its events, of which the <c>claude_code.api_request</c>
/// ones are kept in the app's local database through <see cref="IClaudeApiRequestStore"/>.
/// <para>
/// On the MCP listener and behind exactly its guard — loopback only, the <c>Origin</c>
/// check and, in the desktop head, the bearer token — for the reason
/// <see cref="BacklogTelemetryEndpoint"/> gives: a local session reporting on itself
/// to the application that serves its tools is one trust boundary, and a second port
/// would be a second thing to register for no second question answered.
/// </para>
/// <para>
/// Here rather than in either head because both heads map it, and the handling is the
/// part worth testing; each head maps the route and writes the <see cref="Reply"/>.
/// </para>
/// </summary>
public static class ClaudeCodeLogsEndpoint
{
    /// <summary>Where the endpoint is served — the path OTLP/HTTP fixes for logs, so
    /// an exporter given only the listener's address finds it too.</summary>
    public const string RoutePath = "/v1/logs";

    /// <summary>The largest body accepted, before and after decompression. Claude
    /// Code's batches run to tens of kilobytes; the bound is there because the endpoint
    /// parses what it is sent.</summary>
    public const int MaxBodyBytes = 8 * 1024 * 1024;

    /// <summary>What to answer: the status, and the body an OTLP exporter reads as an
    /// <c>ExportLogsServiceResponse</c> — an empty one, in the encoding it was sent in —
    /// and, for a failure worth retrying, how many seconds to wait before the retry and
    /// the exception behind it, for the head to log: a fault that persists would
    /// otherwise retry for ever and leave no trace.</summary>
    public readonly record struct Reply(int StatusCode, string? ContentType, byte[] Body, int? RetryAfterSeconds = null, Exception? Failure = null)
    {
        internal static Reply Status(int statusCode) => new(statusCode, null, []);
    }

    /// <summary>How long an exporter is asked to wait after the database refused a
    /// batch: long enough for a moved workspace or a busy file to settle.</summary>
    public const int RetryAfterSeconds = 5;

    /// <summary>The URL a person pastes into Claude Code, for a listener at
    /// <paramref name="listener"/> — its scheme, host and port, and <see cref="RoutePath"/>.</summary>
    public static Uri LogsEndpoint(Uri listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        return new Uri(new Uri(listener.GetLeftPart(UriPartial.Authority)), RoutePath);
    }

    /// <summary>
    /// Reads one export request and stores the api_request events in it.
    /// <list type="bullet">
    /// <item>200 with an empty response when it was read, whether or not anything in
    /// it was kept — dropping the other events is this endpoint's job, not a
    /// failure.</item>
    /// <item>415 for an encoding that is neither OTLP/JSON nor OTLP/protobuf.</item>
    /// <item>400 for a body that does not read in the encoding it claimed.</item>
    /// <item>413 for one over <see cref="MaxBodyBytes"/>.</item>
    /// <item>503 with a <c>Retry-After</c> when the database could not take the batch:
    /// OTLP/HTTP retries a 503 and not a 500, and storing is idempotent by
    /// <c>request_id</c>, so the retry is safe and the batch is not lost.</item>
    /// </list>
    /// </summary>
    public static async Task<Reply> AcceptAsync(
        Stream body,
        long? contentLength,
        string? contentType,
        string? contentEncoding,
        IClaudeApiRequestStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(store);

        var encoding = EncodingOf(contentType);

        if (encoding is BodyEncoding.Unsupported) return Reply.Status(415);

        // An absent or empty header is no encoding; x-gzip is gzip's old name.
        var coding = contentEncoding?.Trim() ?? "";
        var gzip = coding.Equals("gzip", StringComparison.OrdinalIgnoreCase)
            || coding.Equals("x-gzip", StringComparison.OrdinalIgnoreCase);

        if (coding.Length > 0 && !gzip && !coding.Equals("identity", StringComparison.OrdinalIgnoreCase))
        {
            return Reply.Status(415);
        }

        if (contentLength > MaxBodyBytes) return Reply.Status(413);

        byte[]? bytes;

        try
        {
            if (gzip)
            {
                await using var inflated = new GZipStream(body, CompressionMode.Decompress, leaveOpen: true);
                bytes = await ReadBoundedAsync(inflated, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                bytes = await ReadBoundedAsync(body, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (InvalidDataException)
        {
            return Reply.Status(400);
        }

        if (bytes is null) return Reply.Status(413);

        IReadOnlyList<ClaudeApiRequest> requests;

        try
        {
            requests = encoding is BodyEncoding.Json
                ? ClaudeCodeOtlpLogs.ReadJson(bytes)
                : ClaudeCodeOtlpLogs.ReadProtobuf(bytes);
        }
        catch (FormatException)
        {
            return Reply.Status(400);
        }

        if (requests.Count > 0)
        {
            try
            {
                await store.AddAsync(requests, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new Reply(503, null, [], RetryAfterSeconds, ex);
            }
        }

        return encoding is BodyEncoding.Json
            ? new Reply(200, "application/json", "{}"u8.ToArray())
            : new Reply(200, "application/x-protobuf", []);
    }

    private enum BodyEncoding
    {
        Unsupported,
        Json,
        Protobuf
    }

    private static BodyEncoding EncodingOf(string? contentType)
    {
        var mediaType = contentType?.Split(';', 2)[0].Trim();

        return mediaType?.ToLowerInvariant() switch
        {
            "application/json" => BodyEncoding.Json,
            "application/x-protobuf" or "application/protobuf" => BodyEncoding.Protobuf,
            _ => BodyEncoding.Unsupported
        };
    }

    /// <summary>Reads to a bound rather than trusting the header: a chunked or
    /// compressed body has none worth believing. Null when the bound was passed.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(Stream source, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes) return null;

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
