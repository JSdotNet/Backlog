using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI;
using Backlog.Tests;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// The Schedules page inside the Sessions takeover (local ADR 0051 §6): the header's
/// button and its badge, the page read from the schedule register against the session
/// records, the breadcrumb back to the list with its filters and selection as they were,
/// and Open run, which comes back to the list on the run's session.
/// </summary>
public sealed class SessionsPaneSchedulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 7, 12, 0, TimeSpan.Zero);

    private static ScheduledRunReport Report(string schedule, string session, ScheduledRunEvent @event, IReadOnlyList<string>? needsYou = null, string? heldBy = null) =>
        new(schedule, schedule == "issue-sweep" ? "Issue sweep" : "Weekly update", "JSdotNet/Backlog", "0 * * * *", session, @event, NeedsYou: needsYou, HeldBy: heldBy);

    /// <summary>The weekly update needs the person, and the issue sweep is held by its
    /// Friday run; both ran an hour ago, against an hourly cron, so neither reads Missed.</summary>
    private static async Task<InMemoryScheduleRegister> RegisterAsync()
    {
        var register = new InMemoryScheduleRegister();
        // Within ten minutes of now, so no zone's hourly due time has passed its grace.
        var at = Now.AddMinutes(-6);

        await register.RecordAsync(Report("weekly-update", "sun-run", ScheduledRunEvent.Started), at);
        await register.RecordAsync(Report("weekly-update", "sun-run", ScheduledRunEvent.Finished, needsYou: ["Review two pull requests"]), at.AddMinutes(5));
        await register.RecordAsync(Report("issue-sweep", "fri-run", ScheduledRunEvent.Started), at.AddMinutes(-10));
        await register.RecordAsync(Report("issue-sweep", "fri-run", ScheduledRunEvent.Finished, needsYou: ["Answer the proposals"]), at.AddMinutes(-5));
        await register.RecordAsync(Report("issue-sweep", "tue-run", ScheduledRunEvent.Started), at.AddMinutes(1));
        await register.RecordAsync(Report("issue-sweep", "tue-run", ScheduledRunEvent.Skipped, heldBy: "fri-run"), at.AddMinutes(2));

        return register;
    }

    private static AgentSession Session(string id, AgentSessionState state, int minutesAgo, bool archived = false) =>
        new(
            Id: id,
            Kind: AgentSessionKind.Claude,
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            Title: $"Session {id}",
            WorkingFolder: $@"D:\Repos\Backlog\.claude\worktrees\{id}",
            Repository: "JSdotNet/Backlog",
            Branch: "main",
            StartedAt: Now.AddMinutes(-minutesAgo - 10),
            LastActivityAt: Now.AddMinutes(-minutesAgo),
            State: state,
            TurnCount: 4,
            Origin: AgentSessionOrigin.Local)
        {
            Archived = archived,
        };

    private static BunitContext Context(IScheduleRegister? register, params AgentSession[] sessions)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IAgentSessionSource>(new Sessions(sessions));
        context.Services.AddSingleton<IDeliveryRunSource>(new SessionsPaneTests.StubRunSource([], []));
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        if (register is not null) context.Services.AddSingleton(register);

        return context;
    }

    private static readonly AgentSession[] Sample =
    [
        Session("running-1", AgentSessionState.Running, 1),
        Session("running-2", AgentSessionState.Running, 2),
        Session("sun-run", AgentSessionState.Finished, 40),
        Session("fri-run", AgentSessionState.Finished, 60),
    ];

    [Fact]
    public async Task The_header_offers_Schedules_with_a_badge_counting_the_schedules_that_need_the_person()
    {
        using var context = Context(await RegisterAsync(), Sample);
        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var button = pane.Find("[data-testid='sessions-open-schedules']");
            Assert.StartsWith("Schedules", button.TextContent.Trim(), StringComparison.Ordinal);
            Assert.StartsWith("2", button.QuerySelector("[data-testid='sessions-schedules-badge']")!.TextContent.Trim(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void No_schedule_register_leaves_the_button_without_a_badge_and_the_page_empty()
    {
        using var context = Context(null, Sample);
        var pane = context.Render<SessionsPane>();

        pane.WaitForElement("[data-testid='sessions-row']");
        Assert.Empty(pane.FindAll("[data-testid='sessions-schedules-badge']"));

        pane.Find("[data-testid='sessions-open-schedules']").Click();

        pane.WaitForElement("[data-testid='schedules-empty']");
    }

    [Fact]
    public async Task The_page_reads_the_register_against_the_session_records()
    {
        // The weekly update's session is archived: the run reads Handled, and only the
        // held sweep still needs the person.
        using var context = Context(
            await RegisterAsync(),
            Session("running-1", AgentSessionState.Running, 1),
            Session("sun-run", AgentSessionState.Finished, 40, archived: true),
            Session("fri-run", AgentSessionState.Finished, 60));
        var pane = context.Render<SessionsPane>();

        pane.WaitForElement("[data-testid='sessions-row']");
        pane.Find("[data-testid='sessions-open-schedules']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("Handled", pane.Find("[data-testid='schedules-row'][data-schedule='weekly-update'] [data-testid='schedules-row-chip']").TextContent.Trim());
            Assert.Equal("Held", pane.Find("[data-testid='schedules-row'][data-schedule='issue-sweep'] [data-testid='schedules-row-chip']").TextContent.Trim());
            Assert.Equal(["issue-sweep"], pane.FindAll("[data-testid='schedules-card']").Select(card => card.GetAttribute("data-schedule")));
        });
    }

    [Fact]
    public async Task The_breadcrumb_comes_back_to_the_list_with_its_filters_and_selection_as_they_were()
    {
        using var context = Context(await RegisterAsync(), Sample);
        var pane = context.Render<SessionsPane>();

        pane.WaitForElement("[data-testid='sessions-row']");
        pane.Find("[data-testid='sessions-view-all']").Click();
        pane.Find("[data-testid='sessions-row'][data-row-key='Claude/running-2']").Click();
        pane.WaitForAssertion(() =>
            Assert.Equal("Claude/running-2", pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key")));

        pane.Find("[data-testid='sessions-open-schedules']").Click();

        pane.WaitForElement("[data-testid='schedules-page']");
        Assert.Empty(pane.FindAll("[data-testid='sessions-panel']"));

        pane.Find("[data-testid='schedules-back']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("true", pane.Find("[data-testid='sessions-view-all']").GetAttribute("aria-pressed"));
            Assert.Equal("Claude/running-2", pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key"));
        });
    }

    [Fact]
    public async Task Back_puts_the_focus_on_the_Schedules_button_again()
    {
        using var context = Context(await RegisterAsync(), Sample);
        var pane = context.Render<SessionsPane>();

        pane.WaitForElement("[data-testid='sessions-row']");
        pane.Find("[data-testid='sessions-open-schedules']").Click();
        pane.WaitForElement("[data-testid='schedules-page']");

        Assert.Equal("schedules-page", context.JSInterop.Invocations["backlogFocus"][^1].Arguments[0]);

        pane.Find("[data-testid='schedules-back']").Click();

        pane.WaitForAssertion(() =>
            Assert.Equal("sessions-open-schedules", context.JSInterop.Invocations["backlogFocus"][^1].Arguments[0]));
    }

    [Fact]
    public async Task The_header_scope_narrows_the_page_and_its_badge_to_the_scoped_repositories()
    {
        var register = await RegisterAsync();
        await register.RecordAsync(
            new ScheduledRunReport("finance-sweep", "Finance sweep", "JSdotNet/finance", "0 * * * *", "fin-run", ScheduledRunEvent.Finished, NeedsYou: ["Check the import"]),
            Now.AddMinutes(-3),
            TestContext.Current.CancellationToken);
        using var context = Context(register, Sample);

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(p => p.RepositoryScope, ["finance"])
            .Add(p => p.RepositoryAlias, repository => repository switch
            {
                "JSdotNet/finance" => "finance",
                "JSdotNet/Backlog" => "backlog",
                _ => null,
            }));

        pane.WaitForAssertion(() =>
            Assert.StartsWith("1", pane.Find("[data-testid='sessions-schedules-badge']").TextContent.Trim(), StringComparison.Ordinal));

        pane.Find("[data-testid='sessions-open-schedules']").Click();

        pane.WaitForAssertion(() =>
            Assert.Equal(["finance-sweep"], pane.FindAll("[data-testid='schedules-row']").Select(row => row.GetAttribute("data-schedule"))));
    }

    [Fact]
    public async Task Open_run_comes_back_to_the_list_on_the_runs_session_widening_the_filters_to_it()
    {
        using var context = Context(await RegisterAsync(), Sample);
        var pane = context.Render<SessionsPane>();

        // The list opens on Live, which a finished run's session is not in.
        pane.WaitForElement("[data-testid='sessions-row']");
        Assert.Equal("true", pane.Find("[data-testid='sessions-view-live']").GetAttribute("aria-pressed"));

        pane.Find("[data-testid='sessions-open-schedules']").Click();
        pane.WaitForElement("[data-testid='schedules-card']");

        var held = pane.Find("[data-testid='schedules-card'][data-schedule='issue-sweep']");
        held.QuerySelector("[data-testid='schedules-card-open']")!.Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='schedules-page']"));
            Assert.Equal("Claude/fri-run", pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key"));
            Assert.Equal("true", pane.Find("[data-testid='sessions-row'][data-row-key='Claude/fri-run']").GetAttribute("data-focused"));
        });
    }

    [Fact]
    public async Task Open_run_on_a_session_this_PC_has_no_record_of_stays_on_the_page_and_says_so()
    {
        using var context = Context(
            await RegisterAsync(),
            Session("running-1", AgentSessionState.Running, 1));
        var pane = context.Render<SessionsPane>();

        pane.WaitForElement("[data-testid='sessions-row']");
        pane.Find("[data-testid='sessions-open-schedules']").Click();
        pane.WaitForElement("[data-testid='schedules-card']");

        pane.Find("[data-testid='schedules-card'][data-schedule='weekly-update'] [data-testid='schedules-card-open']").Click();

        pane.WaitForAssertion(() =>
            Assert.Contains("(sun-run)", pane.Find("[data-testid='schedules-open-missing']").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_register_that_cannot_be_read_says_so_on_the_page_and_leaves_the_list_alone()
    {
        var register = await RegisterAsync();
        register.Failure = new IOException("The database is locked.");
        using var context = Context(register, Sample);
        var pane = context.Render<SessionsPane>();

        pane.WaitForElement("[data-testid='sessions-row']");
        pane.Find("[data-testid='sessions-open-schedules']").Click();

        pane.WaitForAssertion(() =>
            Assert.Contains("The database is locked.", pane.Find("[data-testid='schedules-error']").TextContent, StringComparison.Ordinal));
    }

    private sealed class Sessions(IReadOnlyList<AgentSession> sessions) : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog(sessions, [], sessions.Count));
    }
}
