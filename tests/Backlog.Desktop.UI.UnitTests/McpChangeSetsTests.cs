using System.Text.Json;

using Backlog.Desktop.UI.Mcp;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Changes;
using Backlog.SharedKernel.Mcp;
using Backlog.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// How MCP writes are grouped into change sets (local ADR 0061 §3): by session, or by caller
/// when a call names none, each within thirty minutes of the previous write, named by the
/// first call's tool and target and carrying its caller and correlation id. That the filter
/// runs inside the audit filter and hands the write its change set is
/// <c>McpEndpointGateTests</c>' over the real endpoint.
/// </summary>
public sealed class McpChangeSetsTests
{
    private const string Grant = "Backlog delivery (g_abcdefghijklmnop)";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero));

    private static Dictionary<string, JsonElement> Arguments(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private ValueTask<ChangeSet> WriteAsync(McpChangeSets changeSets, string caller, string tool, string json) =>
        McpChangeSetFilter.ChangeSetForAsync(
            changeSets, caller, tool, Arguments(json), services: null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Writes_from_one_session_within_thirty_minutes_are_one_change_set()
    {
        var changeSets = new McpChangeSets(_clock);

        var first = await WriteAsync(changeSets, Grant, "transition", """{"id":"4f1c","sessionId":"s-1"}""");
        _clock.Advance(TimeSpan.FromMinutes(29));
        var second = await WriteAsync(changeSets, Grant, "comment", """{"id":"9a2e","sessionId":"s-1"}""");
        _clock.Advance(TimeSpan.FromMinutes(29));
        var third = await WriteAsync(changeSets, Grant, "link_change", """{"id":"9a2e","sessionId":"s-1"}""");

        Assert.Same(first, second);
        Assert.Same(first, third);
        Assert.Equal("transition on 4f1c", third.Label);
    }

    [Fact]
    public async Task A_change_set_on_an_entry_is_named_by_the_entry_s_title()
    {
        var id = Guid.NewGuid();
        var entries = new RecordingTaskItems();
        entries.Held.Add((id, "Ship the journal"));
        await using var services = new ServiceCollection().AddSingleton<ITaskItems>(entries).BuildServiceProvider();

        var changeSet = await McpChangeSetFilter.ChangeSetForAsync(
            new McpChangeSets(_clock), Grant, "transition", Arguments($$"""{"id":"{{id}}"}"""), services,
            TestContext.Current.CancellationToken);

        Assert.Equal("transition on Ship the journal", changeSet.Label);
    }

    [Fact]
    public async Task A_write_more_than_thirty_minutes_after_the_previous_one_starts_a_new_change_set()
    {
        var changeSets = new McpChangeSets(_clock);

        var first = await WriteAsync(changeSets, Grant, "transition", """{"id":"4f1c","sessionId":"s-1"}""");
        _clock.Advance(McpChangeSets.Window + TimeSpan.FromSeconds(1));
        var second = await WriteAsync(changeSets, Grant, "comment", """{"id":"9a2e","sessionId":"s-1"}""");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("comment on 9a2e", second.Label);
        Assert.Equal(_clock.GetUtcNow(), second.StartedAt);
    }

    [Fact]
    public async Task Two_sessions_of_one_caller_are_two_change_sets()
    {
        var changeSets = new McpChangeSets(_clock);

        var first = await WriteAsync(changeSets, Grant, "comment", """{"id":"4f1c","sessionId":"s-1"}""");
        var second = await WriteAsync(changeSets, Grant, "comment", """{"id":"4f1c","sessionId":"s-2"}""");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("s-2", second.SessionId);
    }

    [Fact]
    public async Task Without_a_session_id_writes_from_one_caller_are_one_change_set_and_another_caller_has_its_own()
    {
        var changeSets = new McpChangeSets(_clock);

        var first = await WriteAsync(changeSets, McpAudit.UnauthenticatedCaller, "create_item", """{"repository":"o/r"}""");
        _clock.Advance(TimeSpan.FromMinutes(10));
        var second = await WriteAsync(changeSets, McpAudit.UnauthenticatedCaller, "transition", """{"id":"4f1c"}""");
        var other = await WriteAsync(changeSets, Grant, "transition", """{"id":"4f1c"}""");

        Assert.Same(first, second);
        Assert.Null(first.SessionId);
        Assert.Equal("create_item", first.Label);
        Assert.Equal(McpAudit.UnauthenticatedCaller, first.Caller);
        Assert.NotEqual(first.Id, other.Id);
        Assert.Equal(Grant, other.Caller);
    }

    [Fact]
    public async Task Without_a_session_id_a_write_more_than_thirty_minutes_later_starts_a_new_change_set()
    {
        var changeSets = new McpChangeSets(_clock);

        var first = await WriteAsync(changeSets, Grant, "comment", """{"id":"4f1c"}""");
        _clock.Advance(McpChangeSets.Window);
        var atTheEdge = await WriteAsync(changeSets, Grant, "comment", """{"id":"4f1c"}""");
        _clock.Advance(McpChangeSets.Window + TimeSpan.FromTicks(1));
        var after = await WriteAsync(changeSets, Grant, "comment", """{"id":"4f1c"}""");

        Assert.Same(first, atTheEdge);
        Assert.NotEqual(first.Id, after.Id);
    }

    [Fact]
    public async Task Another_caller_passing_the_same_session_id_has_its_own_change_set()
    {
        var changeSets = new McpChangeSets(_clock);

        var first = await WriteAsync(changeSets, Grant, "comment", """{"id":"4f1c","sessionId":"s-1"}""");
        var other = await WriteAsync(changeSets, McpAudit.UnauthenticatedCaller, "comment", """{"id":"4f1c","sessionId":"s-1"}""");

        Assert.NotEqual(first.Id, other.Id);
        Assert.Equal(McpAudit.UnauthenticatedCaller, other.Caller);
    }

    [Fact]
    public async Task First_writes_sent_at_once_share_one_change_set()
    {
        var changeSets = new McpChangeSets(_clock);

        var all = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(() =>
            WriteAsync(changeSets, Grant, "comment", $$"""{"id":"e-{{i}}","sessionId":"s-1"}""").AsTask(),
            TestContext.Current.CancellationToken)));

        Assert.Single(all.Select(changeSet => changeSet.Id).Distinct());
    }

    [Fact]
    public void A_call_whose_principal_names_nobody_is_the_host_s_caller()
    {
        Assert.Equal(McpAudit.HarnessCaller, McpChangeSetFilter.CallerOf(null, McpAudit.HarnessCaller));
        Assert.Equal(
            McpAudit.UnauthenticatedCaller,
            McpChangeSetFilter.CallerOf(McpScopePolicies.Unauthenticated([McpScopes.TasksWrite]), McpAudit.HarnessCaller));
    }

    [Fact]
    public async Task A_blank_session_id_counts_as_none()
    {
        var changeSets = new McpChangeSets(_clock);

        var first = await WriteAsync(changeSets, Grant, "comment", """{"id":"4f1c"}""");
        var second = await WriteAsync(changeSets, Grant, "comment", """{"id":"4f1c","sessionId":"  "}""");

        Assert.Same(first, second);
    }

    [Fact]
    public async Task The_change_set_is_an_agent_s_and_carries_the_grant_s_name_and_the_call_s_correlation_id()
    {
        var changeSets = new McpChangeSets(_clock);
        var grant = new McpGrant(
            "g_abcdefghijklmnop", "Backlog delivery", [McpScopes.TasksWrite], 1, "jobsc", _clock.GetUtcNow(), Revoked: false);
        var caller = McpChangeSetFilter.CallerOf(
            McpScopePolicies.Static(McpScopePolicies.GrantCaller(grant), [McpScopes.TasksWrite]),
            fallback: McpAudit.HarnessCaller);

        var changeSet = await McpCallAudit.WithCorrelationAsync(
            "4bf92f3577b34da6a3ce929d0e0e4736",
            () => WriteAsync(changeSets, caller, "transition", """{"id":"4f1c","sessionId":"s-1"}""").AsTask());

        Assert.Equal(ChangeActor.Agent, changeSet.Actor);
        Assert.Equal(Grant, changeSet.Caller);
        Assert.Equal("s-1", changeSet.SessionId);
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", changeSet.CorrelationId);
    }

    [Fact]
    public async Task A_later_write_keeps_the_first_call_s_correlation_id()
    {
        var changeSets = new McpChangeSets(_clock);

        var first = await McpCallAudit.WithCorrelationAsync(
            "first", () => WriteAsync(changeSets, Grant, "transition", """{"id":"4f1c"}""").AsTask());
        var second = await McpCallAudit.WithCorrelationAsync(
            "second", () => WriteAsync(changeSets, Grant, "comment", """{"id":"4f1c"}""").AsTask());

        Assert.Same(first, second);
        Assert.Equal("first", second.CorrelationId);
    }
}
