using System.Net;
using System.Text.Json;

using Backlog.Infrastructure.Claude;

namespace Backlog.Infrastructure.Claude.UnitTests;

/// <summary>
/// The "Test this account" button on a Claude card. Three questions, asked of
/// Anthropic in the order the card asks for them: does the key open an
/// organization, is the named actor a member of it, and does the workspace exist.
/// Each answers on its own line so a card that fails at step two says step one
/// passed.
/// </summary>
public sealed class ClaudeAccountProbeTests
{
    private static readonly ClaudeAccount Configured = new()
    {
        AdminApiKey = "sk-ant-admin01-example",
        Actor = "me@example.com",
        WorkspaceId = "wrkspc_01"
    };

    [Fact]
    public async Task A_key_that_opens_an_organization_names_it()
    {
        var transport = new RoutedTransport
        {
            ["v1/organizations/me"] = """{"id":"org_1","type":"organization","name":"Acme Corp"}""",
            ["v1/organizations/users?email=me%40example.com&limit=100"] =
                """{"data":[{"id":"user_1","email":"me@example.com","name":"Me","role":"developer"}],"has_more":false}""",
            ["v1/organizations/workspaces/wrkspc_01"] = """{"id":"wrkspc_01","name":"Product"}"""
        };

        var check = await new ClaudeAccountProbe(transport).CheckAsync(Configured, TestContext.Current.CancellationToken);

        Assert.True(check.Passed);
        Assert.Equal(["Admin API key", "Your Claude account", "Workspace"], check.Steps.Select(step => step.Subject).ToArray());
        Assert.All(check.Steps, step => Assert.Equal(ClaudeCheckOutcome.Passed, step.Outcome));
        Assert.Equal("Opens the organization Acme Corp.", check.Steps[0].Detail);
        Assert.Equal("me@example.com is a member of Acme Corp (developer).", check.Steps[1].Detail);
        Assert.Equal("Reports the workspace Product.", check.Steps[2].Detail);
    }

    /// <summary>A rejected key ends the run: the two questions after it are asked
    /// with the same key, so their answers would only repeat the refusal.</summary>
    [Fact]
    public async Task A_rejected_key_fails_the_first_step_and_skips_the_rest()
    {
        var transport = new RoutedTransport
        {
            ["v1/organizations/me"] = new ClaudeException("Anthropic rejected the admin key — check it hasn't been revoked.") { Status = HttpStatusCode.Unauthorized }
        };

        var check = await new ClaudeAccountProbe(transport).CheckAsync(Configured, TestContext.Current.CancellationToken);

        Assert.False(check.Passed);
        Assert.Equal(ClaudeCheckOutcome.Failed, check.Steps[0].Outcome);
        Assert.Equal("Anthropic rejected the admin key — check it hasn't been revoked.", check.Steps[0].Detail);
        Assert.Equal(ClaudeCheckOutcome.Skipped, check.Steps[1].Outcome);
        Assert.Equal(ClaudeCheckOutcome.Skipped, check.Steps[2].Outcome);
        Assert.Single(transport.Paths);
    }

    [Fact]
    public async Task An_actor_nobody_in_the_organization_has_is_said_so()
    {
        var transport = new RoutedTransport
        {
            ["v1/organizations/me"] = """{"id":"org_1","name":"Acme Corp"}""",
            ["v1/organizations/users?email=me%40example.com&limit=100"] = """{"data":[],"has_more":false}""",
            ["v1/organizations/workspaces/wrkspc_01"] = """{"id":"wrkspc_01","name":"Product"}"""
        };

        var check = await new ClaudeAccountProbe(transport).CheckAsync(Configured, TestContext.Current.CancellationToken);

        Assert.False(check.Passed);
        Assert.Equal(ClaudeCheckOutcome.Passed, check.Steps[0].Outcome);
        Assert.Equal(ClaudeCheckOutcome.Failed, check.Steps[1].Outcome);
        Assert.Equal("Nobody in Acme Corp has the email me@example.com. The dashboard would report nothing for it.", check.Steps[1].Detail);
        Assert.Equal(ClaudeCheckOutcome.Passed, check.Steps[2].Outcome);
    }

    /// <summary>The email filter is asked for and checked: an endpoint that ignores
    /// it hands back everybody, and the wrong somebody must not pass for you.</summary>
    [Fact]
    public async Task A_member_list_that_ignored_the_filter_is_still_searched_for_the_actor()
    {
        var transport = new RoutedTransport
        {
            ["v1/organizations/me"] = """{"id":"org_1","name":"Acme Corp"}""",
            ["v1/organizations/users?email=me%40example.com&limit=100"] =
                """{"data":[{"email":"other@example.com","role":"admin"},{"email":"ME@example.com","role":"user"}],"has_more":false}"""
        };

        var check = await new ClaudeAccountProbe(transport).CheckAsync(Configured with { WorkspaceId = null }, TestContext.Current.CancellationToken);

        Assert.Equal(ClaudeCheckOutcome.Passed, check.Steps[1].Outcome);
        Assert.Equal("me@example.com is a member of Acme Corp (user).", check.Steps[1].Detail);
    }

    [Fact]
    public async Task A_workspace_anthropic_cannot_find_is_said_so()
    {
        var transport = new RoutedTransport
        {
            ["v1/organizations/me"] = """{"id":"org_1","name":"Acme Corp"}""",
            ["v1/organizations/users?email=me%40example.com&limit=100"] = """{"data":[{"email":"me@example.com","role":"user"}]}""",
            ["v1/organizations/workspaces/wrkspc_01"] = new ClaudeException("Anthropic couldn't find that report — the API version may have moved on.") { Status = HttpStatusCode.NotFound }
        };

        var check = await new ClaudeAccountProbe(transport).CheckAsync(Configured, TestContext.Current.CancellationToken);

        Assert.False(check.Passed);
        Assert.Equal(ClaudeCheckOutcome.Failed, check.Steps[2].Outcome);
        Assert.Equal("Anthropic has no workspace with the id wrkspc_01 in Acme Corp.", check.Steps[2].Detail);
    }

    /// <summary>What is not filled in is not tested, and is not a failure either: the
    /// card's checklist already says which fields are still empty.</summary>
    [Fact]
    public async Task Blank_fields_are_skipped_rather_than_failed()
    {
        var transport = new RoutedTransport
        {
            ["v1/organizations/me"] = """{"id":"org_1","name":"Acme Corp"}"""
        };

        var check = await new ClaudeAccountProbe(transport).CheckAsync(new ClaudeAccount { AdminApiKey = "sk-ant-admin01-example" }, TestContext.Current.CancellationToken);

        Assert.True(check.Passed);
        Assert.Equal(ClaudeCheckOutcome.Skipped, check.Steps[1].Outcome);
        Assert.Equal("No account named yet.", check.Steps[1].Detail);
        Assert.Equal(ClaudeCheckOutcome.Skipped, check.Steps[2].Outcome);
        Assert.Equal("No workspace set - the whole organization is reported.", check.Steps[2].Detail);
        Assert.Single(transport.Paths);
    }

    [Fact]
    public async Task No_key_means_nothing_is_asked_at_all()
    {
        var transport = new RoutedTransport();

        var check = await new ClaudeAccountProbe(transport).CheckAsync(new ClaudeAccount { Actor = "me@example.com" }, TestContext.Current.CancellationToken);

        Assert.False(check.Passed);
        Assert.Equal(ClaudeCheckOutcome.Failed, check.Steps[0].Outcome);
        Assert.Equal("No key pasted yet.", check.Steps[0].Detail);
        Assert.Empty(transport.Paths);
    }

    /// <summary>A transport that answers each path from a table, or throws what the
    /// table holds for it.</summary>
    private sealed class RoutedTransport : IClaudeTransport
    {
        private readonly Dictionary<string, object> _answers = new(StringComparer.Ordinal);

        public object this[string path]
        {
            set => _answers[path] = value;
        }

        public List<string> Paths { get; } = [];

        public string Description => "stub";

        public Task<bool> IsAvailableAsync(ClaudeAccount account, CancellationToken cancellationToken = default) =>
            Task.FromResult(account.IsConfigured);

        public Task<JsonElement> SendAsync(ClaudeAccount account, HttpMethod method, string path, CancellationToken cancellationToken = default)
        {
            Paths.Add(path);

            if (!_answers.TryGetValue(path, out var answer))
            {
                throw new InvalidOperationException($"The test did not expect a call to {path}.");
            }

            if (answer is Exception exception) throw exception;

            return Task.FromResult(JsonDocument.Parse((string)answer).RootElement.Clone());
        }
    }
}
