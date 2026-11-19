using Backlog.Infrastructure.Sessions;
using Backlog.Modules.Sessions.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// A run file written while the pane is open — by the delivery surface's MCP tools or
/// a validation answer — reaches the pane through <see cref="IDeliveryRunChangeSignal"/>:
/// the runs are read again in place, the sessions are not, and what the reader had
/// open stays open. Raised off the renderer's thread, as the MCP request's is.
/// </summary>
public sealed class SessionsPaneRunRefreshTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private const string Folder = @"D:\Repos\Backlog\.claude\worktrees\keen-bose-667825";

    private static readonly string Worktree = DeliveryRunWorktrees.KeyOf(Folder)!;

    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task A_raise_shows_a_new_run_and_a_stage_change_without_reading_the_sessions_again()
    {
        var sessions = new StubSessionSource([Live]);
        var runs = new ChangingRunSource([InProgress("run-1", "in_progress", "pending")]);
        var signal = new DeliveryRunChangeSignal();
        using var context = Context(sessions, runs, signal);

        var pane = context.Render<SessionsPane>();
        pane.WaitForAssertion(() => Assert.Equal("orch-feature · Implement (2 of 3)", Summary(pane)));

        runs.Runs =
        [
            InProgress("run-1", "done", "in_progress"),
            SessionRowsTests.Run("run-2", Worktree, Noon.AddMinutes(-5), Noon.AddMinutes(-1), status: "in_progress")
        ];

        await Task.Run(signal.Raise, TestContext.Current.CancellationToken);

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(
                ["run-2", "run-1"],
                pane.FindAll("[data-testid='sessions-run-card'] [data-testid='sessions-run']").Select(line => line.GetAttribute("data-run-id")));
            Assert.Equal(
                "Review",
                pane.Find("[data-testid='sessions-run'][data-run-id='run-1'] [data-testid='sessions-stage'][data-stage='Review']").GetAttribute("data-stage"));
            Assert.Equal(
                "In progress",
                pane.Find("[data-testid='sessions-run'][data-run-id='run-1'] [data-testid='sessions-stage'][data-stage='Review'] [data-testid='sessions-stage-took']").TextContent.Trim());
        });

        // No reload: the sessions were read once, at opening, and the pane never
        // showed itself busy for a run read.
        Assert.Equal(1, sessions.Reads);
        Assert.Equal(2, runs.Reads);
        Assert.Null(pane.Find("#sessions-content").GetAttribute("aria-busy"));
    }

    [Fact]
    public async Task A_burst_of_raises_while_a_run_read_is_out_folds_into_one_follow_up_read()
    {
        var sessions = new StubSessionSource([Live]);
        var runs = new ChangingRunSource([InProgress("run-1", "in_progress", "pending")]);
        var signal = new DeliveryRunChangeSignal();
        using var context = Context(sessions, runs, signal);

        var pane = context.Render<SessionsPane>();
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-run']")));
        Assert.Equal(1, runs.Reads);

        var hold = runs.Hold();

        await Task.Run(signal.Raise, TestContext.Current.CancellationToken);
        // Polled rather than WaitForAssertion: a read held open renders nothing, and
        // bUnit re-checks an assertion only on a render.
        await UntilAsync(() => runs.Reads == 2);

        // Five more writes while that read is out — the stage updates of a busy run.
        await Task.Run(() =>
        {
            for (var write = 0; write < 5; write++) signal.Raise();
        }, TestContext.Current.CancellationToken);

        runs.Runs = [InProgress("run-1", "done", "in_progress")];
        runs.Release(hold);

        pane.WaitForAssertion(() =>
            Assert.Equal(
                "In progress",
                pane.Find("[data-testid='sessions-stage'][data-stage='Review'] [data-testid='sessions-stage-took']").TextContent.Trim()));

        // Long enough for any further read the dispatcher had queued to start.
        await Task.Delay(Settle, TestContext.Current.CancellationToken);

        Assert.Equal(3, runs.Reads);
        Assert.Equal(1, sessions.Reads);
    }

    [Fact]
    public async Task A_raise_keeps_the_selected_row_an_unfolded_earlier_run_and_an_open_stage()
    {
        var other = Live with { Id = "a1b2c3d4", Title = "quiet-lake-123456", WorkingFolder = @"D:\Repos\Backlog\.claude\worktrees\quiet-lake-123456", LastActivityAt = Noon.AddMinutes(-1) };
        var earlier = SessionRowsTests.Run("run-1", Worktree, Noon.AddMinutes(-90), Noon.AddMinutes(-40));
        var sessions = new StubSessionSource([other, Live]);
        var runs = new ChangingRunSource([earlier, InProgress("run-2", "in_progress", "pending")]);
        var signal = new DeliveryRunChangeSignal();
        using var context = Context(sessions, runs, signal);

        var pane = context.Render<SessionsPane>();
        var liveKey = "";

        pane.WaitForAssertion(() =>
        {
            var row = pane.FindAll("[data-testid='sessions-row']").Single(row => row.TextContent.Contains("keen-bose-667825", StringComparison.Ordinal));
            liveKey = row.GetAttribute("data-row-key")!;
        });

        // The row with the runs is not the one the pane opens on: pick it.
        pane.Find($"[data-testid='sessions-row'][data-row-key='{liveKey}']").Click();
        pane.WaitForAssertion(() => Assert.Equal(liveKey, pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key")));

        pane.Find("[data-testid='sessions-run'][data-run-id='run-1'] [data-testid='sessions-run-fold']").Click();
        pane.WaitForAssertion(() =>
            Assert.False(pane.Find("[data-testid='sessions-run'][data-run-id='run-1'] [data-testid='sessions-run-panel']").HasAttribute("hidden")));

        pane.Find("[data-testid='sessions-run'][data-run-id='run-2'] [data-testid='sessions-stage'][data-stage='Scope'] [data-testid='sessions-stage-toggle']").Click();
        pane.WaitForAssertion(() => Assert.False(ScopeRegion(pane).HasAttribute("hidden")));

        runs.Runs = [earlier, InProgress("run-2", "done", "in_progress")];
        await Task.Run(signal.Raise, TestContext.Current.CancellationToken);

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(
                "In progress",
                pane.Find("[data-testid='sessions-run'][data-run-id='run-2'] [data-testid='sessions-stage'][data-stage='Review'] [data-testid='sessions-stage-took']").TextContent.Trim());

            Assert.Equal(liveKey, pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key"));
            Assert.False(pane.Find("[data-testid='sessions-run'][data-run-id='run-1'] [data-testid='sessions-run-panel']").HasAttribute("hidden"));
            Assert.False(ScopeRegion(pane).HasAttribute("hidden"));
        });

        Assert.Equal(1, sessions.Reads);
    }

    [Fact]
    public async Task A_raise_after_the_pane_is_gone_reads_nothing()
    {
        var sessions = new StubSessionSource([Live]);
        var runs = new ChangingRunSource([InProgress("run-1", "in_progress", "pending")]);
        var signal = new DeliveryRunChangeSignal();
        using var context = Context(sessions, runs, signal);

        var pane = context.Render<SessionsPane>();
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-run']")));

        await context.DisposeComponentsAsync();

        await Task.Run(signal.Raise, TestContext.Current.CancellationToken);
        await Task.Delay(Settle, TestContext.Current.CancellationToken);

        Assert.Equal(1, runs.Reads);
        Assert.Equal(1, sessions.Reads);
    }

    [Fact]
    public void A_pane_whose_host_composed_no_signal_reads_once_as_before()
    {
        var sessions = new StubSessionSource([Live]);
        var runs = new ChangingRunSource([InProgress("run-1", "in_progress", "pending")]);
        using var context = Context(sessions, runs, signal: null);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-run']")));
        Assert.Equal(1, runs.Reads);
    }

    /// <summary>
    /// A reader who never clicked a row is looking at the first one drawn. A run
    /// started elsewhere lands a newer row above it; the panel stays on the row the
    /// reader was looking at, and the stage open in its card stays open.
    /// </summary>
    [Fact]
    public async Task A_raise_that_brings_a_new_row_to_the_top_leaves_an_unclicked_panel_on_its_row()
    {
        const string ElsewhereFolder = @"D:\Repos\Backlog\.claude\worktrees\quiet-lake-123456";
        var elsewhere = DeliveryRunWorktrees.KeyOf(ElsewhereFolder)!;

        // Finished, so the rows sort by when they were last active alone: a live
        // session keeps the top of the list whatever a run does.
        var sessions = new StubSessionSource([Live with { State = AgentSessionState.Finished }]);
        var runs = new ChangingRunSource([InProgress("run-1", "in_progress", "pending")]);
        var signal = new DeliveryRunChangeSignal();
        using var context = Context(sessions, runs, signal);

        var pane = context.Render<SessionsPane>();
        var liveKey = "";

        // All, because Live hides a row with no session: a run-only row is finished.
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-view-all']")));
        pane.Find("[data-testid='sessions-view-all']").Click();
        pane.WaitForAssertion(() =>
            Assert.Equal("true", pane.Find("[data-testid='sessions-view-all']").GetAttribute("aria-pressed")));

        pane.WaitForAssertion(() =>
        {
            liveKey = Assert.Single(pane.FindAll("[data-testid='sessions-row']")).GetAttribute("data-row-key")!;
            Assert.Equal(liveKey, pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key"));
        });

        pane.Find("[data-testid='sessions-stage'][data-stage='Scope'] [data-testid='sessions-stage-toggle']").Click();
        pane.WaitForAssertion(() => Assert.False(pane.Find("[data-testid='sessions-stage'][data-stage='Scope'] .fold__region").HasAttribute("hidden")));

        runs.Runs =
        [
            InProgress("run-1", "in_progress", "pending"),
            SessionRowsTests.Run("run-9", elsewhere, Noon.AddMinutes(-2), Noon.AddMinutes(-1), status: "in_progress")
        ];
        await Task.Run(signal.Raise, TestContext.Current.CancellationToken);

        pane.WaitForAssertion(() =>
        {
            // The new row is drawn above the one on screen...
            var rows = pane.FindAll("[data-testid='sessions-row']");
            Assert.Equal(2, rows.Count);
            Assert.NotEqual(liveKey, rows[0].GetAttribute("data-row-key"));

            // ...and the panel, its highlight and the open stage stay where they were.
            Assert.Equal(liveKey, pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key"));
            Assert.Contains("sessions-row--selected", pane.Find($"[data-testid='sessions-row'][data-row-key='{liveKey}']").ClassList);
            Assert.False(pane.Find("[data-testid='sessions-stage'][data-stage='Scope'] .fold__region").HasAttribute("hidden"));
        });
    }

    /// <summary>
    /// The row a run read held still was never pressed: a Refresh afterwards hands the
    /// panel back to the first row drawn, as before any run was written.
    /// </summary>
    [Fact]
    public async Task A_refresh_after_a_raise_returns_an_unpressed_panel_to_the_first_row()
    {
        var (pane, sessions, liveKey) = await PinnedAboveAsync(press: false);

        pane.Find("[data-testid='sessions-refresh']").Click();

        await UntilAsync(() => sessions.Reads == 2);
        pane.WaitForAssertion(() =>
        {
            var first = pane.FindAll("[data-testid='sessions-row']")[0].GetAttribute("data-row-key");
            Assert.NotEqual(liveKey, first);
            Assert.Equal(first, pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key"));
        });
    }

    /// <summary>
    /// A run written while a Refresh is out is read after it, and holds still the row
    /// that Refresh drew first, not the one on screen before it: an unpressed panel
    /// ends on the first row of the new reading.
    /// </summary>
    [Fact]
    public async Task A_raise_during_a_refresh_leaves_an_unpressed_panel_on_the_refreshs_first_row()
    {
        var elsewhere = DeliveryRunWorktrees.KeyOf(@"D:\Repos\Backlog\.claude\worktrees\quiet-lake-123456")!;
        var sessions = new StubSessionSource([Live with { State = AgentSessionState.Finished }]);
        var runs = new ChangingRunSource([InProgress("run-1", "in_progress", "pending")]);
        var signal = new DeliveryRunChangeSignal();
        using var context = Context(sessions, runs, signal);

        var pane = context.Render<SessionsPane>();
        var liveKey = "";

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-view-all']")));
        pane.Find("[data-testid='sessions-view-all']").Click();
        pane.WaitForAssertion(() =>
        {
            liveKey = Assert.Single(pane.FindAll("[data-testid='sessions-row']")).GetAttribute("data-row-key")!;
            Assert.Equal(liveKey, pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key"));
        });

        // The Refresh's reading has a newer run-only row on top; its run read is held.
        runs.Runs =
        [
            InProgress("run-1", "in_progress", "pending"),
            SessionRowsTests.Run("run-9", elsewhere, Noon.AddMinutes(-2), Noon.AddMinutes(-1), status: "in_progress")
        ];
        var hold = runs.Hold();
        pane.Find("[data-testid='sessions-refresh']").Click();
        await UntilAsync(() => runs.Reads == 2);

        await Task.Run(signal.Raise, TestContext.Current.CancellationToken);
        runs.Release(hold);

        await UntilAsync(() => runs.Reads == 3);
        pane.WaitForAssertion(() =>
        {
            var rows = pane.FindAll("[data-testid='sessions-row']");
            Assert.Equal(2, rows.Count);
            var first = rows[0].GetAttribute("data-row-key");
            Assert.NotEqual(liveKey, first);
            Assert.Equal(first, pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key"));
            Assert.Null(pane.Find("#sessions-content").GetAttribute("aria-busy"));
        });
        Assert.Equal(2, sessions.Reads);
    }

    /// <summary>A row the reader pressed stays the panel's through a Refresh.</summary>
    [Fact]
    public async Task A_refresh_after_a_raise_keeps_a_pressed_row()
    {
        var (pane, sessions, liveKey) = await PinnedAboveAsync(press: true);

        pane.Find("[data-testid='sessions-refresh']").Click();

        await UntilAsync(() => sessions.Reads == 2);
        pane.WaitForAssertion(() => Assert.Null(pane.Find("#sessions-content").GetAttribute("aria-busy")));
        Assert.Equal(liveKey, pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key"));
    }

    /// <summary>A finished session on screen, pressed or not, then a raise that lands a
    /// run-only row above it; the panel is still on the session.</summary>
    private async Task<(IRenderedComponent<SessionsPane> Pane, StubSessionSource Sessions, string LiveKey)> PinnedAboveAsync(bool press)
    {
        var elsewhere = DeliveryRunWorktrees.KeyOf(@"D:\Repos\Backlog\.claude\worktrees\quiet-lake-123456")!;
        var sessions = new StubSessionSource([Live with { State = AgentSessionState.Finished }]);
        var runs = new ChangingRunSource([InProgress("run-1", "in_progress", "pending")]);
        var signal = new DeliveryRunChangeSignal();
        var context = Context(sessions, runs, signal);
        _contexts.Add(context);

        var pane = context.Render<SessionsPane>();
        var liveKey = "";

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-view-all']")));
        pane.Find("[data-testid='sessions-view-all']").Click();
        pane.WaitForAssertion(() =>
        {
            liveKey = Assert.Single(pane.FindAll("[data-testid='sessions-row']")).GetAttribute("data-row-key")!;
            Assert.Equal(liveKey, pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key"));
        });

        if (press)
        {
            pane.Find($"[data-testid='sessions-row'][data-row-key='{liveKey}']").Click();
            pane.WaitForAssertion(() => Assert.Equal(liveKey, pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key")));
        }

        runs.Runs =
        [
            InProgress("run-1", "in_progress", "pending"),
            SessionRowsTests.Run("run-9", elsewhere, Noon.AddMinutes(-2), Noon.AddMinutes(-1), status: "in_progress")
        ];
        await Task.Run(signal.Raise, TestContext.Current.CancellationToken);

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(2, pane.FindAll("[data-testid='sessions-row']").Count);
            Assert.Equal(liveKey, pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key"));
        });

        return (pane, sessions, liveKey);
    }

    private readonly List<BunitContext> _contexts = [];

    public void Dispose()
    {
        foreach (var context in _contexts) context.Dispose();
    }

    /// <summary>Refresh pressed while a runs-only read is out is not lost: it shows
    /// busy at once, and the read it queues is a full one, sessions included — one
    /// read at a time throughout.</summary>
    [Fact]
    public async Task Refresh_pressed_during_a_run_read_becomes_a_full_read_after_it()
    {
        var sessions = new StubSessionSource([Live]);
        var runs = new ChangingRunSource([InProgress("run-1", "in_progress", "pending")]);
        var signal = new DeliveryRunChangeSignal();
        using var context = Context(sessions, runs, signal);

        var pane = context.Render<SessionsPane>();
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-run']")));

        var hold = runs.Hold();
        await Task.Run(signal.Raise, TestContext.Current.CancellationToken);
        await UntilAsync(() => runs.Reads == 2);

        pane.Find("[data-testid='sessions-refresh']").Click();

        pane.WaitForAssertion(() => Assert.Equal("true", pane.Find("#sessions-content").GetAttribute("aria-busy")));
        Assert.Equal(1, sessions.Reads);

        runs.Release(hold);

        await UntilAsync(() => sessions.Reads == 2);
        pane.WaitForAssertion(() => Assert.Null(pane.Find("#sessions-content").GetAttribute("aria-busy")));
        await Task.Delay(Settle, TestContext.Current.CancellationToken);

        Assert.Equal(2, sessions.Reads);
        Assert.Equal(3, runs.Reads);
        Assert.Equal(1, runs.MostAtOnce);
    }

    /// <summary>Raises during a Refresh start no read beside it: they fold into one
    /// runs-only follow-up once it lands, and the sessions are read once for it.</summary>
    [Fact]
    public async Task Raises_during_a_refresh_fold_into_one_run_read_after_it()
    {
        var sessions = new StubSessionSource([Live]);
        var runs = new ChangingRunSource([InProgress("run-1", "in_progress", "pending")]);
        var signal = new DeliveryRunChangeSignal();
        using var context = Context(sessions, runs, signal);

        var pane = context.Render<SessionsPane>();
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-run']")));

        var hold = runs.Hold();
        pane.Find("[data-testid='sessions-refresh']").Click();
        await UntilAsync(() => runs.Reads == 2);
        Assert.Equal(2, sessions.Reads);

        await Task.Run(() =>
        {
            for (var write = 0; write < 3; write++) signal.Raise();
        }, TestContext.Current.CancellationToken);

        // Long enough for a read started beside the Refresh to show; none should.
        await Task.Delay(Settle, TestContext.Current.CancellationToken);
        Assert.Equal(2, runs.Reads);

        runs.Runs = [InProgress("run-1", "done", "in_progress")];
        runs.Release(hold);

        await UntilAsync(() => runs.Reads == 3);
        pane.WaitForAssertion(() =>
            Assert.Equal(
                "In progress",
                pane.Find("[data-testid='sessions-stage'][data-stage='Review'] [data-testid='sessions-stage-took']").TextContent.Trim()));
        await Task.Delay(Settle, TestContext.Current.CancellationToken);

        Assert.Equal(3, runs.Reads);
        Assert.Equal(2, sessions.Reads);
        Assert.Equal(1, runs.MostAtOnce);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold within the timeout.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static string Summary(IRenderedComponent<SessionsPane> pane) =>
        pane.Find("[data-testid='sessions-row'] .stage-strip__summary").TextContent.Trim();

    private static AngleSharp.Dom.IElement ScopeRegion(IRenderedComponent<SessionsPane> pane) =>
        pane.Find("[data-testid='sessions-run'][data-run-id='run-2'] [data-testid='sessions-stage'][data-stage='Scope'] .fold__region");

    private static DeliveryRun InProgress(string id, string implement, string review) =>
        SessionRowsTests.Run(id, Worktree, Noon.AddMinutes(-30), Noon.AddMinutes(-10), status: "in_progress") with
        {
            Stages =
            [
                new DeliveryRunStage("Scope", "done", 1_000, 1),
                new DeliveryRunStage("Implement", implement, implement == "done" ? 2_000 : null, implement == "done" ? 1 : 0),
                new DeliveryRunStage("Review", review, null, 0)
            ]
        };

    private static BunitContext Context(StubSessionSource sessions, ChangingRunSource runs, IDeliveryRunChangeSignal? signal)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IAgentSessionSource>(sessions);
        context.Services.AddSingleton<IDeliveryRunSource>(runs);
        if (signal is not null) context.Services.AddSingleton(signal);

        return context;
    }

    private static readonly AgentSession Live = new(
        Id: "5905cf2d",
        Kind: AgentSessionKind.Claude,
        EnvironmentId: "tower",
        Environment: "DEV-TOWER",
        Title: "keen-bose-667825",
        WorkingFolder: Folder,
        Repository: null,
        Branch: "claude/desktop-session-area",
        StartedAt: Noon.AddHours(-2),
        LastActivityAt: Noon.AddMinutes(-3),
        State: AgentSessionState.Running,
        TurnCount: 12,
        Origin: AgentSessionOrigin.Local);

    /// <summary>A run source whose answer the test changes between reads, which
    /// counts them, and which can hold a read open until released.</summary>
    private sealed class ChangingRunSource(IReadOnlyList<DeliveryRun> runs) : IDeliveryRunSource
    {
        private int _reads;
        private volatile IReadOnlyList<DeliveryRun> _runs = runs;
        private volatile TaskCompletionSource? _hold;

        public int Reads => Volatile.Read(ref _reads);

        public IReadOnlyList<DeliveryRun> Runs
        {
            get => _runs;
            set => _runs = value;
        }

        public TaskCompletionSource Hold() =>
            _hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release(TaskCompletionSource hold)
        {
            _hold = null;
            hold.SetResult();
        }

        private int _atOnce;
        private int _mostAtOnce;

        /// <summary>The most reads that were out together.</summary>
        public int MostAtOnce => Volatile.Read(ref _mostAtOnce);

        public async Task<DeliveryRunCatalog> GetRunsAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _reads);
            var atOnce = Interlocked.Increment(ref _atOnce);

            int most;
            while ((most = Volatile.Read(ref _mostAtOnce)) < atOnce
                   && Interlocked.CompareExchange(ref _mostAtOnce, atOnce, most) != most)
            {
            }

            try
            {
                if (_hold is { } hold) await hold.Task;

                return new DeliveryRunCatalog(_runs, []);
            }
            finally
            {
                Interlocked.Decrement(ref _atOnce);
            }
        }
    }
}
