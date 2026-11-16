using System.Security.Claims;

using Backlog.Desktop.UI.Mcp;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.Mcp;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel.Mcp;
using Backlog.Tests;

using Microsoft.Extensions.Time.Testing;

using static Backlog.Desktop.UI.UnitTests.McpServerSettingsPageTests;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Agents that grant other agents (local ADR 0052 §5): the <c>agents:grant</c> tools' host, the
/// access desk, held to every rule of the proposal over the real file stores and the real token
/// mint — only scopes the granter holds; never <c>devbook:decide</c>, <c>agents:grant</c> or
/// <c>tasks:admin</c>; one level deep; never its own grant or its granter's; the person's and
/// another granter's grants read-only; seven days unless renewed; scopes cut to the granter's at
/// each mint; and revoking the granter revokes what it granted in the same write. Each grant
/// raises a quiet notification and writes an audit record naming both.
/// </summary>
public sealed class McpGrantDelegationTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-delegation-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FileMcpGrantStore _grants;
    private readonly FileMcpAccessRequestStore _requests;
    private readonly McpGrantTokens _tokens;
    private readonly RecordingNotifications _notifications = new();
    private readonly FakeAuditLog _audit = new();
    private readonly McpAccessDesk _desk;

    public McpGrantDelegationTests()
    {
        _grants = new FileMcpGrantStore(Path.Combine(_root, FileMcpGrantStore.FileName), _clock);
        _requests = new FileMcpAccessRequestStore(Path.Combine(_root, FileMcpAccessRequestStore.FileName), _clock);
        _tokens = new McpGrantTokens(_grants, _clock, _requests);
        _desk = new McpAccessDesk(_grants, _requests, new AlwaysConsent(), _clock, notifications: _notifications, audit: _audit);
    }

    private CancellationToken Cancel => TestContext.Current.CancellationToken;

    private McpGrant Manager() =>
        _grants.Create("Manager session", ["sessions:dispatch", "tasks:read", "tasks:write", "devbook:read", "agents:grant"], "Settings");

    private async Task<McpAccessRequest> RegisterAsync(string name, params string[] scopes)
    {
        var registered = await _desk.RegisterAsync(name, scopes, "copilot.exe", @"D:\Repos\Backlog", Cancel);
        Assert.False(registered.Refused, registered.Message);
        return registered.Request!;
    }

    private async Task<McpGrant> WorkerAsync(McpGrant granter, params string[] scopes)
    {
        var request = await RegisterAsync("Worker · Copilot", scopes);
        var granted = await _desk.GrantAsync(await PrincipalOf(granter), request.Id, null, Cancel);
        Assert.False(granted.Refused, granted.Message);
        return _grants.Find(request.GrantId)!;
    }

    [Fact]
    public async Task A_granter_grants_a_registration_one_level_deep_for_seven_days_and_the_worker_gets_a_token()
    {
        var manager = Manager();
        var request = await RegisterAsync("Worker · Copilot", "tasks:read", "tasks:write");

        var answer = await _desk.GrantAsync(await PrincipalOf(manager), request.Id, ["tasks:read", "tasks:write"], Cancel);

        Assert.False(answer.Refused, answer.Message);
        var worker = _grants.Find(request.GrantId)!;
        Assert.Equal(manager.Id, worker.GrantedBy);
        Assert.Equal(Now + McpGrantClaims.DelegationLifetime, worker.ExpiresAt);
        Assert.Equal(["tasks:read", "tasks:write"], worker.Scopes);
        Assert.Equal(McpAccessRequestState.Allowed, _requests.Find(request.Id)!.State);

        var principal = await PrincipalOf(worker);
        Assert.Equal(["tasks:read", "tasks:write"], McpScopePolicies.ScopesOf(principal).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_grant_raises_a_quiet_notification_and_an_audit_record_naming_both()
    {
        var manager = Manager();
        var worker = await WorkerAsync(manager, "tasks:read");

        var (_, draft) = Assert.Single(_notifications.Published, published => published.Draft.Source.Key == worker.Id);
        Assert.Equal(NotificationKind.Alert, draft.Kind);
        Assert.Equal(NotificationSeverity.Info, draft.Severity);
        Assert.Null(draft.Answer);
        Assert.Contains("Manager session", draft.Title, StringComparison.Ordinal);
        Assert.Contains("Worker · Copilot", draft.Title, StringComparison.Ordinal);

        var record = Assert.Single(await _audit.LatestAsync(10, Cancel));
        Assert.Equal(McpAudit.DelegatedGrant, record.Tool);
        Assert.Equal(McpScopePolicies.GrantCaller(manager), record.Caller);
        Assert.Equal(McpScopePolicies.GrantCaller(worker), record.TargetId);
        Assert.Equal(McpCallOutcome.Succeeded, record.Outcome);
    }

    [Fact]
    public async Task Only_scopes_the_granter_holds_are_granted()
    {
        var manager = Manager();
        var request = await RegisterAsync("Worker", "tasks:read", "runs:write");

        var refused = await _desk.GrantAsync(await PrincipalOf(manager), request.Id, ["tasks:read", "runs:write"], Cancel);

        Assert.Equal(McpAccessRefusals.NotHeld, refused.Refusal);
        Assert.True(_requests.Find(request.Id)!.IsOpen);
        Assert.Empty(_grants.Find(request.GrantId)!.Scopes);
    }

    [Fact]
    public async Task Granting_as_asked_keeps_only_what_the_granter_may_hand_on()
    {
        var manager = Manager();
        var request = await RegisterAsync("Worker", "tasks:read", "runs:write", "agents:grant", "tasks:admin");

        var answer = await _desk.GrantAsync(await PrincipalOf(manager), request.Id, null, Cancel);

        Assert.False(answer.Refused, answer.Message);
        Assert.Equal(["tasks:read"], _grants.Find(request.GrantId)!.Scopes);
    }

    [Theory]
    [InlineData("devbook:decide")]
    [InlineData("agents:grant")]
    [InlineData("tasks:admin")]
    public async Task The_powerful_scopes_are_never_delegated_even_by_a_granter_that_holds_them(string scope)
    {
        var granter = _grants.Create("Orchestrator", ["tasks:read", "tasks:admin", "agents:grant"], "Settings");
        var worker = await WorkerAsync(granter, "tasks:read");
        var caller = await PrincipalOf(granter);

        Assert.Equal(McpAccessRefusals.NotDelegable, (await _desk.EditScopesAsync(caller, worker.Id, ["tasks:read", scope], Cancel)).Refusal);

        if (scope != McpScopes.DevbookDecide)
        {
            var request = await RegisterAsync("Second worker", "tasks:read", scope);
            Assert.Equal(McpAccessRefusals.NotDelegable, (await _desk.GrantAsync(caller, request.Id, [scope], Cancel)).Refusal);
        }

        Assert.Equal(["tasks:read"], _grants.Find(worker.Id)!.Scopes);
    }

    [Fact]
    public async Task A_delegated_grant_cannot_grant_further_and_never_mints_agents_grant()
    {
        var manager = Manager();
        var worker = await WorkerAsync(manager, "tasks:read");

        // A hand edit puts agents:grant on the worker: one level deep still holds, at the desk and
        // at the mint.
        _grants.SetScopes(worker.Id, ["tasks:read", "agents:grant"]);
        var caller = await PrincipalOf(_grants.Find(worker.Id)!);
        Assert.DoesNotContain(McpScopes.AgentsGrant, McpScopePolicies.ScopesOf(caller));

        var request = await RegisterAsync("Grandchild", "tasks:read");
        Assert.Equal(McpAccessRefusals.OneLevel, (await _desk.GrantAsync(caller, request.Id, null, Cancel)).Refusal);
        Assert.Equal(McpAccessRefusals.OneLevel, _desk.List(caller).Refusal);
    }

    [Fact]
    public async Task A_granter_never_touches_its_own_grant()
    {
        var manager = Manager();
        var caller = await PrincipalOf(manager);

        Assert.Equal(McpAccessRefusals.OwnGrant, (await _desk.EditScopesAsync(caller, manager.Id, ["tasks:read"], Cancel)).Refusal);
        Assert.Equal(McpAccessRefusals.OwnGrant, (await _desk.RevokeAsync(caller, manager.Id, Cancel)).Refusal);

        var more = await _desk.RequestMoreAsync(caller, ["runs:write"], null, Cancel);
        Assert.False(more.Refused, more.Message);
        Assert.Equal(McpAccessRefusals.OwnGrant, (await _desk.GrantAsync(caller, more.RequestId!, null, Cancel)).Refusal);

        var stored = _grants.Find(manager.Id)!;
        Assert.False(stored.Revoked);
        Assert.Equal(manager.Scopes, stored.Scopes);
    }

    [Fact]
    public async Task A_worker_never_touches_its_granter_s_grant()
    {
        var manager = Manager();
        var worker = await WorkerAsync(manager, "tasks:read");
        _grants.SetScopes(worker.Id, ["tasks:read", "agents:grant"]);
        var caller = await PrincipalOf(_grants.Find(worker.Id)!);

        Assert.True((await _desk.RevokeAsync(caller, manager.Id, Cancel)).Refused);
        Assert.True((await _desk.EditScopesAsync(caller, manager.Id, ["tasks:read"], Cancel)).Refused);
        Assert.False(_grants.Find(manager.Id)!.Revoked);
    }

    [Fact]
    public async Task Grants_the_person_made_are_read_only_to_a_granter()
    {
        var manager = Manager();
        var person = _grants.Create("Backlog delivery", ["tasks:read", "tasks:write"], "Settings");
        var caller = await PrincipalOf(manager);

        Assert.Equal(McpAccessRefusals.ReadOnly, (await _desk.EditScopesAsync(caller, person.Id, ["tasks:read"], Cancel)).Refusal);
        Assert.Equal(McpAccessRefusals.ReadOnly, (await _desk.RevokeAsync(caller, person.Id, Cancel)).Refusal);

        // Nor does a granter answer the person's own agent asking for more.
        var more = await _desk.RequestMoreAsync(await PrincipalOf(person), ["devbook:read"], null, Cancel);
        Assert.Equal(McpAccessRefusals.ReadOnly, (await _desk.GrantAsync(caller, more.RequestId!, null, Cancel)).Refusal);

        Assert.Equal(["tasks:read", "tasks:write"], _grants.Find(person.Id)!.Scopes);
        Assert.False(_grants.Find(person.Id)!.Revoked);
    }

    [Fact]
    public async Task Grants_another_granter_made_are_read_only_to_a_granter()
    {
        var manager = Manager();
        var other = _grants.Create("Delivery orchestrator", ["tasks:read", "agents:grant"], "Settings");
        var worker = await WorkerAsync(other, "tasks:read");
        var caller = await PrincipalOf(manager);

        Assert.Equal(McpAccessRefusals.ReadOnly, (await _desk.EditScopesAsync(caller, worker.Id, ["tasks:read"], Cancel)).Refusal);
        Assert.Equal(McpAccessRefusals.ReadOnly, (await _desk.RevokeAsync(caller, worker.Id, Cancel)).Refusal);
        Assert.False(_grants.Find(worker.Id)!.Revoked);
        Assert.Empty(_desk.List(caller).Granted);
    }

    [Fact]
    public async Task A_delegated_grant_expires_after_seven_days_unless_its_granter_renews_it()
    {
        var manager = Manager();
        var worker = await WorkerAsync(manager, "tasks:read");

        _clock.Advance(TimeSpan.FromDays(6));
        Assert.NotNull(_tokens.Mint(worker.Id).Token);

        _clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(McpGrantRefusal.GrantExpired, _tokens.Mint(worker.Id).Refusal);
    }

    [Fact]
    public async Task Editing_a_delegated_grant_renews_it_for_another_seven_days()
    {
        var manager = Manager();
        var worker = await WorkerAsync(manager, "tasks:read");

        _clock.Advance(TimeSpan.FromDays(6));
        var edited = await _desk.EditScopesAsync(await PrincipalOf(manager), worker.Id, ["tasks:read"], Cancel);

        Assert.False(edited.Refused, edited.Message);
        Assert.Equal(_clock.GetUtcNow() + McpGrantClaims.DelegationLifetime, _grants.Find(worker.Id)!.ExpiresAt);

        _clock.Advance(TimeSpan.FromDays(3));
        Assert.NotNull(_tokens.Mint(worker.Id).Token);
        Assert.Equal(McpAudit.DelegatedScopes, (await _audit.LatestAsync(1, Cancel))[0].Tool);
    }

    [Fact]
    public async Task At_each_mint_a_delegated_grant_is_cut_to_its_granter_s_current_scopes()
    {
        var manager = Manager();
        var worker = await WorkerAsync(manager, "tasks:read", "tasks:write");

        _grants.SetScopes(manager.Id, ["sessions:dispatch", "tasks:read", "agents:grant"]);

        var principal = await PrincipalOf(worker);
        Assert.Equal(["tasks:read"], McpScopePolicies.ScopesOf(principal));
        Assert.Equal(["tasks:read", "tasks:write"], _grants.Find(worker.Id)!.Scopes);

        _grants.SetScopes(manager.Id, ["sessions:dispatch", "tasks:read", "tasks:write", "agents:grant"]);
        Assert.Equal(["tasks:read", "tasks:write"], McpScopePolicies.ScopesOf(await PrincipalOf(worker)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Trimming_a_granter_refuses_its_workers_live_tokens_so_they_mint_again_under_the_new_cut()
    {
        var manager = Manager();
        var worker = await WorkerAsync(manager, "tasks:read", "tasks:write");
        var token = _tokens.Mint(worker.Id).Token!;

        _grants.SetScopes(manager.Id, ["sessions:dispatch", "tasks:read", "agents:grant"]);

        Assert.Equal(McpGrantRefusal.StaleVersion, (await _tokens.ValidateAsync(token.Value, Cancel)).Refusal);
        Assert.Equal(["tasks:read"], McpScopePolicies.ScopesOf(await PrincipalOf(worker)));
    }

    [Fact]
    public async Task A_granter_that_loses_agents_grant_takes_its_workers_tokens_with_it()
    {
        var manager = Manager();
        var worker = await WorkerAsync(manager, "tasks:read");

        _grants.SetScopes(manager.Id, ["sessions:dispatch", "tasks:read"]);

        Assert.Equal(McpGrantRefusal.GranterRevoked, _tokens.Mint(worker.Id).Refusal);
    }

    [Fact]
    public async Task An_expired_grant_is_not_renewed_by_an_edit_and_a_delegated_grant_without_an_expiry_fails_closed()
    {
        var manager = Manager();
        var worker = await WorkerAsync(manager, "tasks:read");
        _clock.Advance(TimeSpan.FromDays(8));

        Assert.Equal(McpAccessRefusals.Expired, (await _desk.EditScopesAsync(await PrincipalOf(manager), worker.Id, ["tasks:read"], Cancel)).Refusal);
        Assert.False((await _desk.RevokeAsync(await PrincipalOf(manager), worker.Id, Cancel)).Refused);

        Assert.True((worker with { ExpiresAt = null }).IsExpired(Now));
    }

    [Fact]
    public async Task The_person_cannot_widen_a_delegated_grant_and_is_told_to_revoke_and_allow_it_instead()
    {
        var manager = Manager();
        var worker = await WorkerAsync(manager, "tasks:read");
        var more = await _desk.RequestMoreAsync(await PrincipalOf(worker), ["runs:write"], null, Cancel);

        var refused = await _desk.AllowAsync(more.RequestId!, ["runs:write"], Cancel);

        Assert.Equal(McpAccessRefusals.Delegated, refused.Refusal);
        Assert.Equal(["tasks:read"], _grants.Find(worker.Id)!.Scopes);
    }

    [Fact]
    public async Task The_store_refuses_a_delegated_grant_under_a_revoked_granter_or_with_a_powerful_scope()
    {
        var manager = Manager();
        var request = await RegisterAsync("Worker", "tasks:read");

        Assert.Throws<ArgumentException>(() => _grants.Delegate(request.GrantId, ["tasks:admin"], manager.Id, Now.AddDays(7)));
        _grants.Revoke(manager.Id);
        Assert.Null(_grants.Delegate(request.GrantId, ["tasks:read"], manager.Id, Now.AddDays(7)));
    }

    [Fact]
    public async Task Revoking_a_granter_revokes_everything_it_granted_at_once()
    {
        var manager = Manager();
        var first = await WorkerAsync(manager, "tasks:read");
        var secondRequest = await RegisterAsync("Second worker", "tasks:read");
        await _desk.GrantAsync(await PrincipalOf(manager), secondRequest.Id, null, Cancel);
        var bystander = _grants.Create("Personal assistant", ["tasks:read"], "Settings");
        var firstToken = _tokens.Mint(first.Id).Token!;

        _grants.Revoke(manager.Id);

        Assert.True(_grants.Find(first.Id)!.Revoked);
        Assert.True(_grants.Find(secondRequest.GrantId)!.Revoked);
        Assert.False(_grants.Find(bystander.Id)!.Revoked);
        Assert.Equal(McpGrantRefusal.Revoked, (await _tokens.ValidateAsync(firstToken.Value, Cancel)).Refusal);
        Assert.Equal(McpGrantRefusal.Revoked, _tokens.Mint(first.Id).Refusal);
    }

    [Fact]
    public async Task A_granter_revokes_a_grant_it_made_and_the_audit_names_both()
    {
        var manager = Manager();
        var worker = await WorkerAsync(manager, "tasks:read");

        var answer = await _desk.RevokeAsync(await PrincipalOf(manager), worker.Id, Cancel);

        Assert.False(answer.Refused, answer.Message);
        Assert.True(_grants.Find(worker.Id)!.Revoked);
        var record = (await _audit.LatestAsync(1, Cancel))[0];
        Assert.Equal(McpAudit.DelegatedRevoke, record.Tool);
        Assert.Equal(McpScopePolicies.GrantCaller(manager), record.Caller);
        Assert.Equal(McpScopePolicies.GrantCaller(_grants.Find(worker.Id)!), record.TargetId);
    }

    [Fact]
    public async Task An_agent_without_agents_grant_or_without_a_grant_is_no_granter()
    {
        var plain = _grants.Create("Backlog delivery", ["tasks:read", "tasks:write"], "Settings");
        var request = await RegisterAsync("Worker", "tasks:read");

        Assert.Equal(McpAccessRefusals.NotGranter, (await _desk.GrantAsync(await PrincipalOf(plain), request.Id, null, Cancel)).Refusal);
        Assert.Equal(McpAccessRefusals.NotGranter, (await _desk.GrantAsync(null, request.Id, null, Cancel)).Refusal);
        Assert.Equal(McpAccessRefusals.NotGranter, _desk.List(new ClaimsPrincipal(new ClaimsIdentity())).Refusal);
        Assert.True(_requests.Find(request.Id)!.IsOpen);
    }

    [Fact]
    public async Task The_listing_holds_the_waiting_requests_and_only_the_grants_this_granter_made()
    {
        var manager = Manager();
        var worker = await WorkerAsync(manager, "tasks:read");
        var other = _grants.Create("Delivery orchestrator", ["tasks:read", "agents:grant"], "Settings");
        var request = await RegisterAsync("Waiting agent", "tasks:read");
        var person = _grants.Create("Backlog delivery", ["tasks:read"], "Settings");
        await _desk.RequestMoreAsync(await PrincipalOf(person), ["devbook:read"], null, Cancel);

        var listing = _desk.List(await PrincipalOf(manager));

        Assert.False(listing.Refused, listing.Message);
        Assert.Equal([request.Id], listing.Requests.Select(waiting => waiting.Id));
        Assert.Equal("register", listing.Requests[0].Kind);
        var granted = Assert.Single(listing.Granted);
        Assert.Equal(worker.Id, granted.Id);
        Assert.Equal(worker.ExpiresAt, granted.ExpiresAt);
        Assert.DoesNotContain(listing.Granted, grant => grant.Id == other.Id);
    }

    [Fact]
    public async Task A_request_the_granter_answered_is_closed_for_the_person_and_its_alert_settled()
    {
        var manager = Manager();
        var request = await RegisterAsync("Worker", "tasks:read");

        await _desk.GrantAsync(await PrincipalOf(manager), request.Id, null, Cancel);

        Assert.Equal(McpAccessRefusals.NotOpen, (await _desk.AllowAsync(request.Id, ["tasks:read"], Cancel)).Refusal);
        Assert.Contains(_notifications.ClosedById, closed => closed.Id == McpAccessDesk.NotificationIdFor(request.Id));
    }

    private async Task<ClaimsPrincipal> PrincipalOf(McpGrant grant)
    {
        var minted = _tokens.Mint(grant.Id);
        Assert.NotNull(minted.Token);
        return (await _tokens.ValidateAsync(minted.Token.Value, Cancel)).Principal!;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class AlwaysConsent : IUserConsent
    {
        public Task<UserConsentOutcome> VerifyAsync(string message, CancellationToken cancellationToken = default) =>
            Task.FromResult(UserConsentOutcome.Verified);
    }
}
