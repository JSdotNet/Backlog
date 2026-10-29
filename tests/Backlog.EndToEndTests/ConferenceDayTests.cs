using System.Globalization;
using System.Text;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Backlog.EndToEndTests;

/// <summary>
/// A conference day on the redesigned phone, end to end: the phone paired to the
/// desktop, thoughts captured through the capture sheet as Inbox, Note and Today,
/// the day planned on the desktop and worked through on the phone, a note written
/// on the phone and looked after on the desktop, and captures made while the
/// service is away that arrive once it is back.
///
/// <para>One test, not eight: every step stands on what the one before it left
/// behind, and a step run alone would have to rebuild all of it. The steps are
/// named in the failure message and in the screenshots instead.</para>
///
/// <para>It runs against the AppHost this worktree already has running, and
/// takes the <c>sync</c> resource away and back through the Aspire CLI to make the
/// phone offline — the harness's outbox runs on the server, so switching the
/// browser offline would not reach it. <c>sync</c> is started again whatever
/// happens, so a failed run does not leave the AppHost without its service.</para>
/// </summary>
public sealed class ConferenceDayTests
{
    private const string Sync = "sync";

    private static readonly TimeSpan Flush = TimeSpan.FromSeconds(90);

    [Fact(Timeout = 20 * 60 * 1000)]
    public async Task A_conference_day_on_the_phone_reaches_the_desktop_and_comes_back()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("BACKLOG_E2E") == "1",
            "Browser end-to-end tests run against a running AppHost; set BACKLOG_E2E=1 to run them (tests/Backlog.EndToEndTests/README.md).");

        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = AspireAppHost.ForThisWorktree();
        var evidence = Evidence.For("conference-day");
        var token = DateTime.Now.ToString("MMdd-HHmmss", CultureInfo.InvariantCulture);

        foreach (var resource in new[] { "cosmos", "storage", Sync, DesktopHarness.Resource, PhoneHarness.Resource })
        {
            await appHost.WaitHealthyAsync(resource, TimeSpan.FromMinutes(10), cancellationToken);
        }

        var errorsBefore = AspireAppHost.LogIds(await appHost.LogsJsonAsync(Sync, "Error", cancellationToken)).ToHashSet();
        var log = new StringBuilder();
        var syncStopped = false;
        DesktopHarness? tidyDesktop = null;
        var timed = new List<string>();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = Environment.GetEnvironmentVariable("BACKLOG_E2E_HEADED") != "1",
            Channel = Environment.GetEnvironmentVariable("BACKLOG_E2E_BROWSER_CHANNEL") is { Length: > 0 } channel ? channel : null,
        });

        try
        {
            await PhoneHarness.ResetAsync(appHost, cancellationToken);
            var desktop = await DesktopHarness.OpenAsync(browser, appHost, cancellationToken);
            tidyDesktop = desktop;
            await desktop.EnableSyncAndInboxAsync();
            var phone = await PhoneHarness.OpenAsync(browser, appHost, cancellationToken);

            // Runs a desktop sync, then the check, until the check holds: what the
            // phone sent reaches the desktop on the desktop's next pull, and what
            // the desktop changed reaches the phone on the phone's.
            // A wait that runs out leaves a screenshot of the side it waited on.
            async Task OnDesktopEventuallyAsync(Func<Task<bool>> check, string what)
            {
                try
                {
                    await Interactive.EventuallyAsync(
                        async () =>
                        {
                            log.AppendLine($"desktop sync: {await desktop.SyncNowAsync()}");
                            return await check();
                        },
                        Flush,
                        what);
                }
                catch (TimeoutException)
                {
                    await evidence.ScreenshotAsync(desktop.Page, "desktop-timed-out");
                    throw;
                }
            }

            async Task OnPhoneEventuallyAsync(string route, string heading, Func<Task<bool>> check, string what)
            {
                try
                {
                    await Interactive.EventuallyAsync(
                        async () =>
                        {
                            await phone.ReloadAsync(route, heading);
                            await Task.Delay(1_000);
                            return await check();
                        },
                        Flush,
                        what);
                }
                catch (TimeoutException)
                {
                    await evidence.ScreenshotAsync(phone.Page, "phone-timed-out");
                    throw;
                }
            }

            // 1. Pair the phone with a code the desktop issued. It opens on Today.
            var code = await desktop.IssuePairingCodeAsync();
            await phone.PairAsync(code);
            await Interactive.EventuallyAsync(async () => await phone.SyncStateAsync() == "synced", Flush, "the phone to read synced");
            await Expect(phone.SyncStatus).ToContainTextAsync("Synced");
            await Expect(phone.Page.GetByRole(AriaRole.Heading, new() { Name = "Today", Level = 1 })).ToBeVisibleAsync();
            await evidence.ScreenshotAsync(phone.Page, "phone-paired-today");

            // 2. Capture through the sheet as Inbox, Note and Today. The four Today
            //    captures are the tasks the rest of the day works on.
            var inboxCapture = $"Sheet inbox {token}";
            var noteCapture = $"Sheet note {token}";
            var nowTask = $"Now block {token}";
            var laterTask = $"Agenda later {token}";
            var tickTask = $"Tick me {token}";
            var moveTask = $"Move me {token}";

            await phone.OpenCaptureAsync();
            await phone.CaptureAsync("inbox", inboxCapture);
            await evidence.ScreenshotAsync(phone.Page, "phone-sheet-inbox");
            await phone.CaptureAsync("note", noteCapture);
            await evidence.ScreenshotAsync(phone.Page, "phone-sheet-note");
            foreach (var task in new[] { nowTask, laterTask, tickTask, moveTask })
            {
                await phone.CaptureAsync("today", task);
            }

            await evidence.ScreenshotAsync(phone.Page, "phone-sheet-today");
            await phone.CloseCaptureAsync();
            foreach (var task in new[] { nowTask, laterTask, tickTask, moveTask })
            {
                await Expect(phone.Anytime.GetByTestId("task-row").Filter(new() { HasText = task })).ToBeVisibleAsync();
            }

            await evidence.ScreenshotAsync(phone.Page, "phone-today-anytime");

            await phone.GoToAsync("Inbox");
            await Expect(phone.InboxRow(inboxCapture)).ToHaveAttributeAsync("data-state", "sent", new() { Timeout = 30_000 });

            await OnDesktopEventuallyAsync(
                async () =>
                {
                    await desktop.ShowInboxAsync();
                    return await ShowsAsync(desktop.InboxItem(inboxCapture)) && await ShowsAsync(desktop.InboxItem(noteCapture));
                },
                "the Inbox and Note captures to reach the desktop Inbox");
            await Expect(desktop.InboxItem(inboxCapture).GetByTestId("inbox-pane-item-source")).ToHaveTextAsync("Mobile");
            await Expect(desktop.InboxItem(noteCapture).GetByTestId("inbox-pane-item-source")).ToHaveTextAsync("Mobile");
            await evidence.ScreenshotAsync(desktop.Page, "desktop-inbox-has-inbox-and-note");

            await OnDesktopEventuallyAsync(
                async () =>
                {
                    await desktop.ShowMyDayAsync();
                    foreach (var task in new[] { nowTask, laterTask, tickTask, moveTask })
                    {
                        if (!await ShowsAsync(desktop.TaskRow(task))) return false;
                    }

                    return true;
                },
                "the Today captures to reach the desktop's My Day");
            await Expect(desktop.TaskRow(nowTask).Locator(".task-item__detail--myday")).ToBeVisibleAsync();
            await evidence.ScreenshotAsync(desktop.Page, "desktop-today-captures-in-my-day");

            // 3. On the desktop, give the first task two steps and a block that
            //    holds the current time, and the second a block later on. The
            //    phone shows the first as Now and the second under Agenda.
            //    The Now card is the open timed task whose block holds the time
            //    and starts first, and the shared workspace's My Day may hold
            //    other runs' blocks. So the block starts as early as it can while
            //    still holding the time for ten more minutes: the first quarter
            //    hour after now less 110 minutes, or midnight in the first two
            //    hours of the day, where an earlier start would wrap to yesterday.
            var now = TimeOnly.FromDateTime(DateTime.Now);
            var blockStart = BlockStartFor(now);
            var laterStart = blockStart.AddHours(3);
            var stepOne = $"Step one {token}";
            var stepTwo = $"Step two {token}";

            await desktop.OpenTaskAsync(nowTask);
            await desktop.AddStepsAsync(stepOne, stepTwo);
            timed.Add(nowTask);
            await desktop.SetAgendaTimeAsync(blockStart, 120);
            await desktop.ShowMyDayAsync();
            await desktop.OpenTaskAsync(laterTask);
            timed.Add(laterTask);
            await desktop.SetAgendaTimeAsync(laterStart, 30);
            await evidence.ScreenshotAsync(desktop.Page, "desktop-my-day-agenda-times");
            log.AppendLine($"agenda: {nowTask} {blockStart:HH:mm}+120, {laterTask} {laterStart:HH:mm}+30, phone clock {now:HH:mm}");
            log.AppendLine($"desktop sync: {await desktop.SyncNowAsync()}");

            await phone.Page.BringToFrontAsync();
            await OnPhoneEventuallyAsync(
                "",
                "Today",
                async () => await ShowsAsync(phone.NowCard.GetByTestId("today-card-title"))
                            && (await phone.NowCard.GetByTestId("today-card-title").InnerTextAsync()).Contains(nowTask, StringComparison.Ordinal)
                            && await ShowsAsync(phone.Agenda.GetByTestId("task-row").Filter(new() { HasText = laterTask })),
                "the phone's Today to show the block as Now and the later one under Agenda");
            await Expect(phone.NowCard.GetByTestId("today-card-kicker")).ToContainTextAsync("Now");
            await Expect(phone.NowCard.GetByTestId("today-card-next")).ToContainTextAsync(stepOne);
            await Expect(phone.Agenda.GetByTestId("task-row").Filter(new() { HasText = laterTask }).GetByTestId("task-row-meta"))
                .ToContainTextAsync($"{laterStart:HH:mm}–{laterStart.AddMinutes(30):HH:mm}");
            await evidence.ScreenshotAsync(phone.Page, "phone-today-now-and-agenda");

            // 4. Tick a task and a step on the phone; the desktop has both.
            await phone.TickAsync(tickTask);
            await Expect(phone.DoneToday.GetByTestId("task-row").Filter(new() { HasText = tickTask })).ToBeVisibleAsync();
            await Interactive.EventuallyAsync(
                async () => await phone.TaskRow(tickTask).GetAttributeAsync("data-waiting") == "false",
                Flush,
                "the tick to leave the phone");
            await evidence.ScreenshotAsync(phone.Page, "phone-ticked-done");

            await phone.OpenTaskAsync(nowTask);
            await phone.TickStepAsync(stepOne);
            await Expect(phone.Step(stepTwo)).ToHaveAttributeAsync("data-done", "false");
            await Interactive.EventuallyAsync(
                async () => await phone.Page.GetByTestId("task-detail-waiting").CountAsync() == 0,
                Flush,
                "the step tick to leave the phone");
            await evidence.ScreenshotAsync(phone.Page, "phone-step-ticked");

            await OnDesktopEventuallyAsync(
                async () =>
                {
                    // A ticked task moves into the Completed fold, drawn once
                    // the list has settled, so it is unfolded once more here.
                    await desktop.ShowMyDayAsync();
                    await Task.Delay(1_000);
                    await desktop.ShowCompletedAsync();
                    return await ShowsAsync(desktop.TaskRow(tickTask)) && await DesktopHarness.IsTickedAsync(desktop.TaskRow(tickTask));
                },
                "the phone's tick to reach the desktop");
            await evidence.ScreenshotAsync(desktop.Page, "desktop-shows-done");

            await desktop.OpenTaskAsync(nowTask);
            Assert.True(await desktop.IsStepDoneAsync(stepOne), $"{stepOne} should be ticked on the desktop.");
            Assert.False(await desktop.IsStepDoneAsync(stepTwo), $"{stepTwo} should still be open on the desktop.");
            await evidence.ScreenshotAsync(desktop.Page, "desktop-step-ticked");

            // 5. Move a task to tomorrow from the phone: it leaves Today, and the
            //    desktop's My Day.
            await phone.Page.BringToFrontAsync();
            await phone.ReloadAsync("", "Today");
            await phone.OpenTaskAsync(moveTask);
            await phone.MoveToTomorrowAsync();
            await Expect(phone.TaskRow(moveTask)).ToHaveCountAsync(0);
            await evidence.ScreenshotAsync(phone.Page, "phone-moved-off-today");

            await OnDesktopEventuallyAsync(
                async () =>
                {
                    await desktop.ShowMyDayAsync();
                    return await ShowsAsync(desktop.TaskRow(nowTask)) && await desktop.TaskRow(moveTask).CountAsync() == 0;
                },
                "the moved task to leave the desktop's My Day");
            await evidence.ScreenshotAsync(desktop.Page, "desktop-my-day-without-moved");

            // 6. A note written on the phone, edited on the desktop — the phone
            //    sees the edit — and archived there, which takes it off the phone.
            var note = $"Phone note {token}";
            var edited = $"Written on the phone. Edited on the desktop {token}.";
            await phone.WriteNoteAsync(note, $"Written on the phone {token}.");
            await Expect(phone.NoteSave).ToHaveAttributeAsync("data-waiting", "false", new() { Timeout = (float)Flush.TotalMilliseconds });
            await Expect(phone.NoteSave).ToContainTextAsync("Saved");
            await evidence.ScreenshotAsync(phone.Page, "phone-note-created");

            await OnDesktopEventuallyAsync(
                async () =>
                {
                    await desktop.ShowInboxAsync();
                    return await ShowsAsync(desktop.InboxItem(note));
                },
                "the phone's note to reach the desktop Inbox");
            await desktop.OpenInboxItemAsync(note);
            await evidence.ScreenshotAsync(desktop.Page, "desktop-note-opened");
            await desktop.EditOpenNoteAsync(edited);
            await evidence.ScreenshotAsync(desktop.Page, "desktop-note-edited");
            log.AppendLine($"desktop sync: {await desktop.SyncNowAsync()}");

            await phone.Page.BringToFrontAsync();
            await OnPhoneEventuallyAsync(
                "notes",
                "Notes",
                async () => await ShowsAsync(phone.NoteRow(note))
                            && (await phone.NoteRow(note).InnerTextAsync()).Contains("Edited on the desktop", StringComparison.Ordinal),
                "the desktop's edit to reach the phone's note");
            await evidence.ScreenshotAsync(phone.Page, "phone-sees-desktop-edit");

            await desktop.ShowInboxAsync();
            await desktop.OpenInboxItemAsync(note);
            await desktop.ArchiveOpenItemAsync(note);
            await evidence.ScreenshotAsync(desktop.Page, "desktop-note-archived");
            log.AppendLine($"desktop sync: {await desktop.SyncNowAsync()}");

            await phone.Page.BringToFrontAsync();
            await OnPhoneEventuallyAsync(
                "notes",
                "Notes",
                async () => await ShowsAsync(phone.Page.GetByTestId("notes-count")) && await phone.NoteRow(note).CountAsync() == 0,
                "the archived note to leave the phone's Notes");
            await evidence.ScreenshotAsync(phone.Page, "phone-archived-note-gone");

            // 7. Offline: an Inbox capture with a photo, a plain one and a task for
            //    today wait on the phone, then go — the upload first — once sync
            //    is back, and the desktop has all three.
            //    Loaded before sync goes: a page loaded from the address pulls
            //    while it prerenders, and with the service away that pull waits
            //    out its timeout. From here on the phone moves by its tabs.
            await phone.ReloadAsync("", "Today");
            syncStopped = true;
            await appHost.StopAsync(Sync, cancellationToken);
            log.AppendLine($"{DateTimeOffset.UtcNow:O} sync stopped");

            var offlinePhoto = $"Offline photo {token}";
            var offlineCapture = $"Offline capture {token}";
            var offlineTask = $"Offline task {token}";
            await phone.OpenCaptureAsync();
            await phone.CaptureAsync("inbox", offlinePhoto, Fixture("slide-photo.jpg"));
            await phone.CaptureAsync("inbox", offlineCapture);
            await phone.CaptureAsync("today", offlineTask);
            await phone.CloseCaptureAsync();

            await Expect(phone.TaskRow(offlineTask)).ToHaveAttributeAsync("data-waiting", "true", new() { Timeout = 5_000 });
            await Expect(phone.TaskRow(offlineTask).GetByTestId("task-waiting")).ToBeVisibleAsync();

            // Proof the push was tried and failed, not merely queued a moment ago.
            await Interactive.EventuallyAsync(
                async () => await phone.SyncStateAsync() == "offline",
                TimeSpan.FromSeconds(60),
                "the phone to read offline after its push failed");
            await Expect(phone.SyncStatus).ToContainTextAsync("waiting");
            await evidence.ScreenshotAsync(phone.Page, "phone-today-task-waiting");

            await phone.GoToAsync("Inbox");
            await Expect(phone.InboxRow(offlinePhoto)).ToHaveAttributeAsync("data-state", "waiting");
            await Expect(phone.InboxRow(offlineCapture)).ToHaveAttributeAsync("data-state", "waiting");
            await evidence.ScreenshotAsync(phone.Page, "phone-inbox-waiting");

            var back = DateTimeOffset.UtcNow;
            await appHost.StartAsync(Sync, cancellationToken);
            syncStopped = false;
            log.AppendLine($"{DateTimeOffset.UtcNow:O} sync started");
            await phone.ComeBackOnlineAsync();

            await WaitUntilSentAsync(phone, evidence, phone.InboxRow(offlinePhoto), phone.InboxRow(offlineCapture));
            await Interactive.EventuallyAsync(
                async () =>
                {
                    if (await phone.SyncStateAsync() == "synced") return true;
                    await phone.RefreshInboxAsync();
                    return false;
                },
                Flush,
                "the phone to read synced again");
            await evidence.ScreenshotAsync(phone.Page, "phone-inbox-flushed-synced");

            await phone.GoToAsync("Today");
            await WaitUntilSentAsync(phone, evidence, phone.TaskRow(offlineTask));
            await Expect(phone.TaskRow(offlineTask).GetByTestId("task-waiting")).ToHaveCountAsync(0);
            await evidence.ScreenshotAsync(phone.Page, "phone-today-task-synced");

            // What the phone sent to the Inbox once sync answered again, in the
            // order it sent it. A 401 on the first attempt is the phone meeting the
            // restarted service's new signing key and fetching a token; only
            // answered calls count. Telemetry is exported in batches, so it is read
            // until all three arrived.
            IReadOnlyList<AspireAppHost.Span> flushed = [];
            await Interactive.EventuallyAsync(
                async () =>
                {
                    flushed = (await appHost.CallsAsync(PhoneHarness.Resource, Sync, cancellationToken))
                        .Where(span => span.Timestamp >= back && span.Succeeded)
                        .Where(span => (span.Method == "PUT" && span.Path.StartsWith("/api/sync/attachments/", StringComparison.Ordinal))
                                       || (span.Method == "POST" && span.Path == "/api/sync/inbox"))
                        .ToList();
                    return flushed.Count >= 3;
                },
                TimeSpan.FromSeconds(30),
                "the phone's flush to reach the dashboard's telemetry");
            log.AppendLine("Sent by the phone to the Inbox after sync came back:");
            foreach (var span in flushed) log.AppendLine($"  {span}");
            Assert.Equal(
                ["PUT attachment", "POST inbox", "POST inbox"],
                flushed.Select(span => $"{span.Method} {(span.Path == "/api/sync/inbox" ? "inbox" : "attachment")}").ToArray());

            await OnDesktopEventuallyAsync(
                async () =>
                {
                    await desktop.ShowInboxAsync();
                    return await ShowsAsync(desktop.InboxItem(offlinePhoto)) && await ShowsAsync(desktop.InboxItem(offlineCapture));
                },
                "the offline captures to reach the desktop Inbox");
            await desktop.OpenInboxItemAsync(offlinePhoto);
            await Expect(desktop.Page.GetByTestId("inbox-detail-attachment-image")).ToBeVisibleAsync(new() { Timeout = 60_000 });
            await evidence.ScreenshotAsync(desktop.Page, "desktop-offline-photo-detail");

            await OnDesktopEventuallyAsync(
                async () =>
                {
                    await desktop.ShowMyDayAsync();
                    return await ShowsAsync(desktop.TaskRow(offlineTask));
                },
                "the offline task to reach the desktop's My Day");
            await desktop.OpenTaskAsync(offlineTask);
            await Expect(desktop.MyDayToggle).ToHaveAttributeAsync("aria-pressed", "true");
            await evidence.ScreenshotAsync(desktop.Page, "desktop-offline-task-in-my-day");
        }
        finally
        {
            // Never leave the AppHost without its service, whatever step failed. A
            // failed step does not cancel the run, so its token still lets this run.
            // Only when this run stopped it: starting a running resource restarts
            // it, and a restart mints a new signing key every device then has to
            // recover from.
            try
            {
                if (syncStopped) await appHost.StartAsync(Sync, cancellationToken);
            }
            catch (Exception exception)
            {
                log.AppendLine($"Could not start sync again: {exception.Message}");
            }

            // Tidy, pass or fail: an agenda block left in the shared My Day would
            // be the next run's rival for the Now card, and the one that started
            // earlier wins it.
            if (tidyDesktop is not null && timed.Count > 0)
            {
                try
                {
                    foreach (var task in timed)
                    {
                        await tidyDesktop.ShowMyDayAsync();
                        await tidyDesktop.OpenTaskAsync(task);
                        await tidyDesktop.ClearAgendaTimeAsync();
                    }

                    log.AppendLine($"tidy: cleared the agenda times; desktop sync: {await tidyDesktop.SyncNowAsync()}");
                }
                catch (Exception exception)
                {
                    log.AppendLine($"Could not clear the agenda times: {exception.Message}");
                }
            }

            await evidence.WriteAsync("run.log", log.ToString());
            await evidence.WriteAsync("sync-structured-logs.json", await appHost.LogsJsonAsync(Sync, "Information", cancellationToken));
        }

        var errors = await appHost.LogsJsonAsync(Sync, "Error", cancellationToken);
        await evidence.WriteAsync("sync-errors.json", errors);
        Assert.DoesNotContain(AspireAppHost.LogIds(errors), id => !errorsBefore.Contains(id));
    }

    /// <summary>The earliest quarter hour whose two-hour block still holds
    /// <paramref name="now"/> ten minutes from now; midnight before 01:50.</summary>
    private static TimeOnly BlockStartFor(TimeOnly now)
    {
        var minutes = (int)now.ToTimeSpan().TotalMinutes - 110;
        if (minutes <= 0) return TimeOnly.MinValue;
        var quarter = (minutes + 14) / 15 * 15;
        return TimeOnly.MinValue.AddMinutes(quarter);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>
    /// Whether a row is on screen, given a few seconds to arrive. A pane that
    /// was just shown reads its list after it appears, so counting at once would
    /// read an empty list as an answer.
    /// </summary>
    private static async Task<bool> ShowsAsync(ILocator locator, float timeoutMs = 10_000)
    {
        try
        {
            await locator.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Waits for rows the phone queued while offline to stop saying they wait.
    /// The outbox flushes on its own once it is resumed, and its rows follow —
    /// the phone is not nudged, so a row that loses its delivery fails the run.
    /// A row missing from the list counts as not delivered.
    /// </summary>
    private static async Task WaitUntilSentAsync(PhoneHarness phone, Evidence evidence, params ILocator[] rows)
    {
        try
        {
            await Interactive.EventuallyAsync(
                async () =>
                {
                    foreach (var row in rows)
                    {
                        // An Inbox row says "sent" in data-state; a Today row says
                        // "false" in data-waiting.
                        if (await row.CountAsync() != 1) return false;
                        var state = await row.GetAttributeAsync("data-state");
                        var sent = state is not null ? state == "sent" : await row.GetAttributeAsync("data-waiting") == "false";
                        if (!sent) return false;
                    }

                    return true;
                },
                Flush,
                "the queued rows to be sent");
        }
        catch (TimeoutException)
        {
            await evidence.ScreenshotAsync(phone.Page, "phone-still-waiting");
            throw;
        }
    }
}
