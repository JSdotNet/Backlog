using System.IO.Compression;
using System.Text;

using Backlog.Desktop.UI.Mcp;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The OTLP logs reader and the <c>/v1/logs</c> handling over it (local ADR 0024).
/// <para>
/// The fixtures under <c>tests/Fixtures/claude-code-otlp</c> are what Claude Code
/// 2.1.293 actually posted — one export in each encoding, its identity values
/// overwritten length for length — with two <c>claude_code.api_request</c> records
/// added, because the captured session made no request that succeeded. Everything else
/// in them is a real event this reader has to drop.
/// </para>
/// </summary>
public sealed class ClaudeCodeOtlpLogsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "claude-code-otlp", name));

    private static readonly ClaudeApiRequest First = new(
        "req_011CYfixture0000000000001",
        "ae81a408-527a-4a84-b181-d77fdda707e1",
        new DateTimeOffset(2026, 10, 7, 22, 51, 13, 536, TimeSpan.Zero),
        "claude-haiku-5-5",
        "medium",
        4521,
        12,
        87,
        18342,
        2210,
        1834,
        "sdk",
        null,
        null,
        "ca2e2b77-95d0-496d-ad14-0f3704fcbda7");

    private static readonly ClaudeApiRequest Second = new(
        "req_011CYfixture0000000000002",
        "ae81a408-527a-4a84-b181-d77fdda707e1",
        new DateTimeOffset(2026, 10, 7, 22, 51, 16, 236, TimeSpan.Zero),
        "claude-opus-5-5",
        "high",
        193755,
        3,
        1420,
        40211,
        15876,
        9120,
        "agent:custom",
        "Explore",
        "delivery:phase-review",
        "ca2e2b77-95d0-496d-ad14-0f3704fcbda7");

    [Fact]
    public void The_captured_json_export_yields_its_api_requests_and_nothing_else()
    {
        var requests = ClaudeCodeOtlpLogs.ReadJson(Fixture("claude-code-logs.json"));

        Assert.Equal([First, Second], requests);
    }

    [Fact]
    public void The_captured_protobuf_export_yields_the_same_api_requests()
    {
        var requests = ClaudeCodeOtlpLogs.ReadProtobuf(Fixture("claude-code-logs.pb"));

        Assert.Equal([First, Second], requests);
    }

    private static string Export(string records) =>
        $$"""{"resourceLogs":[{"resource":{},"scopeLogs":[{"scope":{"name":"com.anthropic.claude_code.events"},"logRecords":[{{records}}]}]}]}""";

    private static string Record(string attributes, string body = "claude_code.api_request", string extra = "") =>
        $$"""{"timeUnixNano":"1791413472036000000"{{extra}},"body":{"stringValue":"{{body}}"},"attributes":[{{attributes}}]}""";

    private static string Attr(string key, string value) => $$$"""{"key":"{{{key}}}","value":{{{{value}}}}}""";

    [Fact]
    public void A_request_without_a_request_id_is_dropped_because_it_could_not_be_stored_once()
    {
        var json = Export(Record(Attr("model", "\"stringValue\":\"m\"")));

        Assert.Empty(ClaudeCodeOtlpLogs.ReadJson(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Another_event_with_a_request_id_is_still_dropped()
    {
        var json = Export(Record(Attr("request_id", "\"stringValue\":\"r\""), body: "claude_code.api_error"));

        Assert.Empty(ClaudeCodeOtlpLogs.ReadJson(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void The_event_is_recognised_by_its_event_name_attribute_or_field_as_well_as_its_body()
    {
        var json = Export(string.Join(',',
            Record(Attr("request_id", "\"stringValue\":\"by-attribute\"") + "," + Attr("event.name", "\"stringValue\":\"api_request\""), body: "something"),
            Record(Attr("request_id", "\"stringValue\":\"by-field\""), body: "something", extra: ",\"eventName\":\"claude_code.api_request\"")));

        var requests = ClaudeCodeOtlpLogs.ReadJson(Encoding.UTF8.GetBytes(json));

        Assert.Equal(["by-attribute", "by-field"], requests.Select(r => r.RequestId));
    }

    [Fact]
    public void Integers_sent_as_strings_and_cost_sent_only_in_dollars_are_read()
    {
        var json = Export(Record(string.Join(',',
            Attr("request_id", "\"stringValue\":\"r\""),
            Attr("input_tokens", "\"intValue\":\"1200\""),
            Attr("cost_usd", "\"doubleValue\":0.0123456"))));

        var request = Assert.Single(ClaudeCodeOtlpLogs.ReadJson(Encoding.UTF8.GetBytes(json)));

        Assert.Equal(1200, request.InputTokens);
        Assert.Equal(12346, request.CostUsdMicros);
        Assert.Null(request.OutputTokens);
    }

    [Fact]
    public void Without_event_timestamp_the_record_s_own_time_dates_it()
    {
        var json = Export(Record(Attr("request_id", "\"stringValue\":\"r\"")));

        var request = Assert.Single(ClaudeCodeOtlpLogs.ReadJson(Encoding.UTF8.GetBytes(json)));

        Assert.Equal(new DateTimeOffset(2026, 10, 7, 22, 51, 12, 36, TimeSpan.Zero), request.Timestamp);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    public void A_body_that_is_not_an_export_request_is_a_format_error(string body) =>
        Assert.Throws<FormatException>(() => ClaudeCodeOtlpLogs.ReadJson(Encoding.UTF8.GetBytes(body)));

    [Fact]
    public void A_json_export_with_a_byte_order_mark_is_read()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Fixture("claude-code-logs.json")).ToArray();

        Assert.Equal([First, Second], ClaudeCodeOtlpLogs.ReadJson(bytes));
    }

    [Fact]
    public void Anything_after_the_json_export_is_a_format_error() =>
        Assert.Throws<FormatException>(() => ClaudeCodeOtlpLogs.ReadJson(Encoding.UTF8.GetBytes("""{"resourceLogs":[]} trailing""")));

    [Fact]
    public void A_tag_beyond_the_largest_field_number_is_a_format_error()
    {
        // Field number 2^29, wire type 0, as a varint, then a zero value.
        byte[] tag = [0x80, 0x80, 0x80, 0x80, 0x10, 0x00];

        Assert.Throws<FormatException>(() => ClaudeCodeOtlpLogs.ReadProtobuf(tag));
    }

    [Fact]
    public void A_truncated_protobuf_export_is_a_format_error()
    {
        var bytes = Fixture("claude-code-logs.pb");

        Assert.Throws<FormatException>(() => ClaudeCodeOtlpLogs.ReadProtobuf(bytes.AsSpan(0, bytes.Length - 7)));
    }

    // --- The endpoint ------------------------------------------------------------

    private sealed class RecordingStore : IClaudeApiRequestStore
    {
        public List<ClaudeApiRequest> Stored { get; } = [];

        public Task<int> AddAsync(IReadOnlyList<ClaudeApiRequest> requests, CancellationToken cancellationToken = default)
        {
            Stored.AddRange(requests);
            return Task.FromResult(requests.Count);
        }

        public Task<IReadOnlyList<ClaudeApiRequest>> ListAsync(string? sessionId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ClaudeApiRequest>>(Stored);
    }

    private static Task<ClaudeCodeLogsEndpoint.Reply> Post(byte[] body, string? contentType, IClaudeApiRequestStore store, string? encoding = null) =>
        ClaudeCodeLogsEndpoint.AcceptAsync(new MemoryStream(body), body.Length, contentType, encoding, store, Ct);

    [Fact]
    public async Task A_json_export_is_stored_and_answered_with_an_empty_json_response()
    {
        var store = new RecordingStore();

        var reply = await Post(Fixture("claude-code-logs.json"), "application/json; charset=utf-8", store);

        Assert.Equal(200, reply.StatusCode);
        Assert.Equal("application/json", reply.ContentType);
        Assert.Equal("{}", Encoding.UTF8.GetString(reply.Body));
        Assert.Equal([First, Second], store.Stored);
    }

    [Fact]
    public async Task A_gzipped_protobuf_export_is_stored_and_answered_with_an_empty_protobuf_response()
    {
        var store = new RecordingStore();
        using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            await gzip.WriteAsync(Fixture("claude-code-logs.pb"), Ct);
        }

        var reply = await Post(compressed.ToArray(), "application/x-protobuf", store, "gzip");

        Assert.Equal(200, reply.StatusCode);
        Assert.Equal("application/x-protobuf", reply.ContentType);
        Assert.Empty(reply.Body);
        Assert.Equal([First, Second], store.Stored);
    }

    [Fact]
    public async Task An_export_with_nothing_to_keep_is_still_a_success()
    {
        var store = new RecordingStore();

        var reply = await Post(Encoding.UTF8.GetBytes("""{"resourceLogs":[]}"""), "application/json", store);

        Assert.Equal(200, reply.StatusCode);
        Assert.Empty(store.Stored);
    }

    [Theory]
    [InlineData("text/plain", null, 415)]
    [InlineData(null, null, 415)]
    [InlineData("application/json", "br", 415)]
    [InlineData("application/json", null, 400)]
    [InlineData("application/x-protobuf", "gzip", 400)]
    public async Task What_cannot_be_read_is_refused_and_nothing_is_stored(string? contentType, string? encoding, int status)
    {
        var store = new RecordingStore();

        var reply = await Post(Encoding.UTF8.GetBytes("not an export"), contentType, store, encoding);

        Assert.Equal(status, reply.StatusCode);
        Assert.Empty(store.Stored);
    }

    private sealed class FailingStore : IClaudeApiRequestStore
    {
        public Task<int> AddAsync(IReadOnlyList<ClaudeApiRequest> requests, CancellationToken cancellationToken = default) =>
            throw new IOException("The database is busy.");

        public Task<IReadOnlyList<ClaudeApiRequest>> ListAsync(string? sessionId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ClaudeApiRequest>>([]);
    }

    [Fact]
    public async Task A_batch_the_database_refused_is_answered_503_so_the_exporter_retries_it()
    {
        var reply = await Post(Fixture("claude-code-logs.pb"), "application/x-protobuf", new FailingStore());

        Assert.Equal(503, reply.StatusCode);
        Assert.Equal(ClaudeCodeLogsEndpoint.RetryAfterSeconds, reply.RetryAfterSeconds);
        Assert.IsType<IOException>(reply.Failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("identity")]
    public async Task An_empty_or_identity_encoding_is_no_encoding(string encoding)
    {
        var store = new RecordingStore();

        var reply = await Post(Fixture("claude-code-logs.json"), "application/json", store, encoding);

        Assert.Equal(200, reply.StatusCode);
        Assert.Equal(2, store.Stored.Count);
    }

    [Fact]
    public async Task A_body_over_the_bound_is_refused()
    {
        var store = new RecordingStore();

        var reply = await ClaudeCodeLogsEndpoint.AcceptAsync(
            new MemoryStream(new byte[ClaudeCodeLogsEndpoint.MaxBodyBytes + 1]), contentLength: null, "application/x-protobuf", null, store, Ct);

        Assert.Equal(413, reply.StatusCode);
        Assert.Empty(store.Stored);
    }

    [Fact]
    public void The_endpoint_sits_at_v1_logs_on_the_listener_s_authority()
    {
        Assert.Equal(
            new Uri("http://127.0.0.1:5757/v1/logs"),
            ClaudeCodeLogsEndpoint.LogsEndpoint(new Uri("http://127.0.0.1:5757/mcp")));
    }
}
