using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;

using Backlog.Desktop.UI.Mcp;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The token broker of local ADR 0052 §3: one JSON line in, one out, over a named pipe only
/// the person's Windows SID can open. The codec is held over any stream; the ACL, the
/// client-SID predicate and a real pipe round trip are held on Windows.
/// </summary>
public sealed class McpTokenBrokerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 30, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-token-broker-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FileMcpGrantStore _grants;
    private readonly McpGrantTokens _tokens;

    public McpTokenBrokerTests()
    {
        var clock = new FakeTimeProvider(Now);
        _grants = new FileMcpGrantStore(Path.Combine(_root, FileMcpGrantStore.FileName), clock);
        _tokens = new McpGrantTokens(_grants, clock);
    }

    private CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_request_for_a_grant_is_answered_with_a_token_the_listener_accepts()
    {
        var grant = _grants.Create("Claude Code", ["tasks:read"], "jobsc");

        var reply = await ExchangeAsync("{\"grant\":\"" + grant.Id + "\"}\n");

        var token = (string?)reply["token"];
        Assert.NotNull(token);
        Assert.Equal(Now.AddMinutes(15), DateTimeOffset.Parse((string)reply["expiresAt"]!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(grant.Id, (await _tokens.ValidateAsync(token, Cancel)).Grant?.Id);
    }

    [Fact]
    public async Task An_unknown_grant_is_answered_unknown_grant()
    {
        var reply = await ExchangeAsync("{\"grant\":\"g_aaaaaaaaaaaaaaaa\"}\n");

        Assert.Equal("unknown-grant", (string?)reply["error"]);
        Assert.Null(reply["token"]);
    }

    [Fact]
    public async Task A_revoked_grant_is_answered_revoked()
    {
        var grant = _grants.Create("Copilot", [], "jobsc");
        _grants.Revoke(grant.Id);

        var reply = await ExchangeAsync("{\"grant\":\"" + grant.Id + "\"}\n");

        Assert.Equal("revoked", (string?)reply["error"]);
    }

    [Theory]
    [InlineData("not json\n")]
    [InlineData("{}\n")]
    [InlineData("{\"grant\":42}\n")]
    [InlineData("{\"grant\":\"\"}\n")]
    [InlineData("[\"g_aaaaaaaaaaaaaaaa\"]\n")]
    [InlineData("{\"grant\":\"g_a\"} {\"grant\":\"g_b\"}\n")]
    [InlineData("\n")]
    public async Task A_request_that_is_not_one_grant_is_answered_bad_request(string request)
    {
        var reply = await ExchangeAsync(request);

        Assert.Equal("bad-request", (string?)reply["error"]);
    }

    [Fact]
    public async Task A_request_over_4_KB_is_answered_bad_request_unread()
    {
        var grant = _grants.Create("Claude Code", [], "jobsc");
        var request = "{\"grant\":\"" + grant.Id + "\",\"pad\":\"" + new string('x', 4096) + "\"}\n";

        var reply = await ExchangeAsync(request);

        Assert.Equal("bad-request", (string?)reply["error"]);
    }

    [Fact]
    public async Task A_caller_that_is_not_this_user_is_answered_forbidden_and_gets_no_token()
    {
        var grant = _grants.Create("Claude Code", [], "jobsc");

        var reply = await ExchangeAsync("{\"grant\":\"" + grant.Id + "\"}\n", isSameUser: () => false);

        Assert.Equal("forbidden", (string?)reply["error"]);
        Assert.Null(reply["token"]);
    }

    [Fact]
    public void Only_the_same_SID_is_the_same_user()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("SIDs are Windows-only.");
            return;
        }

        var own = new SecurityIdentifier("S-1-5-21-1111111111-2222222222-3333333333-1001");
        var foreign = new SecurityIdentifier("S-1-5-21-1111111111-2222222222-3333333333-1002");

        Assert.True(McpTokenBroker.IsSameUser(new SecurityIdentifier(own.Value), own));
        Assert.False(McpTokenBroker.IsSameUser(foreign, own));
        Assert.False(McpTokenBroker.IsSameUser(null, own));
    }

    [Theory]
    [InlineData("Backlog", "jobsc", "backlog-mcp-broker-jobsc")]
    [InlineData("Backlog.Debug", "jobsc", "backlog-debug-mcp-broker-jobsc")]
    [InlineData("Backlog", "Jo Bs", "backlog-mcp-broker-jo_bs")]
    [InlineData("Backlog", "jöb\\sc", "backlog-mcp-broker-j_b_sc")]
    public void The_pipe_name_is_the_installation_and_the_user(string appDataFolderName, string userName, string expected) =>
        Assert.Equal(expected, McpTokenBroker.PipeNameFor(appDataFolderName, userName));

    [Fact]
    public void The_pipe_admits_one_SID_and_inherits_nothing()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Named pipe ACLs are Windows-only.");
            return;
        }

        AssertOneRuleFor(WindowsIdentity.GetCurrent().User!);
    }

    [Fact]
    public async Task A_real_pipe_answers_a_token_twice_in_a_row()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The broker's pipe is Windows-only.");

        var grant = _grants.Create("Claude Code", ["tasks:read"], "jobsc");
        using var broker = new McpTokenBroker(_tokens, UniquePipeName(), log: null);
        broker.Start();

        for (var request = 0; request < 2; request++)
        {
            var reply = await RequestOverPipeAsync(broker.PipeName, "{\"grant\":\"" + grant.Id + "\"}\n");
            var token = (string?)reply["token"];

            Assert.NotNull(token);
            Assert.Equal(grant.Id, (await _tokens.ValidateAsync(token, Cancel)).Grant?.Id);
        }

        Assert.True(broker.IsListening);
        Assert.Null(broker.LastError);
    }

    [Fact]
    public async Task A_real_pipe_answers_a_refusal()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The broker's pipe is Windows-only.");

        using var broker = new McpTokenBroker(_tokens, UniquePipeName(), log: null);
        broker.Start();

        var reply = await RequestOverPipeAsync(broker.PipeName, "{\"grant\":\"g_aaaaaaaaaaaaaaaa\"}\n");

        Assert.Equal("unknown-grant", (string?)reply["error"]);
    }

    [Fact]
    public async Task A_request_the_store_fails_on_drops_that_connection_and_the_next_one_is_served()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The broker's pipe is Windows-only.");

        var grant = _grants.Create("Claude Code", [], "jobsc");
        var store = new Backlog.Tests.FailableMcpGrantStore(_grants);
        using var broker = new McpTokenBroker(new McpGrantTokens(store, TimeProvider.System), UniquePipeName(), log: null);
        broker.Start();

        // What a read-only mcp-grants.json does to the reseal a hand-written grant needs.
        store.Failure = new UnauthorizedAccessException("Access to the path is denied.");
        Assert.Null(await ReplyLineOverPipeAsync(broker.PipeName, "{\"grant\":\"" + grant.Id + "\"}\n"));

        store.Failure = null;
        var reply = await RequestOverPipeAsync(broker.PipeName, "{\"grant\":\"" + grant.Id + "\"}\n");

        Assert.NotNull((string?)reply["token"]);
        Assert.True(broker.IsListening);
        Assert.Null(broker.LastError);
    }

    [Fact]
    public async Task A_client_that_never_reads_its_reply_holds_the_pipe_no_longer_than_the_connection_timeout()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The broker's pipe is Windows-only.");

        var grant = _grants.Create("Claude Code", [], "jobsc");
        using var broker = new McpTokenBroker(_tokens, UniquePipeName(), log: null, connectionTimeout: TimeSpan.FromMilliseconds(500));
        broker.Start();
        await ListeningAsync(broker);

        // Sends its request and then neither reads nor closes.
        await using var stalled = new NamedPipeClientStream(".", broker.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await stalled.ConnectAsync(10_000, Cancel);
        await stalled.WriteAsync(Encoding.UTF8.GetBytes("{\"grant\":\"" + grant.Id + "\"}\n"), Cancel);
        await stalled.FlushAsync(Cancel);

        var started = Stopwatch.StartNew();
        var reply = await RequestOverPipeAsync(broker.PipeName, "{\"grant\":\"" + grant.Id + "\"}\n");

        Assert.NotNull((string?)reply["token"]);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(5), $"The next client waited {started.Elapsed}.");
    }

    [Fact]
    public async Task A_second_broker_on_a_name_already_taken_stays_down_rather_than_share_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The broker's pipe is Windows-only.");

        var name = UniquePipeName();
        using var first = new McpTokenBroker(_tokens, name, log: null);
        first.Start();
        await ListeningAsync(first);

        using var second = new McpTokenBroker(_tokens, name, log: null);
        second.Start();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (second.LastError is null && DateTime.UtcNow < deadline) await Task.Delay(20, Cancel);

        Assert.False(second.IsListening);
        Assert.NotNull(second.LastError);
    }

    /// <summary>
    /// The headersHelper itself, run as Claude Code runs it, against the real broker: what it
    /// prints is a header the listener accepts. The one check that Node's pipe client and this
    /// pipe agree, the client-SID read included.
    /// </summary>
    [Fact]
    public async Task The_headers_script_prints_a_header_the_listener_accepts()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The broker's pipe is Windows-only.");
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        var grant = _grants.Create("Claude Code", ["tasks:read"], "jobsc");
        using var broker = new McpTokenBroker(_tokens, UniquePipeName(), log: null);
        broker.Start();
        await ListeningAsync(broker);

        var start = new ProcessStartInfo("node")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(RepositoryRoot.File("tools", "mcp", "backlog-mcp-headers.mjs"));
        start.ArgumentList.Add("--grant");
        start.ArgumentList.Add(grant.Id);
        start.Environment["BACKLOG_MCP_BROKER_PIPE"] = broker.PipeName;

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(Cancel);
        var stderr = process.StandardError.ReadToEndAsync(Cancel);
        await process.WaitForExitAsync(Cancel);

        Assert.True(process.ExitCode == 0, await stderr);
        var header = (string?)JsonNode.Parse(await stdout)!["Authorization"];
        Assert.StartsWith("Bearer ", header, StringComparison.Ordinal);
        Assert.Equal(grant.Id, (await _tokens.ValidateAsync(header!["Bearer ".Length..], Cancel)).Grant?.Id);
    }

    private static bool NodeInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("node", "--version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });

            return process is not null && process.WaitForExit(10_000) && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AssertOneRuleFor(SecurityIdentifier sid)
    {
        var security = McpTokenBroker.CreatePipeSecurity(sid);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>()
            .ToList();

        var rule = Assert.Single(rules);
        Assert.Equal(sid, rule.IdentityReference);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        Assert.Equal(PipeAccessRights.ReadWrite, rule.PipeAccessRights & ~PipeAccessRights.Synchronize);
        Assert.True(security.AreAccessRulesProtected);
    }

    private async Task<JsonNode> ExchangeAsync(string request, Func<bool>? isSameUser = null)
    {
        using var broker = new McpTokenBroker(_tokens, UniquePipeName(), log: null);
        using var stream = new TwoWayStream(Encoding.UTF8.GetBytes(request));

        await broker.HandleAsync(stream, isSameUser ?? (() => true), Cancel);

        var reply = Encoding.UTF8.GetString(stream.Written);
        Assert.EndsWith("\n", reply, StringComparison.Ordinal);
        return JsonNode.Parse(reply)!;
    }

    private async Task<JsonNode> RequestOverPipeAsync(string pipeName, string request)
    {
        var line = await ReplyLineOverPipeAsync(pipeName, request);

        Assert.NotNull(line);
        return JsonNode.Parse(line)!;
    }

    /// <summary>The reply line, or null when the broker closed the connection without one.</summary>
    private async Task<string?> ReplyLineOverPipeAsync(string pipeName, string request)
    {
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(10_000, Cancel);

        await client.WriteAsync(Encoding.UTF8.GetBytes(request), Cancel);
        await client.FlushAsync(Cancel);

        using var reader = new StreamReader(client, Encoding.UTF8);

        try
        {
            return await reader.ReadLineAsync(Cancel);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private async Task ListeningAsync(McpTokenBroker broker)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!broker.IsListening && DateTime.UtcNow < deadline) await Task.Delay(20, Cancel);
        Assert.True(broker.IsListening, broker.LastError);
    }

    private static string UniquePipeName() => "backlog-test-mcp-broker-" + Guid.NewGuid().ToString("N");

    /// <summary>A stream that reads the request it was given and keeps what is written to
    /// it, the two directions of a pipe.</summary>
    private sealed class TwoWayStream(byte[] input) : Stream
    {
        private readonly MemoryStream _input = new(input);
        private readonly MemoryStream _output = new();

        public byte[] Written => _output.ToArray();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => _output.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _input.Dispose();
                _output.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
