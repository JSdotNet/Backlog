using Backlog.Desktop.UI.Inbox;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Inbox.Abstractions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The inbox-zero screen (features.md#inbox-zero): once the unfiled Inbox has
/// nothing left waiting and the session decided something, the rows and the
/// detail give way to the session's summary — the count per decision kind,
/// what is coming back, and the way to the busiest list and to Deferred. With
/// no decision this session the first-use empty state stays.
/// </summary>
public sealed class InboxZeroTests : IDisposable
{
    /// <summary>A Thursday, so the weekday labels have a week to fall in.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.Date);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-inbox-zero-tests", Guid.NewGuid().ToString("n"));
    private readonly BunitContext _context = new();
    private readonly FakeInboxItems _inbox;

    public InboxZeroTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton(new GitHubSettingsStore(Path.Combine(_root, "github.json")));
        var clock = new FakeTimeProvider(Now);
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        _context.Services.AddSingleton<TimeProvider>(clock);
        TasksTestHost.AddToastChannel(_context.Services);
        _inbox = InboxTestHost.AddInboxState(_context.Services);
        _inbox.Now = Now;
    }

    private InboxDesktopState State => _context.Services.GetRequiredService<InboxDesktopState>();

    [Fact]
    public async Task With_nothing_waiting_after_a_session_the_summary_replaces_the_rows_and_the_detail()
    {
        var reading = _inbox.SeedList("Reading");
        var errands = _inbox.SeedList("Errands");
        _inbox.Seed("Long read one", listId: reading.Id);
        _inbox.Seed("Long read two", listId: reading.Id);
        _inbox.Seed("Buy stamps", listId: errands.Id);

        // Already deferred before the session: six dated, one undated.
        var later = _inbox.Seed("In three weeks", status: InboxStatus.Deferred, deferredUntil: Today.AddDays(21));
        var monday = _inbox.Seed("On Monday", status: InboxStatus.Deferred, deferredUntil: Today.AddDays(4));
        var nextWeek = _inbox.Seed("Next week", status: InboxStatus.Deferred, deferredUntil: Today.AddDays(9));
        var month = _inbox.Seed("In a month", status: InboxStatus.Deferred, deferredUntil: Today.AddDays(30));
        var sixth = _inbox.Seed("Two months out", status: InboxStatus.Deferred, deferredUntil: Today.AddDays(60));
        _inbox.Seed("No date", status: InboxStatus.Deferred);

        var routed = _inbox.Seed("Fix the sync bug");
        var archived = _inbox.Seed("Old newsletter");
        var deferred = _inbox.Seed("Ask about the emulator");
        var filed = _inbox.Seed("Another long read");
        var merged = _inbox.Seed("Same as the task");

        var pane = _context.Render<InboxPane>();
        await pane.InvokeAsync(State.InitializeAsync);
        Assert.Empty(pane.FindAll("[data-testid='inbox-zero']"));

        await pane.InvokeAsync(async () =>
        {
            State.SelectItem(routed.Id);
            await State.RouteToBacklogAsync();
            State.SelectItem(archived.Id);
            await State.ArchiveAsync();
            State.SelectItem(deferred.Id);
            await State.DeferAsync(Today.AddDays(1));
            State.SelectItem(filed.Id);
            await State.MoveToListAsync(reading.Id);
            State.SelectItem(merged.Id);
            await State.MergeIntoTaskAsync(merged.Id, Guid.NewGuid());
        });

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='inbox-zero']")));

        // The summary stands where the rows and the detail stood; the side menu stays.
        Assert.Empty(pane.FindAll("[data-testid='inbox-pane-split']"));
        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-nav-inbox']"));

        Assert.Equal("Inbox is clear", pane.Find("[data-testid='inbox-zero-title']").TextContent.Trim());
        Assert.Contains("You decided on 5 items in this session.", pane.Find("[data-testid='inbox-zero-summary']").TextContent, StringComparison.Ordinal);

        Assert.Equal("What this session decided", pane.Find("[data-testid='inbox-zero-stats']").GetAttribute("aria-label"));
        foreach (var (slug, label) in new[]
                 {
                     ("backlog", "moved to backlog"),
                     ("lists", "filed in lists"),
                     ("deferred", "deferred"),
                     ("archived", "archived"),
                     ("merged", "merged"),
                 })
        {
            var stat = pane.Find($"[data-testid='inbox-zero-stat-{slug}']");
            Assert.Equal("1", stat.QuerySelector(".inbox-zero__stat__value")!.TextContent.Trim());
            Assert.Equal(label, stat.QuerySelector(".inbox-zero__stat__label")!.TextContent.Trim());
        }

        // Coming back: dated deferrals only, soonest first, five at most.
        var rows = pane.FindAll("[data-testid='inbox-zero-coming-back'] li");
        Assert.Equal(
            [
                ("Ask about the emulator", "tomorrow"),
                ("On Monday", "Monday"),
                ("Next week", "17 Oct"),
                ("In three weeks", "29 Oct"),
                ("In a month", "7 Nov"),
            ],
            rows.Select(row => (
                row.QuerySelector(".inbox-zero__back-name")!.TextContent.Trim(),
                row.QuerySelector(".inbox-zero__back-when")!.TextContent.Trim())));
        Assert.Empty(pane.FindAll($"[data-testid='inbox-zero-back-{sixth.Id:D}']"));

        // The busiest list — Reading, now three — and Deferred.
        var openList = pane.Find("[data-testid='inbox-zero-open-list']");
        Assert.Equal("Open Reading · 3", openList.TextContent.Trim());
        await openList.ClickAsync(new());
        Assert.Equal(InboxDesktopState.ListNavId(reading.Id), State.SelectedSliceId);
        Assert.Empty(pane.FindAll("[data-testid='inbox-zero']"));

        await pane.InvokeAsync(() => State.SelectSlice(InboxDesktopState.InboxSliceId));
        await pane.Find("[data-testid='inbox-zero-open-deferred']").ClickAsync(new());
        Assert.Equal(InboxDesktopState.DeferredSliceId, State.SelectedSliceId);
    }

    [Fact]
    public async Task A_kind_with_no_decisions_is_left_out_and_no_list_with_waiting_items_leaves_out_its_link()
    {
        var item = _inbox.Seed("Old newsletter");

        var pane = _context.Render<InboxPane>();
        await pane.InvokeAsync(State.InitializeAsync);
        await pane.InvokeAsync(async () =>
        {
            State.SelectItem(item.Id);
            await State.ArchiveAsync();
        });

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='inbox-zero']")));

        Assert.Equal(["inbox-zero-stat-archived"], pane.FindAll("[data-testid^='inbox-zero-stat-']").Select(stat => stat.GetAttribute("data-testid")));
        Assert.Contains("You decided on 1 item in this session.", pane.Find("[data-testid='inbox-zero-summary']").TextContent, StringComparison.Ordinal);
        Assert.Empty(pane.FindAll("[data-testid='inbox-zero-coming-back']"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-zero-open-list']"));
        Assert.NotNull(pane.Find("[data-testid='inbox-zero-open-deferred']"));
    }

    [Fact]
    public async Task With_no_decisions_this_session_the_first_use_empty_state_stays()
    {
        _inbox.Seed("Deferred before the session", status: InboxStatus.Deferred, deferredUntil: Today.AddDays(2));

        var pane = _context.Render<InboxPane>();
        await pane.InvokeAsync(State.InitializeAsync);

        Assert.Empty(pane.FindAll("[data-testid='inbox-zero']"));
        Assert.Equal("first-use", pane.Find("[data-testid='inbox-pane-empty']").GetAttribute("data-inbox-empty"));
    }

    [Fact]
    public async Task Undoing_the_only_decision_brings_the_rows_back()
    {
        var item = _inbox.Seed("Old newsletter");

        var pane = _context.Render<InboxPane>();
        await pane.InvokeAsync(State.InitializeAsync);
        await pane.InvokeAsync(async () =>
        {
            State.SelectItem(item.Id);
            await State.ArchiveAsync();
        });
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='inbox-zero']")));

        // An item key has no detail to act on here; U still reaches the history.
        await pane.InvokeAsync(() => pane.Instance.OnShortcutAsync("a"));
        Assert.NotNull(pane.Find("[data-testid='inbox-zero']"));

        await pane.InvokeAsync(() => pane.Instance.OnShortcutAsync("u"));

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find($"[data-testid='inbox-item-{item.Id:D}']")));
        Assert.Empty(pane.FindAll("[data-testid='inbox-zero']"));
    }

    [Fact]
    public async Task Another_slice_never_shows_the_summary()
    {
        var list = _inbox.SeedList("Errands");
        var item = _inbox.Seed("Old newsletter");

        var pane = _context.Render<InboxPane>();
        await pane.InvokeAsync(State.InitializeAsync);
        await pane.InvokeAsync(async () =>
        {
            State.SelectItem(item.Id);
            await State.ArchiveAsync();
            State.SelectSlice(InboxDesktopState.ListNavId(list.Id));
        });

        Assert.Empty(pane.FindAll("[data-testid='inbox-zero']"));
        Assert.Equal("list", pane.Find("[data-testid='inbox-pane-empty']").GetAttribute("data-inbox-empty"));
    }

    public void Dispose()
    {
        _context.Dispose();

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
