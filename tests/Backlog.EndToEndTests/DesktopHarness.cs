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

    public static async Task<DesktopHarness> OpenAsync(IBrowser browser, AspireAppHost appHost, CancellationToken cancellationToken)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        });
        return new DesktopHarness(await context.NewPageAsync(), await appHost.UrlAsync(Resource, cancellationToken));
    }

    // ---- Settings ----

    private async Task OpenSettingsAsync(string tab)
    {
        await Page.GotoAsync(new Uri(_baseUrl, "settings").ToString());
        var pill = Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = tab, Exact = true });
        await Interactive.RepeatAsync(
            () => pill.ClickAsync(),
            async () => await pill.GetAttributeAsync("aria-selected") == "true",
            $"the {tab} settings tab");
    }

    /// <summary>Turns on the two features the scenario needs: <c>sync</c>, without
    /// which there is no Devices tab, and the Inbox pane, which is off by
    /// default. Both are kept per worktree, so this changes nothing elsewhere.</summary>
    public async Task EnableSyncAndInboxAsync()
    {
        await OpenSettingsAsync("Features");
        foreach (var feature in new[] { SyncFeature(), InboxPaneFeature() })
        {
            var checkbox = Page.GetByRole(AriaRole.Checkbox, new PageGetByRoleOptions { NameRegex = feature });
            await Interactive.RepeatAsync(
                async () =>
                {
                    if (!await checkbox.IsCheckedAsync()) await checkbox.ClickAsync();
                },
                () => checkbox.IsCheckedAsync(),
                $"turning on {feature}");
        }
    }

    /// <summary>Registers this desktop with the sync service unless it already is,
    /// then issues a single-use pairing code for the phone.</summary>
    public async Task<string> IssuePairingCodeAsync()
    {
        await OpenSettingsAsync("Devices");

        // The tab first shows the credential it holds, then asks the service
        // about it. Only the answer says whether registering is needed: an
        // emulator restarted without its data no longer knows the device, and
        // the page then offers "Register" beside an alert.
        var identity = Page.GetByTestId("devices-identity");
        await Interactive.EventuallyAsync(
            async () => await Page.GetByTestId("devices-empty").IsVisibleAsync()
                        || await Page.GetByTestId("devices-unregistered").IsVisibleAsync()
                        || (await identity.IsVisibleAsync() && (await identity.InnerTextAsync()).Contains("paired", StringComparison.OrdinalIgnoreCase)),
            TimeSpan.FromSeconds(60),
            "the Devices tab to hear from the sync service");

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
        await OpenSettingsAsync("Devices");
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

    public Task ShowInboxAsync() => ShowPaneAsync("inbox-pane-option", "inbox-pane");

    /// <summary>Shows the Tasks view — a view in the header's view switch, not a
    /// pane any more.</summary>
    public Task ShowTasksAsync() => ShowPaneAsync("tasks-view-option", "backlog-pane");

    public ILocator InboxItem(string title) =>
        Page.GetByTestId("inbox-pane-item").Filter(new LocatorFilterOptions { HasText = title });

    public async Task OpenInboxItemAsync(string title) =>
        await Interactive.ClickAsync(
            InboxItem(title).GetByRole(AriaRole.Button).First,
            Page.GetByTestId("inbox-detail").Filter(new LocatorFilterOptions { HasText = title }));

    public ILocator TaskRow(string title) =>
        Page.Locator("[data-task-id]").Filter(new LocatorFilterOptions { HasText = title });

    public async Task OpenTaskAsync(string title) =>
        await Interactive.ClickAsync(TaskRow(title).First, Page.GetByTestId("entry-action-myday"));

    [GeneratedRegex(@"^Sync\b")]
    private static partial Regex SyncFeature();

    [GeneratedRegex(@"^Inbox pane\b")]
    private static partial Regex InboxPaneFeature();
}
