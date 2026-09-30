using System.Text;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Backlog.EndToEndTests;

/// <summary>
/// The conference day the mobile Inbox slice was built for, end to end: a phone
/// paired to the desktop, captures and a talk note with a slide photo and a
/// handout taken while the service is away, the note routed to Tasks on the
/// desktop, and My Day read and written from the phone.
///
/// <para>One test, not seven: every step stands on what the one before it left
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
    public async Task A_conference_day_reaches_the_desktop_and_comes_back_as_my_day()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("BACKLOG_E2E") == "1",
            "Browser end-to-end tests run against a running AppHost; set BACKLOG_E2E=1 to run them (tests/Backlog.EndToEndTests/README.md).");

        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = AspireAppHost.ForThisWorktree();
        var evidence = Evidence.For("conference-day");
        var token = DateTime.Now.ToString("MMdd-HHmmss");

        foreach (var resource in new[] { "cosmos", "storage", Sync, DesktopHarness.Resource, PhoneHarness.Resource })
        {
            await appHost.WaitHealthyAsync(resource, TimeSpan.FromMinutes(10), cancellationToken);
        }

        var errorsBefore = AspireAppHost.LogIds(await appHost.LogsJsonAsync(Sync, "Error", cancellationToken)).ToHashSet();
        var log = new StringBuilder();

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
            await desktop.EnableSyncAndInboxAsync();
            var phone = await PhoneHarness.OpenAsync(browser, appHost, cancellationToken);

            // 1. Pair the phone with a code the desktop issued.
            var code = await desktop.IssuePairingCodeAsync();
            await phone.PairAsync(code);
            await Interactive.EventuallyAsync(async () => await phone.SyncStateAsync() == "synced", Flush, "the phone to read synced");
            await Expect(phone.SyncStatus).ToContainTextAsync("Synced");
            await evidence.ScreenshotAsync(phone.Page, "phone-paired-synced");

            // 2. A quick capture shows at once, then on the desktop from channel mobile.
            var quick = $"Quick capture {token}";
            await phone.CaptureAsync(quick);
            await Expect(phone.InboxRow(quick)).ToBeVisibleAsync(new() { Timeout = 5_000 });
            await Expect(phone.InboxRow(quick)).ToHaveAttributeAsync("data-waiting", "false", new() { Timeout = 30_000 });
            await evidence.ScreenshotAsync(phone.Page, "phone-quick-capture");

            log.AppendLine($"desktop sync: {await desktop.SyncNowAsync()}");
            await desktop.ShowInboxAsync();
            await Expect(desktop.InboxItem(quick)).ToBeVisibleAsync();
            await Expect(desktop.InboxItem(quick).GetByTestId("inbox-pane-item-source")).ToHaveTextAsync("Mobile");
            await evidence.ScreenshotAsync(desktop.Page, "desktop-quick-capture-mobile");

            // 3. Offline: a talk note with tags, a speaker, a JPEG and a PDF waits on
            //    the phone, and so does a second quick capture.
            await appHost.StopAsync(Sync, cancellationToken);
            log.AppendLine($"{DateTimeOffset.UtcNow:O} sync stopped");

            var note = $"Talk note {token}";
            await phone.GoToAsync("note", "Talk note");
            await phone.WriteTalkNoteAsync(
                note,
                "Offline-first sync, from the keynote. The slide and the handout are attached.",
                "#conference, #e2e",
                "@ada",
                Fixture("slide-photo.jpg"),
                Fixture("handout.pdf"));
            await evidence.ScreenshotAsync(phone.Page, "phone-talk-note-draft");
            await phone.SendTalkNoteAsync();
            // Each attempt against the stopped service reads "uploading 1 of 2"
            // until it gives up; between attempts the note reads "waiting".
            var seen = new List<string>();
            await Interactive.EventuallyAsync(
                async () =>
                {
                    var status = await phone.TalkNoteStatus.GetAttributeAsync("data-status") ?? "";
                    if (seen.LastOrDefault() != status) seen.Add(status);
                    Assert.NotEqual("synced", status);
                    return status.StartsWith("waiting", StringComparison.Ordinal);
                },
                TimeSpan.FromSeconds(60),
                "the talk note to read waiting");
            log.AppendLine($"talk note status while sync is down: {string.Join(" → ", seen)}");
            await evidence.ScreenshotAsync(phone.Page, "phone-talk-note-waiting");

            await phone.GoToAsync("inbox", "Inbox");
            await Expect(phone.InboxRow(note)).ToHaveAttributeAsync("data-waiting", "true");
            var offline = $"Offline capture {token}";
            await phone.CaptureAsync(offline);
            await Expect(phone.InboxRow(offline)).ToHaveAttributeAsync("data-waiting", "true", new() { Timeout = 5_000 });
            await evidence.ScreenshotAsync(phone.Page, "phone-inbox-waiting");

            // 4. Back online: the uploads, then the note's capture, then the second
            //    capture, and the phone reads synced again.
            var back = DateTimeOffset.UtcNow;
            await appHost.StartAsync(Sync, cancellationToken);
            log.AppendLine($"{DateTimeOffset.UtcNow:O} sync started");
            await phone.ComeBackOnlineAsync();

            await WaitUntilSentAsync(phone, evidence, phone.InboxRow(note), phone.InboxRow(offline));
            await Interactive.EventuallyAsync(
                async () =>
                {
                    if (await phone.SyncStateAsync() == "synced") return true;
                    await phone.RefreshInboxAsync();
                    return false;
                },
                Flush,
                "the phone to read synced again");
            await evidence.ScreenshotAsync(phone.Page, "phone-flushed-synced");

            // What the phone sent once sync answered again, in the order it sent it.
            // A 401 on the first attempt is the phone meeting the restarted
            // service's new signing key and fetching a token; only answered calls
            // count.
            // Telemetry is exported in batches, so it is read until all four arrived.
            IReadOnlyList<AspireAppHost.Span> flushed = [];
            await Interactive.EventuallyAsync(
                async () =>
                {
                    flushed = (await appHost.CallsAsync(PhoneHarness.Resource, Sync, cancellationToken))
                        .Where(span => span.Timestamp >= back && span.Succeeded)
                        .Where(span => (span.Method == "PUT" && span.Path.StartsWith("/api/sync/attachments/", StringComparison.Ordinal))
                                       || (span.Method == "POST" && span.Path == "/api/sync/inbox"))
                        .ToList();
                    return flushed.Count >= 4;
                },
                TimeSpan.FromSeconds(30),
                "the phone's flush to reach the dashboard's telemetry");
            log.AppendLine("Sent by the phone after sync came back:");
            foreach (var span in flushed) log.AppendLine($"  {span}");
            Assert.Equal(
                ["PUT attachment", "PUT attachment", "POST inbox", "POST inbox"],
                flushed.Select(span => $"{span.Method} {(span.Path == "/api/sync/inbox" ? "inbox" : "attachment")}").ToArray());

            // 5. The desktop has the whole note, and routes it to Tasks with its files.
            log.AppendLine($"desktop sync: {await desktop.SyncNowAsync()}");
            await desktop.ShowInboxAsync();
            var item = desktop.InboxItem(note);
            await Expect(item).ToBeVisibleAsync();
            await Expect(item.GetByTestId("inbox-pane-item-person")).ToContainTextAsync("ada");
            await Expect(item.GetByTestId("inbox-pane-item-tag")).ToContainTextAsync(["conference", "e2e"]);
            await desktop.OpenInboxItemAsync(note);
            await Expect(desktop.Page.GetByTestId("inbox-detail-attachment-image")).ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(desktop.Page.GetByTestId("inbox-detail-files")).ToContainTextAsync("handout.pdf");
            await evidence.ScreenshotAsync(desktop.Page, "desktop-talk-note-detail");

            await desktop.Page.GetByTestId("inbox-move-to-backlog").ClickAsync();
            await Expect(desktop.Page.GetByTestId("inbox-detail-state")).ToContainTextAsync("Routed", new() { Timeout = 30_000 });
            await evidence.ScreenshotAsync(desktop.Page, "desktop-talk-note-routed");

            await desktop.ShowTasksAsync();
            await desktop.OpenTaskAsync(note);
            await Expect(desktop.Page.GetByTestId("entry-action-files")).ToContainTextAsync("Folder");
            await Expect(desktop.Page.GetByTestId("entry-action-myday-set")).ToHaveAttributeAsync("aria-pressed", "false");
            await evidence.ScreenshotAsync(desktop.Page, "desktop-routed-task-with-attachments");

            // 6. The phone drops the routed note, and the task is not in its My Day
            //    until the desktop picks it for today.
            log.AppendLine($"desktop sync: {await desktop.SyncNowAsync()}");
            await phone.GoToAsync("inbox", "Inbox");
            await Interactive.EventuallyAsync(
                async () =>
                {
                    await phone.RefreshInboxAsync();
                    await Task.Delay(1_000);
                    return await phone.InboxRow(note).CountAsync() == 0;
                },
                Flush,
                "the phone Inbox to drop the routed note");
            await evidence.ScreenshotAsync(phone.Page, "phone-inbox-dropped-note");

            await phone.GoToAsync("tasks", "My Day");
            await phone.RefreshMyDayAsync();
            await Task.Delay(2_000, cancellationToken);
            await Expect(phone.TaskRow(note)).ToHaveCountAsync(0);
            await evidence.ScreenshotAsync(phone.Page, "phone-my-day-without-task");

            await desktop.ShowTasksAsync();
            await desktop.OpenTaskAsync(note);
            var myDay = desktop.Page.GetByTestId("entry-action-myday-set");
            await Interactive.RepeatAsync(
                () => myDay.ClickAsync(),
                async () => await myDay.GetAttributeAsync("aria-pressed") == "true",
                "adding the routed task to My Day");
            await evidence.ScreenshotAsync(desktop.Page, "desktop-task-added-to-my-day");
            log.AppendLine($"desktop sync: {await desktop.SyncNowAsync()}");

            await phone.Page.BringToFrontAsync();
            await Interactive.EventuallyAsync(
                async () =>
                {
                    await phone.RefreshMyDayAsync();
                    await Task.Delay(1_000);
                    return await phone.TaskRow(note).CountAsync() == 1;
                },
                Flush,
                "the routed task to reach the phone's My Day");
            await evidence.ScreenshotAsync(phone.Page, "phone-my-day-with-task");

            // 7. Offline again: a task added from My Day shows at once and waits,
            //    then reaches the desktop picked for today.
            await appHost.StopAsync(Sync, cancellationToken);
            log.AppendLine($"{DateTimeOffset.UtcNow:O} sync stopped");
            var added = $"Phone task {token}";
            await phone.AddTaskAsync(added);
            await Expect(phone.TaskRow(added)).ToHaveAttributeAsync("data-waiting", "true", new() { Timeout = 5_000 });
            await Expect(phone.TaskRow(added).GetByTestId("task-waiting")).ToBeVisibleAsync();

            // Proof the push was tried and failed, not merely queued a moment ago.
            await Interactive.EventuallyAsync(
                async () => await phone.SyncStateAsync() == "offline",
                TimeSpan.FromSeconds(60),
                "the phone to read offline after its push failed");
            await Expect(phone.SyncStatus).ToContainTextAsync("waiting");
            await Expect(phone.TaskRow(added)).ToHaveAttributeAsync("data-waiting", "true");
            await evidence.ScreenshotAsync(phone.Page, "phone-my-day-task-waiting");

            await appHost.StartAsync(Sync, cancellationToken);
            log.AppendLine($"{DateTimeOffset.UtcNow:O} sync started");
            await phone.ComeBackOnlineAsync();
            await WaitUntilSentAsync(phone, evidence, phone.TaskRow(added));
            await Expect(phone.TaskRow(added).GetByTestId("task-waiting")).ToHaveCountAsync(0);
            await evidence.ScreenshotAsync(phone.Page, "phone-my-day-task-synced");

            log.AppendLine($"desktop sync: {await desktop.SyncNowAsync()}");
            await desktop.ShowTasksAsync();
            await Expect(desktop.TaskRow(added)).ToBeVisibleAsync();
            await Expect(desktop.TaskRow(added).Locator(".task-item__detail--myday")).ToBeVisibleAsync();
            await desktop.OpenTaskAsync(added);
            await Expect(desktop.Page.GetByTestId("entry-action-myday-set")).ToHaveAttributeAsync("aria-pressed", "true");
            await evidence.ScreenshotAsync(desktop.Page, "desktop-phone-task-in-my-day");
        }
        finally
        {
            // Never leave the AppHost without its service, whatever step failed. A
            // failed step does not cancel the run, so its token still lets this run.
            try
            {
                await appHost.StartAsync(Sync, cancellationToken);
            }
            catch (Exception exception)
            {
                log.AppendLine($"Could not start sync again: {exception.Message}");
            }

            await evidence.WriteAsync("run.log", log.ToString());
            await evidence.WriteAsync("sync-structured-logs.json", await appHost.LogsJsonAsync(Sync, "Information", cancellationToken));
        }

        var errors = await appHost.LogsJsonAsync(Sync, "Error", cancellationToken);
        await evidence.WriteAsync("sync-errors.json", errors);
        Assert.DoesNotContain(AspireAppHost.LogIds(errors), id => !errorsBefore.Contains(id));
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

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
                        if (await row.CountAsync() != 1 || await row.GetAttributeAsync("data-waiting") != "false") return false;
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
