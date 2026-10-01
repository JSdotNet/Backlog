using Backlog.Desktop.UI.Inbox;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Triage from the keyboard: the Inbox pane's single-letter shortcuts and the
/// one-item-at-a-time triage mode.
///
/// <para>A key reaches the pane through components.js, which decides at the
/// keydown whether it is a shortcut or typing; that half is checked here as
/// source, because no test in this repository runs the browser's script. What
/// the pane does with a key it is handed is called directly through the same
/// <c>OnShortcutAsync</c> the script invokes.</para>
/// </summary>
public sealed class InboxKeyboardTriageTests
{
    // --- Keys over the rows -------------------------------------------------

    [Fact]
    public async Task J_and_k_walk_the_rows_and_stop_at_either_end()
    {
        using var harness = Harness.Create();
        var items = SeedNewestFirst(harness, "First", "Second", "Third");
        var pane = await harness.RenderAsync();

        await PressAsync(pane, "j");
        Assert.Equal(items[0].Id, harness.State.SelectedItemId);

        await PressAsync(pane, "j");
        await PressAsync(pane, "j");
        await PressAsync(pane, "j");
        Assert.Equal(items[2].Id, harness.State.SelectedItemId);

        await PressAsync(pane, "k");
        Assert.Equal(items[1].Id, harness.State.SelectedItemId);

        // The row moved to is the one focused, so the list scrolls with the keys.
        Assert.Contains(
            harness.Context.JSInterop.Invocations["backlogFocus"],
            call => Equals(call.Arguments[0], InboxItemList.RowId(items[1].Id)));
    }

    [Fact]
    public async Task J_after_an_archive_reads_the_row_that_slid_into_its_place()
    {
        using var harness = Harness.Create();
        var items = SeedNewestFirst(harness, "First", "Second", "Third");
        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, items[1].Id);

        await PressAsync(pane, "a");
        pane.WaitForAssertion(() => Assert.Equal(InboxStatus.Archived, harness.Inbox.Find(items[1].Id)!.Status));

        await PressAsync(pane, "j");
        Assert.Equal(items[2].Id, harness.State.SelectedItemId);
    }

    /// <summary>On the Sources tab the rows are not on screen, so a triage
    /// key would decide an item the reader cannot see. Only ? still answers.</summary>
    [Fact]
    public async Task Triage_keys_do_nothing_while_the_sources_tab_is_shown()
    {
        using var harness = Harness.Create();
        var items = SeedNewestFirst(harness, "Keep me");
        var pane = await harness.RenderAsync(withSources: true);
        await harness.SelectAsync(pane, items[0].Id);

        await pane.InvokeAsync(() => harness.State.ShowSources(true));
        await PressAsync(pane, "a");
        await PressAsync(pane, "j");

        Assert.Equal(InboxStatus.Unprocessed, harness.Inbox.Find(items[0].Id)!.Status);
        Assert.Equal(items[0].Id, harness.State.SelectedItemId);

        await PressAsync(pane, "?");
        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-shortcuts-list']"));
    }

    [Fact]
    public async Task A_and_r_decide_the_chosen_item_the_way_its_buttons_do()
    {
        using var harness = Harness.Create();
        var items = SeedNewestFirst(harness, "Keep", "Route me");
        var pane = await harness.RenderAsync();

        await harness.SelectAsync(pane, items[0].Id);
        await PressAsync(pane, "a");
        pane.WaitForAssertion(() => Assert.Equal(InboxStatus.Archived, harness.Inbox.Find(items[0].Id)!.Status));

        await harness.SelectAsync(pane, items[1].Id);
        await PressAsync(pane, "r");
        pane.WaitForAssertion(() => Assert.Equal(InboxStatus.Triaged, harness.Inbox.Find(items[1].Id)!.Status));
    }

    [Fact]
    public async Task A_key_does_not_offer_an_act_the_header_withholds()
    {
        using var harness = Harness.Create();
        var routed = harness.Inbox.Seed("Already routed");
        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, routed.Id);
        await PressAsync(pane, "r");
        pane.WaitForAssertion(() => Assert.Equal(InboxStatus.Triaged, harness.Inbox.Find(routed.Id)!.Status));

        await PressAsync(pane, "a");
        await PressAsync(pane, "d");

        Assert.Equal(InboxStatus.Triaged, harness.Inbox.Find(routed.Id)!.Status);
        Assert.Empty(pane.FindAll("[data-testid='inbox-defer-dialog']"));
    }

    [Fact]
    public async Task L_opens_move_to_list_and_d_offers_review_dates_that_defer_the_item()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Later");
        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        await PressAsync(pane, "l");
        Assert.NotNull(pane.Find("[data-testid='inbox-move-list-dialog']"));
        await pane.Find("[data-testid='inbox-move-list-dialog']").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        await PressAsync(pane, "d");
        var choices = pane.FindAll("[data-testid='inbox-defer-choices'] [role='menuitem']");
        Assert.Equal(4, choices.Count);
        Assert.StartsWith("Tomorrow", choices[0].TextContent.Trim(), StringComparison.Ordinal);

        await choices[0].ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            var deferred = harness.Inbox.Find(item.Id)!;
            Assert.Equal(InboxStatus.Deferred, deferred.Status);
            Assert.Equal(harness.State.Today.AddDays(1), deferred.DeferredUntil);
        });
    }

    [Fact]
    public async Task T_focuses_the_tag_field_and_x_picks_the_chosen_item()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Tag me");
        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        await PressAsync(pane, "t");
        Assert.Contains(
            harness.Context.JSInterop.Invocations["backlogFocus"],
            call => Equals(call.Arguments[0], InboxItemDetail.TagsInputId));
        Assert.Equal(InboxItemDetail.TagsInputId, pane.Find("[data-testid='inbox-detail-tags'] input").Id);

        await PressAsync(pane, "x");
        Assert.True(harness.State.SelectionMode);
        Assert.True(harness.State.IsPicked(item.Id));

        await PressAsync(pane, "x");
        Assert.False(harness.State.IsPicked(item.Id));
    }

    // --- Finding them -------------------------------------------------------

    [Fact]
    public async Task The_header_and_the_question_mark_open_the_list_of_every_key()
    {
        using var harness = Harness.Create();
        var pane = await harness.RenderAsync();

        await pane.Find("[data-testid='inbox-pane-shortcuts']").ClickAsync(new());
        var keys = pane.FindAll("[data-testid='inbox-shortcuts-list'] kbd").Select(key => key.TextContent).ToList();
        Assert.Equal(["j", "k", "a", "d", "l", "r", "t", "x", "Esc", "?"], keys);

        await pane.Find("[data-testid='inbox-shortcuts-dialog']").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(pane.FindAll("[data-testid='inbox-shortcuts-dialog']"));

        await PressAsync(pane, "?");
        Assert.NotNull(pane.Find("[data-testid='inbox-shortcuts-dialog']"));
    }

    // --- Triage mode --------------------------------------------------------

    [Fact]
    public async Task Triage_shows_one_item_counted_and_moves_on_after_each_decision()
    {
        using var harness = Harness.Create();
        var items = SeedNewestFirst(harness, "One", "Two", "Three");
        var pane = await harness.RenderAsync();

        await pane.Find("[data-testid='inbox-triage-start']").ClickAsync(new());

        Assert.Empty(pane.FindAll("[data-testid='inbox-pane-body']"));
        Assert.Equal("1 of 3", Counter(pane));
        Assert.Equal("One", DetailTitle(pane));

        // Archived: it leaves the rows, and the next one takes its place.
        await PressAsync(pane, "a");
        pane.WaitForAssertion(() =>
        {
            Assert.Equal("1 of 2", Counter(pane));
            Assert.Equal("Two", DetailTitle(pane));
        });

        // Routed: it stays in the rows, and the session still moves on.
        await PressAsync(pane, "r");
        pane.WaitForAssertion(() =>
        {
            Assert.Equal("2 of 2", Counter(pane));
            Assert.Equal("Three", DetailTitle(pane));
        });

        // A button is a decision too.
        await pane.Find("[data-testid='inbox-archive']").ClickAsync(new());
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='inbox-triage-done']")));
        Assert.Equal(InboxStatus.Triaged, harness.Inbox.Find(items[1].Id)!.Status);
    }

    [Fact]
    public async Task An_archive_from_the_middle_counts_the_rows_that_are_left()
    {
        using var harness = Harness.Create();
        SeedNewestFirst(harness, "One", "Two", "Three", "Four");
        var pane = await harness.RenderAsync();
        await pane.Find("[data-testid='inbox-triage-start']").ClickAsync(new());

        await PressAsync(pane, "j");
        Assert.Equal("2 of 4", Counter(pane));

        await PressAsync(pane, "a");
        pane.WaitForAssertion(() =>
        {
            Assert.Equal("Three", DetailTitle(pane));
            Assert.Equal("2 of 3", Counter(pane));
        });
    }

    [Fact]
    public async Task Past_the_last_row_triage_returns_to_the_item_skipped_on_the_way()
    {
        using var harness = Harness.Create();
        var items = SeedNewestFirst(harness, "Skipped", "Decided");
        var pane = await harness.RenderAsync();
        await pane.Find("[data-testid='inbox-triage-start']").ClickAsync(new());

        await PressAsync(pane, "j");
        await PressAsync(pane, "a");

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(items[0].Id, harness.State.SelectedItemId);
            Assert.Equal("1 of 1", Counter(pane));
        });
    }

    [Fact]
    public async Task Escape_leaves_triage_on_the_row_the_reader_stopped_at()
    {
        using var harness = Harness.Create();
        var items = SeedNewestFirst(harness, "One", "Two", "Three");
        var pane = await harness.RenderAsync();
        await pane.Find("[data-testid='inbox-triage-start']").ClickAsync(new());
        await PressAsync(pane, "j");

        await PressAsync(pane, "Escape");

        Assert.False(harness.State.TriageMode);
        Assert.NotNull(pane.Find("[data-testid='inbox-pane-body']"));
        Assert.Equal("true", pane.Find($"[data-testid='inbox-item-{items[1].Id:D}']").GetAttribute("aria-pressed"));
        Assert.Contains(
            harness.Context.JSInterop.Invocations["backlogFocus"],
            call => Equals(call.Arguments[0], InboxItemList.RowId(items[1].Id)));
    }

    [Fact]
    public async Task Escape_outside_triage_is_left_to_whatever_else_hears_it()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Stay");
        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        await PressAsync(pane, "Escape");

        Assert.Equal(item.Id, harness.State.SelectedItemId);
        Assert.NotNull(pane.Find("[data-testid='inbox-pane-body']"));
    }

    // --- Typing is typing ---------------------------------------------------

    [Fact]
    public async Task The_pane_registers_for_shortcuts_and_no_inbox_markup_arms_a_server_side_prevent_default()
    {
        using var harness = Harness.Create();
        harness.Inbox.Seed("Anything");
        var pane = await harness.RenderAsync();

        Assert.Contains(
            harness.Context.JSInterop.Invocations["backlogPaneShortcuts.register"],
            call => Equals(call.Arguments[0], "inbox-pane"));

        // The library's splitter holds a constant preventDefault for its own
        // arrows; what must not exist is one the Inbox arms for its letters.
        var inboxUi = RepositoryRoot.File("src", "Modules", "Inbox", "Backlog.Modules.Inbox.UI", "InboxPane.razor");
        foreach (var razor in Directory.GetFiles(Path.GetDirectoryName(inboxUi)!, "*.razor"))
        {
            Assert.DoesNotContain(":preventDefault", File.ReadAllText(razor), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Components_js_decides_at_the_keydown_that_a_key_in_a_field_or_with_a_modifier_types()
    {
        var script = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"));
        var start = script.IndexOf("window.backlogPaneShortcuts", StringComparison.Ordinal);
        Assert.True(start >= 0, "components.js no longer registers pane shortcuts.");

        var listener = script[script.LastIndexOf("const TYPING_TARGETS", start, StringComparison.Ordinal)..];
        listener = listener[..listener.IndexOf("});", listener.IndexOf("'OnShortcutAsync'", StringComparison.Ordinal), StringComparison.Ordinal)];

        Assert.Contains("input, textarea, select, [contenteditable]", listener, StringComparison.Ordinal);
        Assert.Contains("[role=\"dialog\"]", listener, StringComparison.Ordinal);
        Assert.Contains("event.ctrlKey || event.altKey || event.metaKey || event.isComposing", listener, StringComparison.Ordinal);
        Assert.Contains("[aria-modal=\"true\"]", listener, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault()", listener, StringComparison.Ordinal);
    }

    // --- Helpers ------------------------------------------------------------

    private static Task PressAsync(IRenderedComponent<InboxPane> pane, string key) =>
        pane.InvokeAsync(() => pane.Instance.OnShortcutAsync(key));

    private static string Counter(IRenderedComponent<InboxPane> pane) =>
        string.Join(' ', pane.Find("[data-testid='inbox-triage-pager'] .record-pager__count").TextContent
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string DetailTitle(IRenderedComponent<InboxPane> pane) =>
        pane.Find("#inbox-detail-title").TextContent.Trim();

    private static IReadOnlyList<InboxItemDto> SeedNewestFirst(Harness harness, params string[] titles) =>
        [.. titles.Select((title, index) => harness.Inbox.Seed(title, capturedAt: harness.Inbox.Now.AddMinutes(titles.Length - index)))];

    private sealed class Harness : IDisposable
    {
        private Harness(BunitContext context, FakeInboxItems inbox)
        {
            Context = context;
            Inbox = inbox;
        }

        public BunitContext Context { get; }

        public FakeInboxItems Inbox { get; }

        public InboxDesktopState State => Context.Services.GetRequiredService<InboxDesktopState>();

        public static Harness Create()
        {
            var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddSingleton(new Backlog.Infrastructure.GitHub.GitHubSettingsStore(
                Path.Combine(Path.GetTempPath(), "backlog-inbox-keys-tests", Guid.NewGuid().ToString("n"), "github.json")));
            TasksTestHost.AddToastChannel(context.Services);
            var inbox = InboxTestHost.AddInboxState(context.Services);

            return new Harness(context, inbox);
        }

        public async Task<IRenderedComponent<InboxPane>> RenderAsync(bool withSources = false)
        {
            var pane = withSources
                ? Context.Render<InboxPane>(parameters => parameters
                    .Add(p => p.Sources, (RenderFragment)(builder => builder.AddContent(0, "switches"))))
                : Context.Render<InboxPane>();
            await pane.InvokeAsync(State.InitializeAsync);
            return pane;
        }

        public Task SelectAsync(IRenderedComponent<InboxPane> pane, Guid id) =>
            pane.Find($"[data-testid='inbox-item-{id:D}']").ClickAsync(new());

        public void Dispose() => Context.Dispose();
    }
}
