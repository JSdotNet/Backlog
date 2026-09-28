using Microsoft.Playwright;

namespace Backlog.EndToEndTests;

/// <summary>
/// The phone, as <c>mobile-web-harness</c> serves it: <c>Backlog.Mobile.UI</c> at
/// 375 px. Every hook here is a <c>data-testid</c> the screen already carries.
/// </summary>
internal sealed class PhoneHarness
{
    public const string Resource = "mobile-web-harness";

    private readonly Uri _baseUrl;

    private PhoneHarness(IPage page, Uri baseUrl)
    {
        Page = page;
        _baseUrl = baseUrl;
    }

    public IPage Page { get; }

    /// <summary>
    /// Makes the harness a phone that has never been paired.
    ///
    /// <para>The phone has no "Forget this device" — on a real one that is an
    /// uninstall — so the harness is stopped, its device credential and its
    /// outbox database (which also holds the cached Inbox and the My Day view)
    /// are removed, and it is started again. They live in the harness's own
    /// <c>obj/local-development</c>, so no other worktree is touched.</para>
    /// </summary>
    public static async Task ResetAsync(AspireAppHost appHost, CancellationToken cancellationToken)
    {
        await appHost.StopAsync(Resource, cancellationToken);

        var state = new DirectoryInfo(Path.Combine(
            RepositoryRoot.Root.FullName, "src", "Harness", "Backlog.Mobile.WebHarness", "obj", "local-development"));
        if (state.Exists)
        {
            foreach (var file in state.EnumerateFiles("device-credential.json")
                         .Concat(state.EnumerateFiles("mobile-outbox.db*")))
            {
                file.Delete();
            }

            var staged = new DirectoryInfo(Path.Combine(state.FullName, "talk-notes"));
            if (staged.Exists) staged.Delete(recursive: true);
        }

        await appHost.StartAsync(Resource, cancellationToken);
    }

    public static async Task<PhoneHarness> OpenAsync(IBrowser browser, AspireAppHost appHost, CancellationToken cancellationToken)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 375, Height = 812 },
            HasTouch = true,
        });
        var phone = new PhoneHarness(await context.NewPageAsync(), await appHost.UrlAsync(Resource, cancellationToken));
        await phone.Page.GotoAsync(phone._baseUrl.ToString());
        return phone;
    }

    public ILocator SyncStatus => Page.GetByTestId("sync-status");

    public Task<string?> SyncStateAsync() => SyncStatus.GetAttributeAsync("data-sync-state");

    public async Task PairAsync(string code)
    {
        var submit = Page.GetByTestId("pairing-submit");
        await Interactive.FillAsync(Page.GetByTestId("pairing-code-field").Locator("input"), code, () => submit.IsEnabledAsync());
        await submit.ClickAsync();
        await Page.GetByTestId("tab-bar").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
    }

    public async Task GoToAsync(string tab, string heading)
    {
        await Interactive.ClickAsync(
            Page.GetByTestId($"tab-bar-{tab}"),
            Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = heading, Level = 1 }));
    }

    // ---- Inbox ----

    public ILocator InboxRow(string title) => Page.GetByTestId("inbox-row").Filter(new LocatorFilterOptions { HasText = title });

    public async Task CaptureAsync(string text)
    {
        var field = Page.GetByTestId("capture-field").Locator("input");
        await Interactive.FillAsync(field, text);
        await Page.GetByTestId("capture-submit").ClickAsync();
    }

    public Task RefreshInboxAsync() => Page.GetByTestId("inbox-refresh").ClickAsync();

    // ---- Talk note ----

    public async Task WriteTalkNoteAsync(string title, string body, string tags, string speaker, params string[] files)
    {
        await Interactive.FillAsync(Page.GetByTestId("note-title").Locator("input"), title);
        await Interactive.FillAsync(Page.GetByTestId("note-body").Locator("textarea"), body);
        await Interactive.FillAsync(Page.GetByTestId("note-tags").Locator("input"), tags);
        await Interactive.FillAsync(Page.GetByTestId("note-speaker").Locator("input"), speaker);

        // The hidden file inputs WebAttachmentPicker reads: a file set on them
        // arrives exactly as one picked through "Choose files" would.
        await Page.GetByTestId("note-input-files").SetInputFilesAsync(files);
        await Interactive.EventuallyAsync(
            async () => await Page.GetByTestId("attachment-tile").CountAsync() == files.Length,
            TimeSpan.FromSeconds(30),
            $"{files.Length} attachment tiles");
    }

    public Task SendTalkNoteAsync() => Interactive.ClickAsync(Page.GetByTestId("note-send"), TalkNoteStatus);

    public ILocator TalkNoteStatus => Page.GetByTestId("note-status");

    // ---- My Day ----

    public ILocator TaskRow(string title) => Page.GetByTestId("task-row").Filter(new LocatorFilterOptions { HasText = title });

    public async Task AddTaskAsync(string title)
    {
        await Interactive.FillAsync(Page.GetByTestId("task-add-field").Locator("input"), title);
        await Page.GetByTestId("task-add-submit").ClickAsync();
    }

    public Task RefreshMyDayAsync() => Page.GetByTestId("tasks-refresh").ClickAsync();

    /// <summary>
    /// What the phone does when the network comes back or the app returns to the
    /// foreground: it clears the outbox's backoff and flushes. The harness's
    /// outbox runs server-side, so a browser offline switch cannot take the
    /// service away from it — the scenario stops the <c>sync</c> resource instead,
    /// and raises the page's own <c>online</c> event when it is back.
    /// </summary>
    public Task ComeBackOnlineAsync() => Page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))");
}
