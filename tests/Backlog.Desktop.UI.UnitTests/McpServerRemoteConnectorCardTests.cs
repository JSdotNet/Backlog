using Backlog.Infrastructure.Sync;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Mcp;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Buttons;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using static Backlog.Desktop.UI.UnitTests.McpServerSettingsPageTests;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The MCP server page's Remote connector card (local ADR 0052 §8): the claude.ai
/// connector's client id and redirect URI, where its secret stands, Create secret showing
/// the plaintext once, Replace and Revoke confirmed in place, and nothing offered where
/// there is no sync client or the device never paired. The sync service is an in-memory
/// stand-in at <see cref="IRemoteConnector"/>; the client behind it is
/// RemoteConnectorClientTests'.
/// </summary>
public sealed class McpServerRemoteConnectorCardTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_host_with_no_sync_client_says_so_and_offers_nothing()
    {
        using var context = Context(new FakeMcpServer { IsListening = true });

        var page = context.Render<McpServerSettingsPage>();

        Assert.Equal(McpRemoteConnector.NoSyncClient, Text(page, "mcp-server-connector-note"));
        AssertOffersNothing(page);
    }

    [Fact]
    public void A_device_that_never_paired_says_so_and_offers_nothing()
    {
        var connector = new FakeConnector { IsPaired = false };
        using var context = ConnectorContext(connector);

        var page = context.Render<McpServerSettingsPage>();

        Assert.Equal(McpRemoteConnector.NotPaired, Text(page, "mcp-server-connector-note"));
        AssertOffersNothing(page);
        Assert.Equal(0, connector.StatusReads);
    }

    [Fact]
    public void With_no_secret_the_card_shows_what_to_enter_and_offers_Create_alone()
    {
        using var context = ConnectorContext(new FakeConnector());

        var page = context.Render<McpServerSettingsPage>();

        Assert.Equal("claude-connector", Text(page, "mcp-server-connector-client-id"));
        Assert.Equal("https://claude.ai/api/mcp/auth_callback", Text(page, "mcp-server-connector-redirect"));
        Assert.Equal("claude-connector", Copy(page, "mcp-server-connector-client-id-copy").Text);
        Assert.Equal("No secret yet. Create one to connect claude.ai.", Text(page, "mcp-server-connector-state"));
        Assert.Equal("Create secret", Text(page, "mcp-server-connector-create"));
        Assert.Empty(page.FindAll("[data-testid='mcp-server-connector-revoke']"));
        Assert.Empty(page.FindAll("[data-testid='mcp-server-connector-secret']"));
    }

    [Theory]
    [InlineData(null, "A secret is set: created 17 days ago · not used yet.")]
    [InlineData(3, "A secret is set: created 17 days ago · last used 3 min ago.")]
    public void A_secret_set_says_when_it_was_created_and_last_used(int? minutesAgo, string state)
    {
        var connector = new FakeConnector
        {
            Status = Status(hasSecret: true, Now.AddDays(-17), minutesAgo is { } m ? Now.AddMinutes(-m) : null)
        };
        using var context = ConnectorContext(connector);

        var page = context.Render<McpServerSettingsPage>();

        Assert.Equal(state, Text(page, "mcp-server-connector-state"));
        Assert.Equal("Replace secret", Text(page, "mcp-server-connector-create"));
        Assert.Single(page.FindAll("[data-testid='mcp-server-connector-revoke']"));
    }

    [Fact]
    public void Create_secret_shows_the_secret_once_with_a_copy_and_Done_closes_it_for_good()
    {
        var connector = new FakeConnector();
        using var context = ConnectorContext(connector);
        var page = context.Render<McpServerSettingsPage>();

        page.Find("[data-testid='mcp-server-connector-create']").Click();

        page.WaitForAssertion(() => Assert.Equal("s3cr3t-shown-once", Text(page, "mcp-server-connector-secret-value")));
        Assert.Equal(1, connector.Creates);
        Assert.Equal("s3cr3t-shown-once", Copy(page, "mcp-server-connector-secret-copy").Text);
        Assert.Equal(McpRemoteConnector.SecretShownOnce, Text(page, "mcp-server-connector-secret-note"));
        Assert.StartsWith("A secret is set: created just now", Text(page, "mcp-server-connector-state"), StringComparison.Ordinal);

        page.Find("[data-testid='mcp-server-connector-secret-done']").Click();

        Assert.Empty(page.FindAll("[data-testid='mcp-server-connector-secret']"));
        Assert.DoesNotContain("s3cr3t-shown-once", page.Markup, StringComparison.Ordinal);
    }

    /// <summary>The status never carries the secret, so the page drawn again - after leaving
    /// it and coming back - has none to show.</summary>
    [Fact]
    public void A_page_drawn_again_after_Create_shows_no_secret()
    {
        var connector = new FakeConnector();
        using var context = ConnectorContext(connector);
        var first = context.Render<McpServerSettingsPage>();
        first.Find("[data-testid='mcp-server-connector-create']").Click();
        first.WaitForAssertion(() => Assert.Single(first.FindAll("[data-testid='mcp-server-connector-secret']")));

        var again = context.Render<McpServerSettingsPage>();

        Assert.Empty(again.FindAll("[data-testid='mcp-server-connector-secret']"));
        Assert.DoesNotContain("s3cr3t-shown-once", again.Markup, StringComparison.Ordinal);
        Assert.StartsWith("A secret is set", Text(again, "mcp-server-connector-state"), StringComparison.Ordinal);
    }

    [Fact]
    public void Replace_secret_asks_in_place_and_Keep_creates_nothing()
    {
        var connector = new FakeConnector { Status = Status(hasSecret: true, Now.AddDays(-2), null) };
        using var context = ConnectorContext(connector);
        var page = context.Render<McpServerSettingsPage>();

        page.Find("[data-testid='mcp-server-connector-create']").Click();

        Assert.Equal(McpRemoteConnector.ReplaceQuestion, Text(page, "mcp-server-connector-replace-question"));
        Assert.Equal(0, connector.Creates);

        page.Find("[data-testid='mcp-server-connector-keep']").Click();

        Assert.Empty(page.FindAll("[data-testid='mcp-server-connector-replace-question']"));
        Assert.Equal(0, connector.Creates);

        page.Find("[data-testid='mcp-server-connector-create']").Click();
        page.Find("[data-testid='mcp-server-connector-replace-confirm']").Click();

        page.WaitForAssertion(() => Assert.Equal("s3cr3t-shown-once", Text(page, "mcp-server-connector-secret-value")));
        Assert.Equal(1, connector.Creates);
    }

    [Fact]
    public void Revoke_asks_in_place_and_Keep_leaves_the_secret_alone()
    {
        var connector = new FakeConnector { Status = Status(hasSecret: true, Now.AddDays(-2), null) };
        using var context = ConnectorContext(connector);
        var page = context.Render<McpServerSettingsPage>();

        page.Find("[data-testid='mcp-server-connector-revoke']").Click();

        Assert.Equal(McpRemoteConnector.RevokeQuestion, Text(page, "mcp-server-connector-revoke-question"));
        Assert.Empty(page.FindAll("[data-testid='mcp-server-connector-create']"));
        Assert.Equal(0, connector.Revokes);

        page.Find("[data-testid='mcp-server-connector-keep']").Click();

        Assert.Empty(page.FindAll("[data-testid='mcp-server-connector-revoke-question']"));
        Assert.Single(page.FindAll("[data-testid='mcp-server-connector-revoke']"));
        Assert.Equal(0, connector.Revokes);
    }

    [Fact]
    public void The_second_Revoke_revokes_and_the_card_offers_Create_again()
    {
        var connector = new FakeConnector { Status = Status(hasSecret: true, Now.AddDays(-2), Now.AddHours(-1)) };
        using var context = ConnectorContext(connector);
        var page = context.Render<McpServerSettingsPage>();

        page.Find("[data-testid='mcp-server-connector-revoke']").Click();
        page.Find("[data-testid='mcp-server-connector-revoke-confirm']").Click();

        page.WaitForAssertion(() => Assert.Equal("No secret yet. Create one to connect claude.ai.", Text(page, "mcp-server-connector-state")));
        Assert.Equal(1, connector.Revokes);
        Assert.Equal("Create secret", Text(page, "mcp-server-connector-create"));
        Assert.Empty(page.FindAll("[data-testid='mcp-server-connector-revoke']"));
    }

    [Fact]
    public void A_status_the_service_refuses_is_said_in_an_alert_and_only_Try_again_is_offered()
    {
        var connector = new FakeConnector
        {
            StatusFailure = new Error(SyncErrorCodes.ConnectorNotOwner, "Only the owner's devices manage the connector.")
        };
        using var context = ConnectorContext(connector);

        var page = context.Render<McpServerSettingsPage>();

        Assert.Equal("Only the owner's devices manage the connector.", Text(page, "mcp-server-connector-error"));
        Assert.Contains("setting__status--error", page.Find("[data-testid='mcp-server-connector-error']").ClassName, StringComparison.Ordinal);
        var buttons = page.Find("[data-testid='mcp-server-connector-card']").QuerySelectorAll("button");
        Assert.Equal(["Try again"], buttons.Select(button => button.TextContent.Trim()));
        Assert.Empty(page.FindAll("[data-testid='mcp-server-connector-client-id']"));
    }

    [Fact]
    public void Try_again_reads_the_status_again_and_clears_the_error()
    {
        var connector = new FakeConnector
        {
            StatusFailure = new Error(DevicePairingClient.UnreachableCode, "The sync service could not be reached.")
        };
        using var context = ConnectorContext(connector);
        var page = context.Render<McpServerSettingsPage>();
        connector.StatusFailure = null;

        page.Find("[data-testid='mcp-server-connector-retry']").Click();

        page.WaitForAssertion(() => Assert.Equal("claude-connector", Text(page, "mcp-server-connector-client-id")));
        Assert.Equal(2, connector.StatusReads);
        Assert.Empty(page.FindAll("[data-testid='mcp-server-connector-error']"));
        Assert.Empty(page.FindAll("[data-testid='mcp-server-connector-retry']"));
    }

    /// <summary>The activity is a local read and the connector a network call: a sync
    /// service that does not answer holds back the connector card alone.</summary>
    [Fact]
    public void The_activity_shows_while_the_connector_status_is_still_on_its_way()
    {
        var status = new TaskCompletionSource<Result<ConnectorStatusResponse>>();
        var connector = new FakeConnector { StatusAnswer = status.Task };
        var audit = new FakeAuditLog(
            new McpAuditRecord(Now, McpAudit.SharedTokenCaller, "transition", "4f1c9e", McpCallOutcome.Succeeded, "trace-1"));
        using var context = ConnectorContext(connector, audit);

        var page = context.Render<McpServerSettingsPage>();

        Assert.Equal(["Succeeded"], Column(page, "mcp-server-activity-outcome"));
        Assert.Empty(page.FindAll("[data-testid='mcp-server-connector-client-id']"));

        status.SetResult(Result.Success(Status(hasSecret: false, null, null)));

        page.WaitForAssertion(() => Assert.Equal("claude-connector", Text(page, "mcp-server-connector-client-id")));
    }

    /// <summary>The secret on screen is the live one even when the status read after Create
    /// fails: a second press still asks before it replaces it.</summary>
    [Fact]
    public void After_Create_a_failed_reread_still_makes_the_next_press_ask_first()
    {
        var connector = new FakeConnector { FailStatusAfterCreate = true };
        using var context = ConnectorContext(connector);
        var page = context.Render<McpServerSettingsPage>();

        page.Find("[data-testid='mcp-server-connector-create']").Click();
        page.WaitForAssertion(() => Assert.Equal("s3cr3t-shown-once", Text(page, "mcp-server-connector-secret-value")));

        Assert.Equal("Replace secret", Text(page, "mcp-server-connector-create"));
        Assert.StartsWith("A secret is set: created just now", Text(page, "mcp-server-connector-state"), StringComparison.Ordinal);

        page.Find("[data-testid='mcp-server-connector-create']").Click();

        Assert.Equal(McpRemoteConnector.ReplaceQuestion, Text(page, "mcp-server-connector-replace-question"));
        Assert.Equal(1, connector.Creates);
    }

    /// <summary>Leaving the page mid-Create cancels the call, and a secret that answers anyway
    /// is never kept by the page that went away. Losing it is inherent: the service already
    /// replaced the old one, so the person creates another.</summary>
    [Fact]
    public void Leaving_the_page_mid_Create_cancels_the_call_and_keeps_no_secret()
    {
        var created = new TaskCompletionSource<Result<ConnectorSecretResponse>>();
        var connector = new FakeConnector { CreateAnswer = created.Task };
        using var context = ConnectorContext(connector);
        var page = context.Render<McpServerSettingsPage>();
        page.Find("[data-testid='mcp-server-connector-create']").Click();
        var instance = page.Instance;

        instance.Dispose();

        Assert.True(connector.CreateToken.IsCancellationRequested);

        created.SetResult(Result.Success(new ConnectorSecretResponse("claude-connector", "late-secret", Now)));

        page.WaitForAssertion(() => Assert.False((bool)Field(instance, "_connectorBusy")!));
        Assert.Null(Field(instance, "_connectorSecret"));
    }

    [Fact]
    public void A_failed_Create_says_why_and_shows_no_secret()
    {
        var connector = new FakeConnector
        {
            CreateFailure = new Error(DevicePairingClient.UnreachableCode, "The sync service could not be reached: No such host is known.")
        };
        using var context = ConnectorContext(connector);
        var page = context.Render<McpServerSettingsPage>();

        page.Find("[data-testid='mcp-server-connector-create']").Click();

        page.WaitForAssertion(() => Assert.Equal(
            "The sync service could not be reached: No such host is known.",
            Text(page, "mcp-server-connector-error")));
        Assert.Empty(page.FindAll("[data-testid='mcp-server-connector-secret']"));
        Assert.Equal("Create secret", Text(page, "mcp-server-connector-create"));
    }

    [Fact]
    public void The_card_sits_between_Agents_and_Activity_and_its_title_is_a_page_card_title()
    {
        using var context = ConnectorContext(new FakeConnector());

        var page = context.Render<McpServerSettingsPage>();

        var cards = page.FindAll("section.settings-card").Select(card => card.GetAttribute("data-testid")).ToList();
        Assert.Equal(cards.IndexOf("mcp-server-agents-card") + 1, cards.IndexOf("mcp-server-connector-card"));
        Assert.Equal(cards.IndexOf("mcp-server-connector-card") + 1, cards.IndexOf("mcp-server-activity-card"));
        Assert.Equal("Remote connector", page.Find("#mcp-server-connector-title").TextContent.Trim());
        Assert.Contains(
            McpServerSettingsPage.RemoteConnectorCardTitle,
            SettingsPages.BuiltIn.Single(p => p.Id == SettingsPages.McpServer).CardTitles!);
    }

    private static void AssertOffersNothing(IRenderedComponent<McpServerSettingsPage> page)
    {
        var card = page.Find("[data-testid='mcp-server-connector-card']");
        Assert.Empty(card.QuerySelectorAll("button"));
        Assert.Empty(page.FindAll("[data-testid='mcp-server-connector-client-id']"));
    }

    private static BunitContext ConnectorContext(FakeConnector connector, FakeAuditLog? audit = null)
    {
        var context = Context(new FakeMcpServer { IsListening = true }, audit);
        context.Services.AddSingleton<IRemoteConnector>(connector);
        context.Services.AddSingleton<TimeProvider>(connector.Clock);
        return context;
    }

    private static CopyButton Copy(IRenderedComponent<McpServerSettingsPage> page, string testId) =>
        page.FindComponents<CopyButton>().Single(button => button.Instance.TestId == testId).Instance;

    private static object? Field(McpServerSettingsPage page, string name) =>
        typeof(McpServerSettingsPage)
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(page);

    private static ConnectorStatusResponse Status(bool hasSecret, DateTimeOffset? createdAt, DateTimeOffset? lastUsedAt) =>
        new(hasSecret, createdAt, lastUsedAt, "claude-connector", new Uri("https://claude.ai/api/mcp/auth_callback"));

    /// <summary>The sync service as the card sees it: one connector, whose secret Create sets
    /// and Revoke clears, every call counted.</summary>
    private sealed class FakeConnector : IRemoteConnector
    {
        public FakeTimeProvider Clock { get; } = new(Now);

        public bool IsPaired { get; set; } = true;

        public ConnectorStatusResponse Status { get; set; } = McpServerRemoteConnectorCardTests.Status(false, null, null);

        public Error? StatusFailure { get; set; }

        public Error? CreateFailure { get; set; }

        /// <summary>When set, the status read answers this instead of at once.</summary>
        public Task<Result<ConnectorStatusResponse>>? StatusAnswer { get; set; }

        /// <summary>When set, Create answers this instead of at once.</summary>
        public Task<Result<ConnectorSecretResponse>>? CreateAnswer { get; set; }

        /// <summary>Every status read after a successful Create fails.</summary>
        public bool FailStatusAfterCreate { get; set; }

        public CancellationToken CreateToken { get; private set; }

        public int StatusReads { get; private set; }

        public int Creates { get; private set; }

        public int Revokes { get; private set; }

        public Task<Result<ConnectorStatusResponse>> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            StatusReads++;
            if (StatusAnswer is { } answer) return answer;
            if (FailStatusAfterCreate && Creates > 0)
            {
                return Task.FromResult(Result.Failure<ConnectorStatusResponse>(
                    new Error(DevicePairingClient.UnreachableCode, "The sync service could not be reached.")));
            }

            return Task.FromResult(StatusFailure is { } failure
                ? Result.Failure<ConnectorStatusResponse>(failure)
                : Result.Success(Status));
        }

        public Task<Result<ConnectorSecretResponse>> CreateSecretAsync(CancellationToken cancellationToken = default)
        {
            CreateToken = cancellationToken;
            if (CreateAnswer is { } answer) return answer;
            if (CreateFailure is { } failure) return Task.FromResult(Result.Failure<ConnectorSecretResponse>(failure));

            Creates++;
            var at = Clock.GetUtcNow();
            Status = Status with { HasSecret = true, CreatedAt = at, LastUsedAt = null };
            return Task.FromResult(Result.Success(new ConnectorSecretResponse(Status.ClientId, "s3cr3t-shown-once", at)));
        }

        public Task<Result> RevokeSecretAsync(CancellationToken cancellationToken = default)
        {
            Revokes++;
            Status = Status with { HasSecret = false, CreatedAt = null, LastUsedAt = null };
            return Task.FromResult(Result.Success());
        }
    }
}
