using Backlog.Modules.Sessions.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The pane asks its port and renders the answer. What is asserted here is the part
/// that is the pane's own: that the grouping control rearranges the rows without
/// removing any, that a source which could not be read is named rather than
/// swallowed, and that "no sessions" and "could not read" do not look alike.
/// </summary>
public sealed class SessionsPaneTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_session_is_a_row_saying_what_the_agent_recorded()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var rows = pane.FindAll(".data-table__row");

            Assert.Equal(3, rows.Count);

            // Most recently active first, before any grouping is asked for.
            var first = rows[0];

            Assert.Contains("keen-bose-667825", first.TextContent);
            Assert.Contains("Claude", first.TextContent);
            Assert.Contains("DEV-TOWER", first.TextContent);

            // The mark and the word together: the library draws Claude's and
            // Copilot's own monochrome marks, and the word is beside it.
            Assert.NotEmpty(first.QuerySelectorAll(".provider-mark"));

            // The state arrives as the library's chip, so colour is never the only
            // carrier of it.
            Assert.NotEmpty(first.QuerySelectorAll(".badge--integration-running"));
        });
    }

    /// <summary>
    /// What Claude does not record shows as an em dash rather than as a blank cell or
    /// a repository guessed from the folder. A wrong repository renders exactly as
    /// well as a right one.
    /// </summary>
    [Fact]
    public void What_an_agent_did_not_record_is_an_em_dash()
    {
        using var context = Context([Sample[0]]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.Contains("—", pane.Find(".data-table__row").TextContent));
    }

    [Fact]
    public void The_row_count_beside_the_title_counts_rows()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
            Assert.Equal("3 sessions", pane.Find("[data-testid='sessions-count']").TextContent.Trim()));
    }

    [Fact]
    public void Grouping_by_environment_is_a_section_per_machine()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-group-environment']")));
        pane.Find("[data-testid='sessions-group-environment']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(
                ["DEV-LAPTOP", "DEV-TOWER"],
                pane.FindAll(".data-table__group-name").Select(name => name.TextContent.Trim()));

            Assert.Equal(
                ["1 session", "2 sessions"],
                pane.FindAll(".data-table__group-count").Select(count => count.TextContent.Trim()));
        });
    }

    [Fact]
    public void Grouping_by_type_is_a_section_per_assistant()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-group-type']")));
        pane.Find("[data-testid='sessions-group-type']").Click();

        pane.WaitForAssertion(() => Assert.Equal(
            ["Claude", "Copilot"],
            pane.FindAll(".data-table__group-name").Select(name => name.TextContent.Trim())));
    }

    /// <summary>
    /// Grouping rearranges and never filters, which is why the count in the header is
    /// worth having beside a control that carves the same rows up.
    /// </summary>
    [Fact]
    public void Grouping_moves_rows_and_removes_none()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.Equal(3, pane.FindAll(".data-table__row").Count));

        foreach (var grouping in new[] { "environment", "type", "none" })
        {
            pane.Find($"[data-testid='sessions-group-{grouping}']").Click();

            pane.WaitForAssertion(() =>
            {
                Assert.Equal(3, pane.FindAll(".data-table__row").Count);
                Assert.Equal("3 sessions", pane.Find("[data-testid='sessions-count']").TextContent.Trim());
            });
        }
    }

    [Fact]
    public void Ungrouped_is_the_shape_the_pane_opens_in()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("true", pane.Find("[data-testid='sessions-group-none']").GetAttribute("aria-pressed"));
            Assert.Empty(pane.FindAll(".data-table__group"));
        });
    }

    /// <summary>
    /// One agent's folder being unreadable leaves the other agent's rows perfectly
    /// good. The reader has to be able to tell "no Copilot sessions" from "Copilot
    /// could not be read", which is why this is a notice above the rows rather than
    /// an empty state instead of them.
    /// </summary>
    [Fact]
    public void An_unreadable_source_is_named_above_the_rows_that_did_arrive()
    {
        using var context = Context([Sample[0]], unreadable: ["Copilot"]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var notice = pane.Find("[data-testid='sessions-unreadable']");

            Assert.Contains("Copilot", notice.TextContent);
            Assert.Single(pane.FindAll(".data-table__row"));
        });
    }

    [Fact]
    public void Nothing_read_and_nothing_wrong_is_an_empty_state_rather_than_an_empty_table()
    {
        using var context = Context([]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='sessions-unreadable']"));
            Assert.Empty(pane.FindAll("[data-testid='sessions-table-table']"));
            Assert.Contains("No sessions on this PC.", pane.Markup);
        });
    }

    /// <summary>
    /// There is no timer on this pane, so refreshing is the only way the list moves.
    /// A refresh that did not re-ask the port would make the button decoration.
    /// </summary>
    [Fact]
    public void Refreshing_asks_the_port_again()
    {
        var source = new StubSessionSource(Sample, [], Sample.Count);
        using var context = Context(source);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.Equal(1, source.Reads));

        pane.Find("[data-testid='sessions-refresh']").Click();

        pane.WaitForAssertion(() => Assert.Equal(2, source.Reads));
    }

    [Fact]
    public void The_close_control_reports_the_press_and_nothing_else()
    {
        using var context = Context(Sample);
        var closed = 0;

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(p => p.OnClose, () => closed++));

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll(".sessions-panel button.btn--ghost")));
        pane.Find(".sessions-panel button.btn--ghost").Click();

        // What that closes is the shell's business; the pane still shows its rows.
        Assert.Equal(1, closed);
        Assert.NotEmpty(pane.FindAll(".data-table__row"));
    }

    /// <summary>
    /// A capped list must never read as the whole history. Both numbers, in the line
    /// directly under the title, where somebody deciding whether their session is
    /// missing will actually see them.
    /// </summary>
    [Fact]
    public void A_capped_list_names_both_numbers()
    {
        using var context = Context(Sample, discovered: 842);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var subtitle = pane.Find("[data-testid='sessions-subtitle']").TextContent;

            Assert.Contains("3", subtitle);
            Assert.Contains("842", subtitle);
        });
    }

    [Fact]
    public void An_uncapped_list_does_not_hedge_about_being_complete()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var subtitle = pane.Find("[data-testid='sessions-subtitle']").TextContent;

            Assert.DoesNotContain("most recent of", subtitle);
        });
    }

    private static BunitContext Context(
        IReadOnlyList<AgentSession> sessions,
        IReadOnlyList<string>? unreadable = null,
        int? discovered = null) =>
        Context(new StubSessionSource(sessions, unreadable ?? [], discovered ?? sessions.Count));

    private static BunitContext Context(IAgentSessionSource source)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(source);

        return context;
    }

    private static readonly IReadOnlyList<AgentSession> Sample =
    [
        new(
            Id: "5905cf2d",
            Kind: AgentSessionKind.Claude,
            Environment: "DEV-TOWER",
            Title: "keen-bose-667825",
            WorkingFolder: @"D:\Repos\Backlog\.claude\worktrees\keen-bose-667825",
            Repository: null,
            Branch: "claude/desktop-session-area",
            StartedAt: Noon.AddHours(-2),
            LastActivityAt: Noon.AddMinutes(-3),
            State: AgentSessionState.Running),
        new(
            Id: "0012e2c7",
            Kind: AgentSessionKind.Copilot,
            Environment: "DEV-TOWER",
            Title: "JSdotNet/Backlog",
            WorkingFolder: @"C:\Users\dev\.copilot\repos\Backlog",
            Repository: "JSdotNet/Backlog",
            Branch: "main",
            StartedAt: Noon.AddDays(-1),
            LastActivityAt: Noon.AddHours(-4),
            State: AgentSessionState.Finished),
        new(
            Id: "9f21ab04",
            Kind: AgentSessionKind.Copilot,
            Environment: "DEV-LAPTOP",
            Title: "JSdotNet/Project-Guidelines-MCP",
            WorkingFolder: @"C:\Users\dev\.copilot\repos\project-guidelines-mcp",
            Repository: "JSdotNet/Project-Guidelines-MCP",
            Branch: "main",
            StartedAt: Noon.AddDays(-3),
            LastActivityAt: Noon.AddDays(-3).AddMinutes(20),
            State: AgentSessionState.Finished)
    ];

    private sealed class StubSessionSource(
        IReadOnlyList<AgentSession> sessions,
        IReadOnlyList<string> unreadable,
        int discovered) : IAgentSessionSource
    {
        internal int Reads { get; private set; }

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default)
        {
            Reads++;

            return Task.FromResult(new AgentSessionCatalog(sessions, unreadable, discovered));
        }
    }
}
