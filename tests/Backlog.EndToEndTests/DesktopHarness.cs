using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Backlog.EndToEndTests;

/// <summary>
/// The desktop, as <c>desktop-web-harness</c> serves it: <c>Backlog.Desktop.UI</c>
/// in a browser.
///
/// <para>Its workspace — the task database and the Inbox — is the per-user
/// <c>Backlog.Debug</c> folder every worktree on the machine shares, so it is
/// never reset here. A scenario marks everything it writes with a token of its
/// own and only ever looks for rows carrying it.</para>
/// </summary>
internal sealed partial class DesktopHarness
{
    public const string Resource = "desktop-web-harness";

    private readonly Uri _baseUrl;

    private DesktopHarness(IPage page, Uri baseUrl)
    {
        Page = page;
        _baseUrl = baseUrl;
    }

    public IPage Page { get; }

    public static Task<DesktopHarness> OpenAsync(IBrowser browser, AspireAppHost appHost, CancellationToken cancellationToken) =>
        OpenAsync(browser, appHost, new ViewportSize { Width = 1440, Height = 900 }, cancellationToken);

    /// <summary>A page at a viewport of the caller's, for a layout that depends on
    /// the window's width.</summary>
    public static async Task<DesktopHarness> OpenAsync(IBrowser browser, AspireAppHost appHost, ViewportSize viewport, CancellationToken cancellationToken)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = viewport });
        return new DesktopHarness(await context.NewPageAsync(), await appHost.UrlAsync(Resource, cancellationToken));
    }

    // ---- Settings ----

    /// <summary>Opens one Settings page by its address, <c>settings/&lt;page&gt;</c>,
    /// and waits for the menu to mark that page's link current. The link is found
    /// by the page id it carries rather than by its name, which also holds the
    /// page's badge (<c>Features 12/19</c>). An address naming a page that is not
    /// on offer shows Features instead, so the wait times out naming the page that
    /// never came.</summary>
    public async Task OpenSettingsAsync(string page)
    {
        await Page.GotoAsync(new Uri(_baseUrl, $"settings/{page}").ToString());
        var current = Page.Locator($"a[data-page='{page}'][aria-current='page']");
        await Interactive.EventuallyAsync(
            async () => await current.CountAsync() > 0,
            TimeSpan.FromSeconds(30),
            $"the {page} settings page to be the current one");
    }

    /// <summary>Turns on the two features the scenario needs: <c>sync</c>, without
    /// which there is no Devices page, and the Inbox pane, which is off by
    /// default. Both are kept per worktree, so this changes nothing elsewhere.</summary>
    public async Task EnableSyncAndInboxAsync()
    {
        await OpenSettingsAsync("features");
        foreach (var feature in new[] { SyncFeature(), InboxPaneFeature() })
        {
            var toggle = Page.GetByRole(AriaRole.Switch, new PageGetByRoleOptions { NameRegex = feature });
            await Interactive.RepeatAsync(
                async () =>
                {
                    if (!await toggle.IsCheckedAsync()) await toggle.ClickAsync();
                },
                () => toggle.IsCheckedAsync(),
                $"turning on {feature}");
        }
    }

    /// <summary>Registers this desktop with the sync service unless it already is,
    /// then issues a single-use pairing code for the phone.</summary>
    public async Task<string> IssuePairingCodeAsync()
    {
        await OpenSettingsAsync("devices");

        // The page first shows the credential it holds, then asks the service
        // about it. Only the answer says whether registering is needed: an
        // emulator restarted without its data no longer knows the device, and
        // the page then offers "Register" beside an alert.
        //
        // A desktop whose token the service no longer accepts — sync restarted
        // under a new development signing key — may never get its device count
        // back on this page. After a grace period a held credential with "Generate
        // a code" offered is taken as registered; generating the code is then
        // the real question, and a refusal fails the run with its message.
        var identity = Page.GetByTestId("devices-identity");
        var grace = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        await Interactive.EventuallyAsync(
            async () => await Page.GetByTestId("devices-empty").IsVisibleAsync()
                        || await Page.GetByTestId("devices-unregistered").IsVisibleAsync()
                        || (await identity.IsVisibleAsync() && (await identity.InnerTextAsync()).Contains("paired", StringComparison.OrdinalIgnoreCase))
                        || (DateTime.UtcNow > grace && await identity.IsVisibleAsync() && await Page.GetByTestId("devices-generate").IsVisibleAsync()),
            TimeSpan.FromSeconds(60),
            "the Devices page to hear from the sync service");

        var register = Page.GetByTestId("devices-register-button");
        if (await register.IsVisibleAsync())
        {
            await Interactive.ClickAsync(register, Page.GetByTestId("devices-busy"));
            await Interactive.EventuallyAsync(
                async () => !await Page.GetByTestId("devices-unregistered").IsVisibleAsync()
                            && await Page.GetByTestId("devices-generate").IsVisibleAsync(),
                TimeSpan.FromSeconds(90),
                "the desktop to be registered");
        }

        await Interactive.ClickAsync(Page.GetByTestId("devices-generate"), Page.GetByTestId("devices-code-display"));
        return (await Page.GetByTestId("devices-code-display").Locator("code").InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Runs a task sync now, rather than waiting up to five minutes for the
    /// worker. The pull is also what brings phone captures into the Inbox.
    ///
    /// <para>One press is one run, and a failed run fails the test. The first
    /// run after <c>sync</c> restarts meets a token signed with the old key;
    /// the desktop runs that cycle once more itself, so it does not end on
    /// "Sync failed: Unauthorized".</para>
    ///
    /// <para>A run is over when the button is idle again, and it failed when a
    /// message appeared: a failed run says why there and leaves the result line
    /// as the last good run wrote it. A run with nothing to move is over in a
    /// few milliseconds, faster than any poll sees the busy state, so the page
    /// itself is asked to notice the button turning busy.</para>
    /// </summary>
    public async Task<string> SyncNowAsync()
    {
        await OpenSettingsAsync("devices");
        var sync = Page.GetByTestId("devices-sync-now");
        var message = Page.GetByTestId("devices-message");
        var started = DateTime.UtcNow;

        await Page.EvaluateAsync(
            """
            () => {
                window.__syncWasBusy = false;
                // aria-busy is added when the run starts: an attribute record whose
                // old value is null, even if the run is already over by now.
                new MutationObserver(records => {
                    if (records.some(r => r.target.dataset.testid === 'devices-sync-now' && r.oldValue === null)) {
                        window.__syncWasBusy = true;
                    }
                }).observe(document.body, { subtree: true, attributes: true, attributeOldValue: true, attributeFilter: ['aria-busy'] });
            }
            """);

        await Interactive.RepeatAsync(
            () => sync.ClickAsync(),
            async () => await Page.EvaluateAsync<bool>("() => window.__syncWasBusy") || await message.IsVisibleAsync(),
            "Sync now");
        await Interactive.EventuallyAsync(
            async () => await sync.GetAttributeAsync("aria-busy") != "true",
            TimeSpan.FromSeconds(90),
            "the desktop's task sync to finish");

        return await message.IsVisibleAsync()
            ? throw new InvalidOperationException($"The desktop's task sync failed: {await message.InnerTextAsync()}")
            : $"{await Page.GetByTestId("devices-sync-result").InnerTextAsync()} ({(DateTime.UtcNow - started).TotalSeconds:0.0} s)";
    }

    // ---- Home panes ----

    /// <summary>Shows one Home pane or view. The Inbox pane reads its list when it
    /// is shown, so a pull is only visible after the pane is shown again. A fresh
    /// load reopens the surface and the view the shared workspace last showed —
    /// another worktree's Dashboard or Roadmap, say — and the option is the way back
    /// from it: during a takeover no option reads pressed, and pressing one closes
    /// the takeover and shows the workspace.</summary>
    private async Task ShowPaneAsync(string option, string pane)
    {
        await Page.GotoAsync(_baseUrl.ToString());
        var toggle = Page.GetByTestId(option);
        await Interactive.RepeatAsync(
            async () =>
            {
                if (await toggle.GetAttributeAsync("aria-pressed") != "true") await toggle.ClickAsync();
            },
            () => Page.Locator($"#{pane}").IsVisibleAsync(),
            $"showing {pane}");
    }

    /// <summary>Shows the Inbox, which takes the whole screen as a surface of its
    /// own rather than opening beside the Tasks view.</summary>
    public Task ShowInboxAsync() => ShowPaneAsync("inbox-pane-option", "inbox-surface");

    /// <summary>Shows the Tasks view — a view in the header's view switch, not a
    /// pane any more.</summary>
    public Task ShowTasksAsync() => ShowPaneAsync("tasks-view-option", "backlog-pane");

    /// <summary>
    /// Shows the Tasks view narrowed to today's My Day, every status, with
    /// Completed unfolded. The filters are the shared workspace's and outlive a
    /// run — another session may have left "Done" or "Ready in a plan" on — so
    /// each is set rather than assumed.
    /// </summary>
    public async Task ShowMyDayAsync()
    {
        await ShowTasksAsync();
        await SetPressedAsync(Page.GetByTestId("readyinplan-filter-option"), false);
        await SetPressedAsync(Page.GetByTestId("myday-filter-option"), true);

        var all = Page.Locator("#status-filter-all");
        await Interactive.RepeatAsync(
            async () =>
            {
                if (await all.GetAttributeAsync("aria-checked") != "true") await all.ClickAsync();
            },
            async () => await all.GetAttributeAsync("aria-checked") == "true",
            "the All status filter");

        // The Completed fold is drawn with the list, a moment after the filters
        // settle; unfolding before it exists unfolds nothing.
        await Page.Locator("[data-task-id]").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await Task.Delay(1_000);
        await ShowCompletedAsync();
    }

    private static Task SetPressedAsync(ILocator toggle, bool pressed) =>
        Interactive.RepeatAsync(
            async () =>
            {
                if (await toggle.CountAsync() > 0 && await toggle.GetAttributeAsync("aria-pressed") != (pressed ? "true" : "false")) await toggle.ClickAsync();
            },
            async () => await toggle.CountAsync() == 0 || await toggle.GetAttributeAsync("aria-pressed") == (pressed ? "true" : "false"),
            $"setting {toggle} to {(pressed ? "pressed" : "not pressed")}");

    public ILocator InboxItem(string title) =>
        Page.GetByTestId("inbox-pane-item").Filter(new LocatorFilterOptions { HasText = title });

    public async Task OpenInboxItemAsync(string title) =>
        await Interactive.ClickAsync(
            InboxItem(title).GetByRole(AriaRole.Button).First,
            Page.GetByTestId("inbox-detail").Filter(new LocatorFilterOptions { HasText = title }));

    public ILocator TaskRow(string title) =>
        Page.Locator("[data-task-id]").Filter(new LocatorFilterOptions { HasText = title });

    /// <summary>Opens an entry in the detail panel, and waits for the panel to
    /// carry its title — a fresh load may already show another entry there.</summary>
    public async Task OpenTaskAsync(string title)
    {
        await Interactive.ClickAsync(
            TaskRow(title).First,
            Page.Locator(".task-panel__title").Filter(new LocatorFilterOptions { HasText = title }));
        await Page.GetByTestId("entry-action-myday").WaitForAsync();
    }

    /// <summary>Whether a task row — an entry or a step — is ticked off: its
    /// check carries the tick mark either way, as a button or as a picture.</summary>
    public static async Task<bool> IsTickedAsync(ILocator row) =>
        await row.Locator(".task-item__check .task-item__tick").CountAsync() > 0;

    /// <summary>Unfolds every Completed section in the list: a ticked-off entry
    /// moves into one, and it is folded by default.</summary>
    public async Task ShowCompletedAsync()
    {
        var folded = Page.Locator("[data-testid$='-completed-toggle'][aria-expanded='false']");
        await Interactive.RepeatAsync(
            async () =>
            {
                if (await folded.CountAsync() > 0) await folded.First.ClickAsync();
            },
            async () => await folded.CountAsync() == 0,
            "unfolding Completed");
    }

    /// <summary>The My Day toggle of the open entry; <c>aria-pressed</c> says
    /// whether it is picked for today.</summary>
    public ILocator MyDayToggle => Page.GetByTestId("entry-action-myday-set");

    /// <summary>A step of the open entry, as the Steps tab lists it. Each step's
    /// title is an input of its own (rename in place), so the row is found by
    /// that input's name rather than by its text.</summary>
    public ILocator Step(string title) =>
        Page.GetByTestId("subitem-list").Locator("[data-task-id]").Filter(new LocatorFilterOptions
        {
            Has = Page.GetByRole(AriaRole.Textbox, new PageGetByRoleOptions { Name = $"Rename {title}", Exact = true }),
        });

    /// <summary>Adds steps to the open entry through the Steps tab's add row.</summary>
    public async Task AddStepsAsync(params string[] steps)
    {
        // The panel opens on whichever view the workspace last showed; Steps is
        // a tab beside the notes.
        var tab = Page.GetByTestId("entry-view-steps");
        await Interactive.RepeatAsync(
            async () =>
            {
                if (await tab.GetAttributeAsync("aria-selected") != "true") await tab.ClickAsync();
            },
            async () => await tab.GetAttributeAsync("aria-selected") == "true",
            "the Steps tab");

        var add = Page.GetByTestId("subitem-list-add-input");
        foreach (var step in steps)
        {
            await Interactive.RepeatAsync(
                async () =>
                {
                    // Typed again only while it has not appeared: each Enter
                    // that lands adds a step.
                    if (await Step(step).CountAsync() > 0) return;
                    await add.FillAsync(step);
                    await add.PressAsync("Enter");
                },
                async () => await Step(step).CountAsync() > 0,
                $"adding the step {step}");
        }
    }

    /// <summary>
    /// Gives the open entry, already in My Day, an agenda time: a start on the
    /// quarter hour and a length in minutes, through the two selects under
    /// "Set a time". Waits for the row to read the slot back.
    /// </summary>
    public async Task SetAgendaTimeAsync(TimeOnly start, int minutes)
    {
        var startSelect = Page.GetByTestId("entry-agenda-start-select").Locator("select");
        await Interactive.ClickAsync(Page.GetByTestId("entry-action-agenda-set"), startSelect);
        await startSelect.SelectOptionAsync(start.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));

        var duration = Page.GetByTestId("entry-agenda-duration-select").Locator("select");
        await duration.WaitForAsync();
        await duration.SelectOptionAsync(minutes.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var end = start.AddMinutes(minutes);
        var slot = $"{start:HH:mm}–{end:HH:mm}";
        await Interactive.EventuallyAsync(
            async () => (await Page.GetByTestId("entry-action-agenda").InnerTextAsync()).Contains(slot, StringComparison.Ordinal),
            TimeSpan.FromSeconds(30),
            $"the agenda time to read {slot}");
    }

    /// <summary>Takes the open entry's agenda time off again, so the block does
    /// not stay in the shared workspace's My Day for the next run to meet.</summary>
    public Task ClearAgendaTimeAsync() =>
        Interactive.RepeatAsync(
            async () =>
            {
                var clear = Page.GetByTestId("entry-action-agenda-clear");
                if (await clear.CountAsync() > 0) await clear.ClickAsync();
            },
            async () => await Page.GetByTestId("entry-action-agenda-clear").CountAsync() == 0,
            "clearing the agenda time");

    // ---- Inbox notes ----

    /// <summary>Rewrites the body of the open Inbox note and saves it.</summary>
    public async Task EditOpenNoteAsync(string body)
    {
        await Interactive.ClickAsync(Page.GetByTestId("inbox-note-edit"), Page.GetByTestId("inbox-note-editor"));
        await Interactive.FillAsync(Page.GetByTestId("inbox-note-body").Locator("textarea"), body);
        await Interactive.ClickAsync(
            Page.GetByTestId("inbox-note-save"),
            Page.GetByTestId("inbox-detail").Filter(new LocatorFilterOptions { HasText = body }).GetByTestId("inbox-note-edit"));
    }

    /// <summary>Archives the open Inbox item with the decision bar's Archive,
    /// which sits beside the detail rather than in it. Done when the item has
    /// left the Inbox list or its detail says it is archived.</summary>
    public Task ArchiveOpenItemAsync(string title)
    {
        var archive = Page.Locator("[data-testid='inbox-archive']:visible").First;
        return Interactive.RepeatAsync(
            async () =>
            {
                if (await archive.CountAsync() > 0 && await archive.IsEnabledAsync()) await archive.ClickAsync();
            },
            async () => await InboxItem(title).CountAsync() == 0
                        || await Page.GetByTestId("inbox-detail").Filter(new LocatorFilterOptions { HasText = "Archived" }).CountAsync() > 0,
            $"archiving {title}");
    }

    [GeneratedRegex(@"^Sync\b")]
    private static partial Regex SyncFeature();

    [GeneratedRegex(@"^Inbox pane\b")]
    private static partial Regex InboxPaneFeature();
}
