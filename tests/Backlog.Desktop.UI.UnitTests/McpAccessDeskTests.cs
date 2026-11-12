using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;

using Backlog.Desktop.UI.Mcp;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.Mcp;
using Backlog.SharedKernel.Mcp;
using Backlog.Tests;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// "Approve an agent once" (local ADR 0052 §5): an agent registers itself into a pending grant
/// that mints no token, asks for more through <c>request_access</c> without its grant changing,
/// and only the person — through Allow with Windows Hello, or Decline — answers. Each limit and
/// refusal is held here, and the pending-to-granted path end to end over the real file stores,
/// the token mint, and the broker's wire.
/// </summary>
public sealed class McpAccessDeskTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-access-desk-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FileMcpGrantStore _grants;
    private readonly FileMcpAccessRequestStore _requests;
    private readonly McpGrantTokens _tokens;
    private readonly FakeConsent _consent = new();
    private readonly FakeWindows _windows = new();
    private readonly RecordingNotifications _notifications = new();
    private readonly McpAccessDesk _desk;

    public McpAccessDeskTests()
    {
        _grants = new FileMcpGrantStore(Path.Combine(_root, FileMcpGrantStore.FileName), _clock);
        _requests = new FileMcpAccessRequestStore(Path.Combine(_root, FileMcpAccessRequestStore.FileName), _clock);
        _tokens = new McpGrantTokens(_grants, _clock, _requests);
        _desk = new McpAccessDesk(_grants, _requests, _consent, _clock, _windows, _notifications);
    }

    private CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_registration_is_a_pending_grant_that_mints_no_token_until_the_person_allows_it_with_Hello()
    {
        var registered = await _desk.RegisterAsync("Claude Code · Backlog", ["tasks:write", "tasks:read"], "claude.exe", @"D:\Repos\Backlog", Cancel);

        Assert.False(registered.Refused, registered.Message);
        var grant = registered.Grant!;
        Assert.Empty(grant.Scopes);
        Assert.Equal(McpAccessDesk.CreatedBySelf, grant.CreatedBy);
        Assert.Equal(["tasks:read", "tasks:write"], registered.Request!.Scopes);
        Assert.Equal(McpGrantRefusal.Pending, _tokens.Mint(grant.Id).Refusal);

        var allowed = await _desk.AllowAsync(registered.Request.Id, ["tasks:read", "tasks:write"], Cancel);

        Assert.False(allowed.Refused, allowed.Message);
        Assert.Equal(1, _consent.Asked);
        Assert.Equal(["tasks:read", "tasks:write"], _grants.Find(grant.Id)!.Scopes);
        Assert.Equal(McpAccessRequestState.Allowed, _requests.Find(registered.Request.Id)!.State);

        var minted = _tokens.Mint(grant.Id);
        Assert.NotNull(minted.Token);
        var validated = await _tokens.ValidateAsync(minted.Token.Value, Cancel);
        Assert.Equal(grant.Id, validated.Grant?.Id);
        Assert.Equal(["tasks:read", "tasks:write"], McpScopePolicies.ScopesOf(validated.Principal).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Allow_with_scopes_trimmed_grants_only_those()
    {
        var registered = await _desk.RegisterAsync("Copilot CLI", ["tasks:read", "tasks:write", "runs:write"], null, null, Cancel);

        await _desk.AllowAsync(registered.Request!.Id, ["tasks:read"], Cancel);

        Assert.Equal(["tasks:read"], _grants.Find(registered.Grant!.Id)!.Scopes);
        Assert.Equal(["tasks:read"], _requests.Find(registered.Request.Id)!.Allowed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_name_is_refused(string name)
    {
        var outcome = await _desk.RegisterAsync(name, ["tasks:read"], null, null, Cancel);

        Assert.Equal(McpAccessRefusals.BadName, outcome.Refusal);
        Assert.Empty(_grants.List());
    }

    [Fact]
    public async Task No_scope_a_misspelt_scope_and_devbook_decide_are_refused_and_make_no_grant()
    {
        Assert.Equal(McpAccessRefusals.NoScopes, (await _desk.RegisterAsync("A", [], null, null, Cancel)).Refusal);
        Assert.Equal(McpAccessRefusals.BadScope, (await _desk.RegisterAsync("A", ["tasks:reed"], null, null, Cancel)).Refusal);
        Assert.Equal(McpAccessRefusals.BadScope, (await _desk.RegisterAsync("A", ["tasks:read", "devbook:decide"], null, null, Cancel)).Refusal);

        Assert.Empty(_grants.List());
        Assert.Empty(_requests.List());
    }

    [Fact]
    public async Task One_request_per_name_waits_at_a_time()
    {
        await _desk.RegisterAsync("Claude Code", ["tasks:read"], null, null, Cancel);

        var again = await _desk.RegisterAsync("claude code", ["tasks:read"], null, null, Cancel);

        Assert.Equal(McpAccessRefusals.AlreadyPending, again.Refusal);
        Assert.Single(_grants.List());
    }

    [Fact]
    public async Task Five_requests_wait_at_most()
    {
        for (var i = 1; i <= McpAccessDesk.MaxOpen; i++)
        {
            Assert.False((await _desk.RegisterAsync($"Agent {i}", ["tasks:read"], null, null, Cancel)).Refused);
        }

        var sixth = await _desk.RegisterAsync("Agent 6", ["tasks:read"], null, null, Cancel);

        Assert.Equal(McpAccessRefusals.TooMany, sixth.Refusal);
        Assert.Equal(McpAccessDesk.MaxOpen, _desk.Waiting().Count);
    }

    [Fact]
    public async Task A_name_an_agent_already_has_is_refused()
    {
        _grants.Create("Backlog delivery", ["tasks:read"], "Settings");

        var outcome = await _desk.RegisterAsync("Backlog delivery", ["tasks:read"], null, null, Cancel);

        Assert.Equal(McpAccessRefusals.NameTaken, outcome.Refusal);
    }

    [Fact]
    public async Task Decline_revokes_the_pending_grant_and_blocks_the_name_for_a_day()
    {
        var registered = await _desk.RegisterAsync("Copilot CLI", ["tasks:read"], null, null, Cancel);

        var declined = await _desk.DeclineAsync(registered.Request!.Id, Cancel);

        Assert.False(declined.Refused, declined.Message);
        Assert.True(_grants.Find(registered.Grant!.Id)!.Revoked);
        Assert.Equal(McpGrantRefusal.Revoked, _tokens.Mint(registered.Grant.Id).Refusal);
        Assert.Equal(0, _consent.Asked);

        _clock.Advance(TimeSpan.FromHours(23));
        Assert.Equal(McpAccessRefusals.DeclinedRecently, (await _desk.RegisterAsync("Copilot CLI", ["tasks:read"], null, null, Cancel)).Refusal);

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.False((await _desk.RegisterAsync("Copilot CLI", ["tasks:read"], null, null, Cancel)).Refused);
    }

    [Fact]
    public async Task Allow_is_refused_when_Hello_is_not_available_and_the_grant_stays_pending()
    {
        var registered = await _desk.RegisterAsync("Claude Code", ["tasks:read"], null, null, Cancel);
        var harness = new McpAccessDesk(_grants, _requests, new UnavailableUserConsent(), _clock);

        var outcome = await harness.AllowAsync(registered.Request!.Id, ["tasks:read"], Cancel);

        Assert.Equal(McpAccessRefusals.HelloUnavailable, outcome.Refusal);
        Assert.True(_requests.Find(registered.Request.Id)!.IsOpen);
        Assert.Empty(_grants.Find(registered.Grant!.Id)!.Scopes);
        Assert.Equal(McpGrantRefusal.Pending, _tokens.Mint(registered.Grant.Id).Refusal);
    }

    [Fact]
    public async Task Allow_is_refused_when_Hello_does_not_confirm()
    {
        var registered = await _desk.RegisterAsync("Claude Code", ["tasks:read"], null, null, Cancel);
        _consent.Answer = UserConsentOutcome.Declined;

        var outcome = await _desk.AllowAsync(registered.Request!.Id, ["tasks:read"], Cancel);

        Assert.Equal(McpAccessRefusals.HelloDeclined, outcome.Refusal);
        Assert.True(_requests.Find(registered.Request.Id)!.IsOpen);
    }

    [Fact]
    public async Task Allow_refuses_a_scope_not_asked_for_and_none_at_all_before_asking_for_Hello()
    {
        var registered = await _desk.RegisterAsync("Claude Code", ["tasks:read"], null, null, Cancel);

        Assert.Equal(McpAccessRefusals.NotAsked, (await _desk.AllowAsync(registered.Request!.Id, ["tasks:read", "tasks:admin"], Cancel)).Refusal);
        Assert.Equal(McpAccessRefusals.NothingAllowed, (await _desk.AllowAsync(registered.Request.Id, [], Cancel)).Refusal);
        Assert.Equal(0, _consent.Asked);
    }

    [Fact]
    public async Task A_request_is_answered_once()
    {
        var registered = await _desk.RegisterAsync("Claude Code", ["tasks:read"], null, null, Cancel);
        await _desk.AllowAsync(registered.Request!.Id, ["tasks:read"], Cancel);

        Assert.Equal(McpAccessRefusals.NotOpen, (await _desk.AllowAsync(registered.Request.Id, ["tasks:read"], Cancel)).Refusal);
        Assert.Equal(McpAccessRefusals.NotOpen, (await _desk.DeclineAsync(registered.Request.Id, Cancel)).Refusal);
        Assert.False(_grants.Find(registered.Grant!.Id)!.Revoked);
    }

    [Fact]
    public async Task Request_more_opens_a_request_and_leaves_the_caller_s_grant_as_it_was_until_allowed()
    {
        var grant = _grants.Create("Backlog delivery", ["tasks:read"], "Settings");
        var caller = await PrincipalOf(grant);

        var answer = await _desk.RequestMoreAsync(caller, ["tasks:read", "runs:write"], @"D:\Repos\Backlog", Cancel);

        Assert.False(answer.Refused, answer.Message);
        Assert.True(answer.WindowOpened);
        var request = _requests.Find(answer.RequestId!)!;
        Assert.Equal(McpAccessRequestKind.MoreScopes, request.Kind);
        Assert.Equal(["runs:write"], request.Scopes);
        Assert.Equal(grant.Version, _grants.Find(grant.Id)!.Version);
        Assert.Equal(grant.Scopes, _grants.Find(grant.Id)!.Scopes);
        Assert.NotNull(_tokens.Mint(grant.Id).Token);

        await _desk.AllowAsync(request.Id, ["runs:write"], Cancel);

        Assert.Equal(["tasks:read", "runs:write"], _grants.Find(grant.Id)!.Scopes);
    }

    [Fact]
    public async Task Request_more_asked_again_while_one_waits_focuses_the_same_request()
    {
        var grant = _grants.Create("Backlog delivery", ["tasks:read"], "Settings");
        var caller = await PrincipalOf(grant);

        var first = await _desk.RequestMoreAsync(caller, ["runs:write"], null, Cancel);
        var second = await _desk.RequestMoreAsync(caller, ["runs:write"], null, Cancel);

        Assert.Equal(first.RequestId, second.RequestId);
        Assert.Single(_requests.List());
        Assert.Equal([first.RequestId!, first.RequestId!], _windows.Shown);
    }

    [Fact]
    public async Task Request_more_is_refused_for_a_caller_with_no_grant_a_pending_grant_and_scopes_it_holds()
    {
        Assert.Equal(McpAccessRefusals.NoGrant, (await _desk.RequestMoreAsync(McpScopePolicies.SharedTokenCaller, ["tasks:read"], null, Cancel)).Refusal);
        Assert.Equal(McpAccessRefusals.NoGrant, (await _desk.RequestMoreAsync(McpScopePolicies.HarnessCaller, ["tasks:read"], null, Cancel)).Refusal);
        Assert.Equal(McpAccessRefusals.NoGrant, (await _desk.RequestMoreAsync(null, ["tasks:read"], null, Cancel)).Refusal);

        var held = _grants.Create("Backlog delivery", ["tasks:read"], "Settings");
        Assert.Equal(McpAccessRefusals.NothingNew, (await _desk.RequestMoreAsync(await PrincipalOf(held), ["tasks:read"], null, Cancel)).Refusal);
        Assert.Equal(McpAccessRefusals.BadScope, (await _desk.RequestMoreAsync(await PrincipalOf(held), ["devbook:decide"], null, Cancel)).Refusal);

        var registered = await _desk.RegisterAsync("Claude Code", ["tasks:read"], null, null, Cancel);
        var pending = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", registered.Grant!.Id)], McpGrantTokens.AuthenticationType));
        Assert.Equal(McpAccessRefusals.Pending, (await _desk.RequestMoreAsync(pending, ["tasks:write"], null, Cancel)).Refusal);
    }

    [Fact]
    public async Task Declining_a_request_for_more_leaves_the_grant_working()
    {
        var grant = _grants.Create("Backlog delivery", ["tasks:read"], "Settings");
        var answer = await _desk.RequestMoreAsync(await PrincipalOf(grant), ["runs:write"], null, Cancel);

        await _desk.DeclineAsync(answer.RequestId!, Cancel);

        Assert.False(_grants.Find(grant.Id)!.Revoked);
        Assert.Equal(["tasks:read"], _grants.Find(grant.Id)!.Scopes);
        Assert.NotNull(_tokens.Mint(grant.Id).Token);
    }

    [Fact]
    public async Task A_request_raises_an_alert_the_phone_shows_and_its_answer_closes_it()
    {
        var registered = await _desk.RegisterAsync("Claude Code", ["tasks:read"], null, null, Cancel);

        var (id, draft) = Assert.Single(_notifications.Published);
        Assert.Equal(Backlog.Modules.Notifications.Abstractions.NotificationKind.Alert, draft.Kind);
        Assert.Null(draft.Answer);
        Assert.Equal(AgentRequestSurface.Link(registered.Request!.Id), draft.DeepLink);
        Assert.Contains("Claude Code", draft.Title, StringComparison.Ordinal);

        await _desk.AllowAsync(registered.Request.Id, ["tasks:read"], Cancel);

        Assert.Equal(id, Assert.Single(_notifications.ClosedById).Id);
    }

    [Fact]
    public async Task Show_opens_the_window_of_the_grant_s_waiting_request_and_changes_nothing()
    {
        var registered = await _desk.RegisterAsync("Claude Code", ["tasks:read"], null, null, Cancel);
        var before = File.ReadAllText(Path.Combine(_root, FileMcpAccessRequestStore.FileName));

        var shown = await _desk.ShowAsync(registered.Grant!.Id);

        Assert.True(shown.WindowOpened);
        Assert.Equal([registered.Request!.Id], _windows.Shown);
        Assert.Equal(before, File.ReadAllText(Path.Combine(_root, FileMcpAccessRequestStore.FileName)));
        Assert.Equal(McpAccessRefusals.NoRequest, (await _desk.ShowAsync("g_nothingwaitshere")).Refusal);
    }

    [Fact]
    public async Task The_broker_registers_over_the_pipe_and_the_pending_grant_is_refused_a_token_there()
    {
        var broker = new McpTokenBroker(_tokens, "unused-" + Guid.NewGuid().ToString("N"), null, desk: _desk);

        var registered = await ExchangeAsync(broker, "{\"register\":{\"name\":\"Claude Code\",\"scopes\":[\"tasks:read\"],\"folder\":\"D:\\\\Repos\\\\Backlog\"}}\n");

        Assert.Equal("pending", (string?)registered["state"]);
        var grantId = (string)registered["grant"]!;
        Assert.StartsWith(McpGrantClaims.IdPrefix, grantId, StringComparison.Ordinal);
        Assert.Equal(@"D:\Repos\Backlog", _requests.Find((string)registered["request"]!)!.Folder);

        Assert.Equal("pending", (string?)(await ExchangeAsync(broker, "{\"grant\":\"" + grantId + "\"}\n"))["error"]);
        Assert.True((bool)(await ExchangeAsync(broker, "{\"request\":\"" + grantId + "\"}\n"))["opened"]!);

        var refused = await ExchangeAsync(broker, "{\"register\":{\"name\":\"Claude Code\",\"scopes\":[\"tasks:read\"]}}\n");
        Assert.Equal(McpAccessRefusals.AlreadyPending, (string?)refused["error"]);
    }

    [Theory]
    [InlineData("{\"allow\":\"r_aaaaaaaaaaaaaaaa\"}\n")]
    [InlineData("{\"decline\":\"r_aaaaaaaaaaaaaaaa\"}\n")]
    [InlineData("{\"setScopes\":{\"grant\":\"g_aaaaaaaaaaaaaaaa\",\"scopes\":[\"tasks:admin\"]}}\n")]
    public async Task No_pipe_command_answers_a_request_or_edits_a_grant(string request)
    {
        var grant = _grants.Create("Backlog delivery", ["tasks:read"], "Settings");
        var broker = new McpTokenBroker(_tokens, "unused-" + Guid.NewGuid().ToString("N"), null, desk: _desk);

        var reply = await ExchangeAsync(broker, request);

        Assert.Equal("bad-request", (string?)reply["error"]);
        Assert.Equal(grant.Version, _grants.Find(grant.Id)!.Version);
        Assert.Equal(grant.Scopes, _grants.Find(grant.Id)!.Scopes);
    }

    [Fact]
    public async Task A_self_registered_grant_stays_pending_when_its_requests_are_lost()
    {
        var registered = await _desk.RegisterAsync("Claude Code", ["tasks:read"], null, null, Cancel);
        File.Delete(Path.Combine(_root, FileMcpAccessRequestStore.FileName));

        Assert.Equal(McpGrantRefusal.Pending, _tokens.Mint(registered.Grant!.Id).Refusal);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", registered.Grant.Id)], McpGrantTokens.AuthenticationType));
        Assert.Equal(McpAccessRefusals.Pending, (await _desk.RequestMoreAsync(principal, ["tasks:write"], null, Cancel)).Refusal);
    }

    [Fact]
    public async Task A_registration_whose_request_cannot_be_written_is_refused_and_leaves_no_working_grant()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, FileMcpAccessRequestStore.FileName), "{ not json");

        var outcome = await _desk.RegisterAsync("Claude Code", ["tasks:read"], null, null, Cancel);

        Assert.Equal(McpAccessRefusals.StoreUnavailable, outcome.Refusal);
        var grant = Assert.Single(_grants.List());
        Assert.True(grant.Revoked);
    }

    [Fact]
    public void Request_access_meets_the_caller_policy_and_no_scope_s()
    {
        Assert.Equal(McpScopePolicies.Caller, McpScopePolicies.ToolPolicy("request_access"));
        Assert.Equal(McpScopePolicies.Caller, McpScopePolicies.ToolPolicy("REQUEST_ACCESS"));
        Assert.Equal(McpScopePolicies.Fallback, McpScopePolicies.ToolPolicy("grant_agent"));
    }

    [Fact]
    public async Task A_broker_without_the_desk_answers_register_bad_request()
    {
        var broker = new McpTokenBroker(_tokens, "unused-" + Guid.NewGuid().ToString("N"), null);

        var reply = await ExchangeAsync(broker, "{\"register\":{\"name\":\"Claude Code\",\"scopes\":[\"tasks:read\"]}}\n");

        Assert.Equal("bad-request", (string?)reply["error"]);
        Assert.Empty(_grants.List());
    }

    private async Task<ClaimsPrincipal> PrincipalOf(McpGrant grant)
    {
        var minted = _tokens.Mint(grant.Id);
        return (await _tokens.ValidateAsync(minted.Token!.Value, Cancel)).Principal!;
    }

    private async Task<JsonNode> ExchangeAsync(McpTokenBroker broker, string request)
    {
        using var stream = new DuplexStream(Encoding.UTF8.GetBytes(request));
        await broker.HandleAsync(stream, () => true, Cancel);
        return JsonNode.Parse(Encoding.UTF8.GetString(stream.Written).TrimEnd('\n'))!;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeConsent : IUserConsent
    {
        public UserConsentOutcome Answer { get; set; } = UserConsentOutcome.Verified;

        public int Asked { get; private set; }

        public Task<UserConsentOutcome> VerifyAsync(string message, CancellationToken cancellationToken = default)
        {
            Asked++;
            return Task.FromResult(Answer);
        }
    }

    private sealed class FakeWindows : IAgentRequestWindows
    {
        public List<string> Shown { get; } = [];

        public Task<bool> ShowAsync(string requestId)
        {
            Shown.Add(requestId);
            return Task.FromResult(true);
        }
    }

    /// <summary>Reads from a fixed request and keeps what is written.</summary>
    private sealed class DuplexStream(byte[] input) : Stream
    {
        private readonly MemoryStream _input = new(input);
        private readonly MemoryStream _output = new();

        public byte[] Written => _output.ToArray();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }
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
