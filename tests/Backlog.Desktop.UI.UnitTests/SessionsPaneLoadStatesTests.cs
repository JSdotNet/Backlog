using Backlog.Modules.Sessions.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the sessions pane shows while its first read is out, and what it offers
/// when a source could not be read (.devbook/design/interaction-guidelines.md,
/// Loading States and Error States).
/// <para>
/// The first read is a table on its way, so it is drawn as one — the library's
/// skeleton — rather than as a sentence in an alert. An unreadable source is a
/// failure of this section's read, so it is announced as an alert and carries the
/// way back: a Retry that asks both sources again. The two notices beside it are
/// news rather than failures and stay polite.
/// </para>
/// </summary>
public sealed class SessionsPaneLoadStatesTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_first_read_is_a_skeleton_of_the_table_rather_than_an_alert()
    {
        var sessions = new PendingSessionSource();
        using var context = Context(sessions, new CountingRunSource());

        var pane = context.Render<SessionsPane>();

        var placeholder = pane.Find("[data-testid='sessions-loading']");

        Assert.NotEmpty(placeholder.QuerySelectorAll(".skeleton"));
        Assert.Equal("true", placeholder.GetAttribute("aria-hidden"));
        Assert.Null(placeholder.GetAttribute("role"));
        Assert.DoesNotContain("app-error-message", placeholder.ClassList);
        Assert.DoesNotContain(pane.FindComponents<Alert>(), alert => alert.Instance.TestId == "sessions-loading");
        Assert.DoesNotContain("Reading the sessions", pane.Markup, StringComparison.Ordinal);

        sessions.Complete([Session()]);

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='sessions-loading']"));
            Assert.Single(pane.FindAll(".data-table__row"));
        });
    }

    [Fact]
    public void An_unreadable_source_is_an_alert_with_a_retry()
    {
        using var context = Context(new FlakySessionSource(unreadableReads: 1), new CountingRunSource());

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var notice = pane.Find("[data-testid='sessions-unreadable']");

            Assert.Equal("alert", notice.GetAttribute("role"));
            Assert.Equal("Retry", notice.QuerySelector("[data-testid='sessions-unreadable-retry']")!.TextContent.Trim());
        });
    }

    [Fact]
    public void Retry_asks_both_sources_again_and_a_clean_read_clears_the_alert()
    {
        var sessions = new FlakySessionSource(unreadableReads: 1);
        var runs = new CountingRunSource();
        using var context = Context(sessions, runs);

        var pane = context.Render<SessionsPane>();
        pane.WaitForElement("[data-testid='sessions-unreadable-retry']");

        Assert.Equal(1, sessions.Reads);
        Assert.Equal(1, runs.Reads);

        pane.Find("[data-testid='sessions-unreadable-retry']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(2, sessions.Reads);
            Assert.Equal(2, runs.Reads);
            Assert.Empty(pane.FindAll("[data-testid='sessions-unreadable']"));
            Assert.Single(pane.FindAll(".data-table__row"));
        });
    }

    /// <summary>A session asked for and not found is news about the request, not
    /// a failure of the read, so it stays a polite status beside the alert.</summary>
    [Fact]
    public void The_focus_notice_stays_a_status()
    {
        using var context = Context(new FlakySessionSource(unreadableReads: 0), new CountingRunSource());

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(p => p.FocusSessionId, "not-here"));

        pane.WaitForAssertion(() =>
            Assert.Equal("status", pane.Find("[data-testid='sessions-focus-missing']").GetAttribute("role")));
    }

    private static BunitContext Context(IAgentSessionSource sessions, IDeliveryRunSource runs)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(sessions);
        context.Services.AddSingleton(runs);
        return context;
    }

    private static AgentSession Session() =>
        new(
            Id: "5905cf2d",
            Kind: AgentSessionKind.Claude,
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            Title: "keen-bose-667825",
            WorkingFolder: @"D:\Repos\Backlog\.claude\worktrees\keen-bose-667825",
            Repository: null,
            Branch: "claude/desktop-session-area",
            StartedAt: Noon.AddHours(-2),
            LastActivityAt: Noon.AddMinutes(-3),
            State: AgentSessionState.Running,
            TurnCount: 12,
            Origin: AgentSessionOrigin.Local);

    /// <summary>A source whose first read has not come back.</summary>
    private sealed class PendingSessionSource : IAgentSessionSource
    {
        private readonly TaskCompletionSource<AgentSessionCatalog> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) => _answer.Task;

        public void Complete(IReadOnlyList<AgentSession> sessions) =>
            _answer.SetResult(new AgentSessionCatalog(sessions, [], sessions.Count));
    }

    /// <summary>A source that cannot read Copilot's folder for its first
    /// <c>unreadableReads</c> reads, and reads it cleanly after.</summary>
    private sealed class FlakySessionSource(int unreadableReads) : IAgentSessionSource
    {
        public int Reads { get; private set; }

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default)
        {
            Reads++;

            IReadOnlyList<string> unreadable = Reads <= unreadableReads ? ["Copilot"] : [];
            return Task.FromResult(new AgentSessionCatalog([Session()], unreadable, 1));
        }
    }

    private sealed class CountingRunSource : IDeliveryRunSource
    {
        public int Reads { get; private set; }

        public Task<DeliveryRunCatalog> GetRunsAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(new DeliveryRunCatalog([], []));
        }
    }
}
