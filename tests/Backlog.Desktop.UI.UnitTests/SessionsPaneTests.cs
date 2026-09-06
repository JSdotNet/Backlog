using Backlog.Modules.Sessions.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The pane asks its port and renders the answer. What is asserted here is the part
/// that is the pane's own: that it opens on the live sessions and says so in both
/// numbers, that the grouping control rearranges the rows without removing any
/// while the view is the control that does remove them, that a source which could
/// not be read is named rather than swallowed, and that "no sessions", "nothing
/// live" and "could not read" do not look alike.
/// </summary>
public sealed class SessionsPaneTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_live_session_is_a_row_saying_what_the_agent_recorded()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var rows = pane.FindAll(".data-table__row");

            // The two the machine has liveness evidence for, out of four records.
            Assert.Equal(2, rows.Count);

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

    /// <summary>
    /// The badge answers to the view, and it names both numbers whenever the view
    /// is hiding any. "2 sessions" over a catalog of four would be the badge
    /// agreeing with the rows and lying about the machine.
    /// </summary>
    [Fact]
    public void The_count_beside_the_title_says_how_many_of_how_many()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
            Assert.Equal("2 of 4 sessions", pane.Find("[data-testid='sessions-count']").TextContent.Trim()));

        pane.Find("[data-testid='sessions-view-all']").Click();

        // Nothing hidden, so nothing to qualify.
        pane.WaitForAssertion(() =>
            Assert.Equal("4 sessions", pane.Find("[data-testid='sessions-count']").TextContent.Trim()));
    }

    [Fact]
    public void Grouping_by_environment_is_a_section_per_machine()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        // All first, deliberately: this test is about the grouping, and measuring it
        // through the live view would make it an assertion about the filter instead.
        ShowAll(pane);
        pane.Find("[data-testid='sessions-group-environment']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(
                ["DEV-LAPTOP", "DEV-TOWER"],
                pane.FindAll(".data-table__group-name").Select(name => name.TextContent.Trim()));

            Assert.Equal(
                ["1 session", "3 sessions"],
                pane.FindAll(".data-table__group-count").Select(count => count.TextContent.Trim()));
        });
    }

    [Fact]
    public void Grouping_by_type_is_a_section_per_assistant()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        ShowAll(pane);
        pane.Find("[data-testid='sessions-group-type']").Click();

        pane.WaitForAssertion(() => Assert.Equal(
            ["Claude", "Copilot"],
            pane.FindAll(".data-table__group-name").Select(name => name.TextContent.Trim())));
    }

    /// <summary>
    /// Grouping rearranges and never filters. The view is the control that removes
    /// rows, so this holds the view still and moves only the grouping.
    /// </summary>
    [Fact]
    public void Grouping_moves_rows_and_removes_none()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        ShowAll(pane);
        pane.WaitForAssertion(() => Assert.Equal(4, pane.FindAll(".data-table__row").Count));

        foreach (var grouping in new[] { "environment", "type", "none" })
        {
            pane.Find($"[data-testid='sessions-group-{grouping}']").Click();

            pane.WaitForAssertion(() =>
            {
                Assert.Equal(4, pane.FindAll(".data-table__row").Count);
                Assert.Equal("4 sessions", pane.Find("[data-testid='sessions-count']").TextContent.Trim());
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

    // --- The view ---------------------------------------------------------

    [Fact]
    public void Live_is_the_view_the_pane_opens_in()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("true", pane.Find("[data-testid='sessions-view-live']").GetAttribute("aria-pressed"));
            Assert.Equal("false", pane.Find("[data-testid='sessions-view-all']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>
    /// Stalled is silence, not an ending. A session nobody has typed at for
    /// three quarters of an hour is still registered on the machine, and a live
    /// view that dropped it would be answering a question about typing rather than
    /// about what is running.
    /// </summary>
    [Fact]
    public void A_stalled_session_is_live_because_silence_is_not_an_ending()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var stalled = pane.FindAll(".data-table__row")
                .Single(row => row.TextContent.Contains("JSdotNet/Archify"));

            Assert.NotEmpty(stalled.QuerySelectorAll(".badge--integration-stalled"));
        });
    }

    [Fact]
    public void A_finished_session_is_not_in_the_live_view()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(2, pane.FindAll(".data-table__row").Count);
            Assert.DoesNotContain("JSdotNet/Project-Guidelines-MCP", pane.Markup);
            Assert.Empty(pane.FindAll(".badge--integration-finished"));
        });
    }

    [Fact]
    public void Switching_to_all_brings_the_finished_sessions_back()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        ShowAll(pane);

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(4, pane.FindAll(".data-table__row").Count);
            Assert.Contains("JSdotNet/Project-Guidelines-MCP", pane.Markup);
            Assert.Equal("false", pane.Find("[data-testid='sessions-view-live']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>
    /// A live view over an all-live catalog is hiding nothing, and "2 of 2
    /// sessions" would invite the reader to go looking for the two it left out.
    /// </summary>
    [Fact]
    public void The_count_says_one_number_when_the_view_hides_nothing()
    {
        using var context = Context([Sample[0], Sample[3]]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var count = pane.Find("[data-testid='sessions-count']").TextContent.Trim();

            Assert.Equal("2 sessions", count);
            Assert.DoesNotContain(" of ", count);
        });
    }

    /// <summary>
    /// The noun agrees with the total, not with the rows. One session record on the
    /// machine, over, and the pane opens on Live with nothing on screen: "0 of 1
    /// sessions" would have the badge contradicting the number it is quoting, and
    /// this is the shape of catalog — a fresh profile after one session — where the
    /// reader meets it first.
    /// </summary>
    [Fact]
    public void The_count_agrees_with_the_total_when_the_view_hides_the_only_session()
    {
        using var context = Context([Sample[1]]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll(".data-table__row"));
            Assert.Equal("0 of 1 session", pane.Find("[data-testid='sessions-count']").TextContent.Trim());
        });
    }

    /// <summary>
    /// "No sessions on this PC" over a machine holding three of them would be the
    /// pane blaming the machine for the reader's own filter. The empty state names
    /// the view, and names the way back out of it.
    /// </summary>
    [Fact]
    public void A_view_that_hides_everything_says_so_rather_than_saying_there_is_nothing()
    {
        using var context = Context([Sample[1], Sample[2]]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Contains("No live sessions right now.", pane.Markup);
            Assert.DoesNotContain("No sessions on this PC.", pane.Markup);

            // Both states named out loud, so the word "live" in the title above is
            // checkable against something rather than being a term of art.
            Assert.Contains("Choose All to see the 2 sessions it has a record of.", pane.Markup);
        });
    }

    /// <summary>
    /// The subtitle describes the reading, and the reading is the whole catalog.
    /// A capped and filtered list that said "the 2 most recent of 842" would be
    /// attributing the view's subtraction to the cap.
    /// </summary>
    [Fact]
    public void The_subtitle_counts_the_reading_and_not_the_view()
    {
        using var context = Context(Sample, discovered: 842);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var subtitle = pane.Find("[data-testid='sessions-subtitle']").TextContent;

            Assert.Contains("4", subtitle);
            Assert.Contains("842", subtitle);
            Assert.DoesNotContain("2 most recent", subtitle);
        });
    }

    /// <summary>
    /// The choice is not persisted anywhere — not in a store, not in a static
    /// field. A second pane is a second opening, and an opening starts on Live.
    /// </summary>
    [Fact]
    public void Reopening_the_pane_opens_on_live_again()
    {
        using var context = Context(Sample);

        var first = context.Render<SessionsPane>();

        ShowAll(first);
        first.WaitForAssertion(() =>
            Assert.Equal("true", first.Find("[data-testid='sessions-view-all']").GetAttribute("aria-pressed")));

        var second = context.Render<SessionsPane>();

        second.WaitForAssertion(() =>
            Assert.Equal("true", second.Find("[data-testid='sessions-view-live']").GetAttribute("aria-pressed")));
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

            Assert.Contains("4", subtitle);
            Assert.Contains("842", subtitle);

            // Capped and filtered are two different subtractions and the subtitle
            // owns only one of them. "The 2 most recent of 842" would be a claim
            // about the reading that the reading never made.
            Assert.DoesNotContain("2 most recent", subtitle);
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

    // --- Repository identity ---------------------------------------------

    [Fact]
    public void The_repository_cell_wears_the_colour_its_host_placed()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(p => p.RepositoryColour, repository =>
                repository == "JSdotNet/Backlog" ? 4 : null));

        // The session that carries the placed repository is a finished one, so this
        // asks for every row before measuring which of them wears a colour.
        ShowAll(pane);

        pane.WaitForAssertion(() =>
        {
            var marked = pane.FindAll(".sessions-table__repository")
                .Where(cell => cell.ClassName?.Contains("repo-mark") == true)
                .ToList();

            var cell = Assert.Single(marked);

            Assert.Contains("repo-mark--4", cell.ClassName);

            // On the cell that names the repository, not down the whole row: a row
            // here is a session, and the repository is one of eight things it says.
            Assert.Contains("JSdotNet/Backlog", cell.TextContent);
        });
    }

    [Fact]
    public void A_repository_the_host_cannot_place_wears_no_mark()
    {
        // The ordinary case: work done in a clone this workspace was never told
        // about. Colouring it would be inventing a project.
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(p => p.RepositoryColour, _ => null));

        // All, for the reason the test above asks for it: the same sample, so the
        // three colouring tests are measuring the same rows.
        ShowAll(pane);

        pane.WaitForAssertion(() => Assert.All(
            pane.FindAll(".sessions-table__repository"),
            cell => Assert.DoesNotContain("repo-mark", cell.ClassName)));
    }

    [Fact]
    public void With_no_host_answering_the_pane_colours_nothing()
    {
        // The pane declines to work it out. Which repositories exist is a workspace
        // question, and answering it here would tie sessions to repository
        // management — the same reason it holds owner/name as an opaque string.
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        ShowAll(pane);

        pane.WaitForAssertion(() => Assert.All(
            pane.FindAll(".sessions-table__repository"),
            cell => Assert.DoesNotContain("repo-mark", cell.ClassName)));
    }

    /// <summary>
    /// The header is the library SectionHeader, and the subtitle keeps the test
    /// id the capped-list wording is read through - a class hook alone would have
    /// lost it.
    /// </summary>
    [Fact]
    public void The_header_is_the_shared_component_wearing_this_panes_classes()
    {
        using var context = Context(Sample);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var header = pane.Find(".sessions-panel__header");

            SectionHeaderAdoptionTests.AssertPaneHeader(header, "sessions-panel", "sessions-title");
            SectionHeaderAdoptionTests.AssertPaneHeaderActions(header, "sessions-panel");

            var subtitle = header.QuerySelector("[data-testid='sessions-subtitle']");
            Assert.NotNull(subtitle);
            Assert.Equal("sessions-panel__subtitle", subtitle.GetAttribute("class"));
            Assert.NotNull(header.QuerySelector(".sessions-panel__header-actions .sessions-panel__count"));
        });
    }

    /// <summary>
    /// Switches to the whole list, and waits for the strip to say so. Several tests
    /// below are about the grouping rather than the view, and they have to get the
    /// view out of the way before they can measure the grouping at all.
    /// </summary>
    private static void ShowAll(IRenderedComponent<SessionsPane> pane)
    {
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-view-all']")));
        pane.Find("[data-testid='sessions-view-all']").Click();

        pane.WaitForAssertion(() =>
            Assert.Equal("true", pane.Find("[data-testid='sessions-view-all']").GetAttribute("aria-pressed")));
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
            State: AgentSessionState.Finished),
        // Quiet for three quarters of an hour, still registered. Here so the sample
        // holds one of each thing the view has to decide about — 1 Running, 1
        // Stalled, 2 Finished — and on DEV-TOWER so it moves a group's count
        // without moving any group's name.
        new(
            Id: "7c4d1e88",
            Kind: AgentSessionKind.Copilot,
            Environment: "DEV-TOWER",
            Title: "JSdotNet/Archify",
            WorkingFolder: @"C:\Users\dev\.copilot\repos\archify",
            Repository: "JSdotNet/Archify",
            Branch: "main",
            StartedAt: Noon.AddHours(-3),
            LastActivityAt: Noon.AddMinutes(-45),
            State: AgentSessionState.Stalled)
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
