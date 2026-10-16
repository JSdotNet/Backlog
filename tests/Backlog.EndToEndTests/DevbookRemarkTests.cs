using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Backlog.EndToEndTests;

/// <summary>
/// A remark on an Architecture chapter in the Devbook can be written, saved and
/// deleted with ordinary clicks, at every window width the Devbook is read at.
///
/// <para>The defect this holds down: while the Devbook was a side pane, at a 1689px
/// window with the pane at its 24rem minimum the margin layout kept its 18rem
/// column, because its fallback asked the window and not the pane. The prose track
/// measured 16px, the block's corner affordance spilled over the notes cell, and
/// Playwright reported the block intercepting the pointer on the remark's Save and
/// Delete. The Devbook takes the whole screen now (local ADR 0022, amendment of
/// 2026-10-15), so its width is the window's, and the widths that matter are the
/// windows'. A click without <c>Force</c> runs Playwright's actionability check,
/// which fails when another element is on top — so each click here is the
/// assertion that nothing is.</para>
///
/// <para>Remarks live in the app's database (local ADR 0011), not in the chapter, and
/// every remark a run creates is deleted again, pass or fail.</para>
/// </summary>
[Collection(DevbookRemarkCollection.Name)]
public sealed class DevbookRemarkTests
{
    private static readonly Regex Chapter = new("Introduction and Goals", RegexOptions.IgnoreCase);

    /// <summary>The block the QA finding left its remark on.</summary>
    private const int Block = 3;

    /// <summary>
    /// Opens backlog's Architecture › 01 Introduction and Goals in the Devbook,
    /// comments on block 3, types a body and saves it, checks every control the
    /// saved remark offers is the top element where it is drawn, and deletes it —
    /// Save and Delete as real clicks, no <c>Force</c>.
    /// </summary>
    [Theory(Timeout = 10 * 60 * 1000)]
    [InlineData(1689, 1277)]
    [InlineData(1152, 1277)]
    [InlineData(900, 1277)]
    public async Task A_remark_can_be_saved_and_deleted_at_this_window_width(int viewportWidth, int viewportHeight)
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("BACKLOG_E2E") == "1",
            "Browser end-to-end tests run against a running AppHost; set BACKLOG_E2E=1 to run them (tests/Backlog.EndToEndTests/README.md).");

        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = AspireAppHost.ForThisWorktree();
        await appHost.WaitHealthyAsync(DesktopHarness.Resource, TimeSpan.FromMinutes(10), cancellationToken);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = Environment.GetEnvironmentVariable("BACKLOG_E2E_HEADED") != "1",
            Channel = Environment.GetEnvironmentVariable("BACKLOG_E2E_BROWSER_CHANNEL") is { Length: > 0 } channel ? channel : null,
        });

        var desktop = await DesktopHarness.OpenAsync(
            browser, appHost, new ViewportSize { Width = viewportWidth, Height = viewportHeight }, cancellationToken);
        var evidence = Evidence.For(string.Create(CultureInfo.InvariantCulture, $"devbook-remark-{viewportWidth}px"));
        var created = new HashSet<string>(StringComparer.Ordinal);

        // The remarks the chapter carried before this run, once they have been
        // read. Null until then, so cleanup never mistakes someone else's remark
        // for one of this run's.
        IReadOnlySet<string>? before = null;

        try
        {
            await desktop.ShowAllTasksAsync();
            await desktop.ClearScopeAsync();
            await desktop.PressScopeChipAsync(desktop.RepositoryChip("backlog"));
            await desktop.ShowDevbookAsync();
            await desktop.OpenArchitectureChapterAsync(Chapter);

            TestContext.Current.SendDiagnosticMessage(
                string.Create(CultureInfo.InvariantCulture, $"viewport {viewportWidth}x{viewportHeight}, the Devbook at the window's width"));

            before = await desktop.RemarkIdsAsync();
            var comment = desktop.ArchitectureChapter.GetByTestId($"markdown-comment-{Block}");

            // Repeated only while no remark has appeared: the first press can land
            // before the circuit attaches, and every press that lands adds one.
            await Interactive.RepeatAsync(
                async () =>
                {
                    if ((await desktop.RemarkIdsAsync()).Except(before).Any()) return;
                    await comment.ClickAsync();
                },
                async () => (await desktop.RemarkIdsAsync()).Except(before).Any(),
                $"commenting on block {Block}");

            created.UnionWith((await desktop.RemarkIdsAsync()).Except(before));
            var id = Assert.Single(created);
            TestContext.Current.SendDiagnosticMessage($"layout with the draft open: {await desktop.RemarkLayoutAsync()}");

            var body = $"Remark e2e {DateTime.Now.ToString("MMdd-HHmmss", CultureInfo.InvariantCulture)}";
            await Interactive.FillAsync(desktop.Remark(id).Locator("textarea"), body);

            var save = desktop.RemarkControl(id, "save");
            await Expect(save).ToBeVisibleAsync();
            await Expect(desktop.RemarkControl(id, "cancel")).ToBeVisibleAsync();
            TestContext.Current.SendDiagnosticMessage($"Save: {await desktop.TopmostAtCentreAsync(save)}");
            TestContext.Current.SendDiagnosticMessage($"Cancel: {await desktop.TopmostAtCentreAsync(desktop.RemarkControl(id, "cancel"))}");
            await evidence.ScreenshotAsync(desktop.Page, "draft-open");

            // Cancel is only checked, not pressed: on a fresh draft it abandons it.
            await desktop.RemarkControl(id, "cancel").ClickAsync(new LocatorClickOptions { Trial = true, Timeout = 5_000 });
            await save.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });

            await Expect(desktop.Remark(id).Locator(".md-comment__body")).ToHaveTextAsync(body);
            TestContext.Current.SendDiagnosticMessage($"layout with the remark saved: {await desktop.RemarkLayoutAsync()}");
            await evidence.ScreenshotAsync(desktop.Page, "remark-saved");

            foreach (var control in new[] { "edit", "resolve", "ai-resolve" })
            {
                var button = desktop.RemarkControl(id, control);
                if (await button.CountAsync() == 0) continue;

                TestContext.Current.SendDiagnosticMessage($"{control}: {await desktop.TopmostAtCentreAsync(button)}");
                await button.ClickAsync(new LocatorClickOptions { Trial = true, Timeout = 5_000 });
            }

            var delete = desktop.RemarkControl(id, "delete");
            await Expect(delete).ToBeInViewportAsync();
            TestContext.Current.SendDiagnosticMessage($"Delete: {await desktop.TopmostAtCentreAsync(delete)}");
            await delete.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });

            await Expect(desktop.Remark(id)).ToHaveCountAsync(0);
            created.Remove(id);
            await evidence.ScreenshotAsync(desktop.Page, "remark-deleted");
        }
        finally
        {
            // Including a remark a failed step added after the ids were last
            // taken — a second press that landed late, say.
            if (before is not null)
            {
                try
                {
                    created.UnionWith((await desktop.RemarkIdsAsync()).Except(before));
                }
                catch (PlaywrightException exception)
                {
                    TestContext.Current.SendDiagnosticMessage($"Could not re-read the chapter's remarks: {exception.Message}");
                }
            }

            // Whatever a failed step left behind, taken away the way it was put
            // there — through the remark's own Delete. Dispatched rather than
            // clicked: even a forced click is delivered at the element's centre,
            // so a run that failed because something covers Delete would land its
            // cleanup on that same cover and leak the remark.
            foreach (var id in created)
            {
                try
                {
                    var delete = desktop.RemarkControl(id, "delete");
                    var cancel = desktop.RemarkControl(id, "cancel");
                    if (await delete.CountAsync() > 0) await delete.DispatchEventAsync("click");
                    else if (await cancel.CountAsync() > 0) await cancel.DispatchEventAsync("click");
                    await Expect(desktop.Remark(id)).ToHaveCountAsync(0);
                }
                catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
                {
                    TestContext.Current.SendDiagnosticMessage(
                        $"Could not remove remark {id}: {exception.Message}. Delete it on Architecture › 01 Introduction and Goals by hand.");
                }
            }
        }
    }
}

/// <summary>The remark tests set the shared repository scope and open the Devbook,
/// so they run one at a time and never beside another test class.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DevbookRemarkCollection
{
    public const string Name = "Devbook remarks on the desktop harness";
}
