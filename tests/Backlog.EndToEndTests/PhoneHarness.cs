using Microsoft.Playwright;

namespace Backlog.EndToEndTests;

/// <summary>
/// The phone, as <c>mobile-web-harness</c> serves it: <c>Backlog.Mobile.UI</c> at
/// 390 × 844. Every hook here is a <c>data-testid</c> the screen already carries.
///
/// <para>Three tabs — Today (<c>/</c>), Inbox (<c>/inbox</c>) and Notes
/// (<c>/notes</c>) — with the capture sheet (<c>/capture</c>) opened over Today
/// by its round button, a task's own page (<c>/tasks/{id}</c>) and the note
/// editor (<c>/notes/new</c>, <c>/notes/{id}</c>).</para>
/// </summary>
internal sealed class PhoneHarness
{
    public const string Resource = "mobile-web-harness";

    /// <summary>The file whose presence keeps the harness from pairing itself;
    /// <c>LocalDevelopmentPairing.ManualPairingMarkerFileName</c>, which this
    /// project cannot reference.</summary>
    private const string ManualPairingMarker = "manual-pairing";

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
    /// <para>"Forget this device" lets the credential go but keeps the outbox, so
    /// the harness is stopped, its device credential and its outbox database
    /// (which also holds the cached Inbox, the task view and the notes) are
    /// removed, and it is started again. They live in the harness's own
    /// <c>obj/local-development</c>, so no other worktree is touched.</para>
    ///
    /// <para>It also leaves the <c>manual-pairing</c> marker there. Under an
    /// AppHost that pairs the harnesses itself, a phone with no credential would
    /// otherwise be paired again before the test reached the pairing screen; the
    /// marker tells the harness to leave this one for the test. Delete it to hand
    /// the phone back to the automatic pairing.</para>
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

        state.Create();
        await File.WriteAllTextAsync(Path.Combine(state.FullName, ManualPairingMarker), string.Empty, cancellationToken);

        await appHost.StartAsync(Resource, cancellationToken);
    }

    public static async Task<PhoneHarness> OpenAsync(IBrowser browser, AspireAppHost appHost, CancellationToken cancellationToken)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
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

    private ILocator Heading(string name) =>
        Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = name, Level = 1, Exact = true });

    /// <summary>Opens one of the three tabs by its label — <c>Today</c>,
    /// <c>Inbox</c> or <c>Notes</c> — and waits for its heading. The tab bar
    /// names each tab <c>tab-bar-{label}</c>.</summary>
    public Task GoToAsync(string tab) =>
        Interactive.ClickAsync(Page.GetByTestId($"tab-bar-{tab.ToLowerInvariant()}"), Heading(tab));

    /// <summary>Loads a tab again from its address, which runs the pull the tab
    /// makes when it opens — the way the app does when it comes back.</summary>
    public async Task ReloadAsync(string route, string heading)
    {
        await Page.GotoAsync(new Uri(_baseUrl, route).ToString());
        await Heading(heading).WaitForAsync();
    }

    // ---- Capture sheet ----

    public ILocator CaptureSheet => Page.GetByTestId("capture-sheet");

    /// <summary>Opens the capture sheet from Today's round button.</summary>
    public async Task OpenCaptureAsync()
    {
        await GoToAsync("Today");
        await Interactive.ClickAsync(Page.GetByTestId("today-capture"), CaptureSheet);
    }

    /// <summary>
    /// Saves one thought from the open sheet as <paramref name="target"/> —
    /// <c>inbox</c>, <c>note</c> or <c>today</c> — with any pictures set on the
    /// harness's hidden picture input, and waits for the sheet's line saying
    /// where it went. The sheet stays open and keeps the choice.
    /// </summary>
    public async Task CaptureAsync(string target, string text, params string[] pictures)
    {
        var choice = Page.GetByTestId($"capture-sheet-target-{target}");
        await Interactive.RepeatAsync(
            () => choice.ClickAsync(),
            async () => await choice.GetAttributeAsync("aria-pressed") == "true",
            $"choosing {target} on the capture sheet");

        await Interactive.FillAsync(Page.GetByTestId("capture-sheet-text").Locator("textarea"), text);

        if (pictures.Length > 0)
        {
            // The hidden input WebAttachmentPicker reads: files set on it with no
            // pick waiting arrive exactly as a picked photo would.
            await Page.GetByTestId("note-input-pictures").SetInputFilesAsync(pictures);
            await Interactive.EventuallyAsync(
                async () => await CaptureSheet.GetByTestId("attachment-tile").CountAsync() == pictures.Length,
                TimeSpan.FromSeconds(30),
                $"{pictures.Length} attachment tiles on the capture sheet");
        }

        var saved = target switch
        {
            "note" => "Note saved",
            "today" => "Added to today",
            _ => "Added to inbox"
        };
        await Interactive.ClickAsync(
            Page.GetByTestId("capture-sheet-save"),
            Page.GetByTestId("capture-sheet-status").Filter(new LocatorFilterOptions { HasText = saved }));
    }

    /// <summary>Closes the sheet; it goes back to Today, which it was opened over.</summary>
    public Task CloseCaptureAsync() =>
        Interactive.ClickAsync(Page.GetByTestId("capture-sheet-cancel"), Heading("Today"));

    // ---- Inbox ----

    public ILocator InboxRow(string title) => Page.GetByTestId("inbox-row").Filter(new LocatorFilterOptions { HasText = title });

    public Task RefreshInboxAsync() => Page.GetByTestId("inbox-refresh").ClickAsync();

    // ---- Today ----

    public ILocator NowCard => Page.GetByTestId("today-card");

    public ILocator Agenda => Page.GetByTestId("today-agenda");

    public ILocator Anytime => Page.GetByTestId("today-anytime");

    public ILocator DoneToday => Page.GetByTestId("today-done");

    /// <summary>A row in one of Today's groups. The task on the card is not a
    /// row: it is <see cref="NowCard"/>.</summary>
    public ILocator TaskRow(string title) => Page.GetByTestId("task-row").Filter(new LocatorFilterOptions { HasText = title });

    public Task RefreshTodayAsync() => Page.GetByTestId("tasks-refresh").ClickAsync();

    /// <summary>Ticks a row off on Today with its circle.</summary>
    public Task TickAsync(string title)
    {
        var row = TaskRow(title);
        return Interactive.RepeatAsync(
            async () =>
            {
                if (await row.GetAttributeAsync("data-done") != "true") await row.GetByTestId("task-row-tick").ClickAsync();
            },
            async () => await row.GetAttributeAsync("data-done") == "true",
            $"ticking off {title}");
    }

    /// <summary>Opens a task's own page from Today — from the card or a row.</summary>
    public Task OpenTaskAsync(string title) =>
        Interactive.ClickAsync(
            Page.Locator("[data-testid=today-card-open], [data-testid=task-row-open]").Filter(new LocatorFilterOptions { HasText = title }).First,
            Page.GetByTestId("task-detail-title").Filter(new LocatorFilterOptions { HasText = title }));

    public ILocator Step(string title) =>
        Page.GetByTestId("task-detail-step").Filter(new LocatorFilterOptions { HasText = title });

    /// <summary>Ticks a step on the open task's page.</summary>
    public Task TickStepAsync(string title)
    {
        var step = Step(title);
        return Interactive.RepeatAsync(
            async () =>
            {
                if (await step.GetAttributeAsync("data-done") != "true") await step.GetByTestId("task-detail-step-box").ClickAsync();
            },
            async () => await step.GetAttributeAsync("data-done") == "true",
            $"ticking the step {title}");
    }

    /// <summary>Moves the open task to tomorrow; the page goes back to Today.</summary>
    public Task MoveToTomorrowAsync() =>
        Interactive.ClickAsync(Page.GetByTestId("task-detail-tomorrow"), Heading("Today"));

    // ---- Notes ----

    public ILocator NoteRow(string title) => Page.GetByTestId("note-row").Filter(new LocatorFilterOptions { HasText = title });

    /// <summary>The editor's save line: its text says Saved once the phone has
    /// kept the note, and <c>data-waiting</c> says whether sync has it yet.</summary>
    public ILocator NoteSave => Page.GetByTestId("note-editor-save");

    /// <summary>Starts a note from the Notes tab and types it. The editor saves
    /// on its own after a pause; the caller waits for that on <see cref="NoteSave"/>.</summary>
    public async Task WriteNoteAsync(string title, string body)
    {
        await GoToAsync("Notes");
        await Interactive.ClickAsync(Page.GetByTestId("notes-new"), Page.GetByTestId("note-editor"));
        await Interactive.FillAsync(Page.GetByTestId("note-editor-title").Locator("input"), title);
        await Interactive.FillAsync(Page.GetByTestId("note-editor-body").Locator("textarea"), body);
    }

    /// <summary>
    /// What the phone does when the network comes back or the app returns to the
    /// foreground: it clears the outbox's backoff and flushes. The harness's
    /// outbox runs server-side, so a browser offline switch cannot take the
    /// service away from it — the scenario stops the <c>sync</c> resource instead,
    /// and raises the page's own <c>online</c> event when it is back.
    /// </summary>
    public Task ComeBackOnlineAsync() => Page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))");
}
