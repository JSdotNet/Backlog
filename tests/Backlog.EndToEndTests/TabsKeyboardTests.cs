using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Backlog.EndToEndTests;

/// <summary>
/// The shared Tabs strip under a real keyboard, on the storybook's Layout page
/// (<c>ui-storybook</c>, <c>/layout</c>). Both faults are about timing or layout,
/// which a render test cannot see: bUnit has no focus to lag behind and no box to
/// scroll.
///
/// <para>It writes nothing and needs only the storybook, so it runs beside the
/// harness tests rather than in their collection.</para>
/// </summary>
public sealed class TabsKeyboardTests
{
    private const string Resource = "ui-storybook";

    /// <summary>
    /// Two ArrowLefts pressed back to back from History land on Read. The second
    /// arrives before the first move's focus has, and used to be taken from the tab
    /// it was dispatched on — landing on Edit twice. Five rounds, because the QA run
    /// that found it saw it five times out of five and a single pass could get lucky.
    /// </summary>
    [Fact(Timeout = 5 * 60 * 1000)]
    public async Task Two_fast_arrows_move_two_tabs()
    {
        SkipUnlessEnabled();

        var cancellationToken = TestContext.Current.CancellationToken;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchAsync(playwright);
        var (page, evidence) = await OpenLayoutAsync(browser, new ViewportSize { Width = 1440, Height = 900 }, "tabs-fast-arrows", cancellationToken);

        var strip = page.GetByTestId("demo-tabs");
        var history = strip.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "History" });
        var read = strip.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "Read" });
        var state = page.GetByTestId("tabs-state");

        for (var round = 1; round <= 5; round++)
        {
            // The first round's click may land on the prerendered page, which
            // ignores it; it is pressed again until the strip answers.
            await Interactive.ClickAsync(history, state.Filter(new LocatorFilterOptions { HasText = "Active tab: history" }));
            await Expect(state).ToHaveTextAsync("Active tab: history");
            await Expect(history).ToBeFocusedAsync();

            await page.Keyboard.PressAsync("ArrowLeft");
            await page.Keyboard.PressAsync("ArrowLeft");

            await Expect(state).ToHaveTextAsync("Active tab: read");
            await Expect(read).ToBeFocusedAsync();
        }

        await evidence.ScreenshotAsync(page, "two-arrows-land-on-read");
    }

    /// <summary>
    /// At phone width a strip narrower than its five tabs shows each tab the
    /// keyboard moves to whole: the arrows one at a time to the last, then Home and
    /// End. Each arrow used to scroll the strip 40 px — the key's own default — and
    /// leave the last tab cut off by most of its width.
    /// </summary>
    [Fact(Timeout = 5 * 60 * 1000)]
    public async Task A_tab_the_keyboard_moves_to_is_wholly_in_view()
    {
        SkipUnlessEnabled();

        var cancellationToken = TestContext.Current.CancellationToken;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchAsync(playwright);
        var (page, evidence) = await OpenLayoutAsync(browser, new ViewportSize { Width = 390, Height = 844 }, "tabs-scroll-into-view", cancellationToken);

        await WalkAsync(page, "scrolling-tabs", ["Graph", "Ratings", "Layers", "Radar", "Type lenses"]);
        await evidence.ScreenshotAsync(page, "last-tab-whole");
    }

    /// <summary>
    /// The same walk where the strip is not the tablist but the row around it — a
    /// host that puts a link beside the tabs and scrolls the row, as the dashboard
    /// and the tools pane do. The reveal looks for the nearest ancestor that
    /// scrolls sideways, so the row is scrolled rather than nothing.
    /// </summary>
    [Fact(Timeout = 5 * 60 * 1000)]
    public async Task A_tab_in_a_row_that_scrolls_around_the_strip_is_wholly_in_view()
    {
        SkipUnlessEnabled();

        var cancellationToken = TestContext.Current.CancellationToken;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchAsync(playwright);
        var (page, evidence) = await OpenLayoutAsync(browser, new ViewportSize { Width = 390, Height = 844 }, "tabs-row-scroll-into-view", cancellationToken);

        await WalkAsync(page, "scrolling-row-tabs", ["Plugins", "Skills", "Agents", "MCP servers"]);
        await evidence.ScreenshotAsync(page, "last-row-tab-whole");
    }

    /// <summary>Clicks the second tab until the strip answers, Home back to the
    /// first, then ArrowRight one tab at a time to the last, End, Home, End and
    /// ArrowLeft — every tab reached has the focus and is whole.</summary>
    private static async Task WalkAsync(IPage page, string testId, string[] titles)
    {
        var strip = page.GetByTestId(testId);
        var list = strip.GetByRole(AriaRole.Tablist);

        // The second tab, not the first: the first is already active in the
        // prerendered page, so a click lost to it would look as if it took. Done
        // first, because the circuit replaces the prerendered strip when it starts.
        var second = Tab(strip, titles[1]);
        await Interactive.RepeatAsync(
            () => second.ClickAsync(),
            async () => await second.GetAttributeAsync("aria-selected") == "true",
            $"choosing {titles[1]}");

        await list.ScrollIntoViewIfNeededAsync();
        Assert.True(
            await list.EvaluateAsync<bool>(
                """
                list => {
                    for (let element = list; element && element !== document.body; element = element.parentElement) {
                        if (element.scrollWidth > element.clientWidth) return true;
                    }
                    return false;
                }
                """),
            "the strip must be narrower than its tabs for this test to mean anything");

        await page.Keyboard.PressAsync("Home");
        await AssertWhollyInViewAsync(strip, titles[0]);

        for (var index = 1; index < titles.Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
            await AssertWhollyInViewAsync(strip, titles[index]);
        }

        await Expect(Tab(strip, titles[^1])).ToHaveAttributeAsync("aria-selected", "true");

        await page.Keyboard.PressAsync("Home");
        await AssertWhollyInViewAsync(strip, titles[0]);

        await page.Keyboard.PressAsync("End");
        await AssertWhollyInViewAsync(strip, titles[^1]);

        await page.Keyboard.PressAsync("ArrowLeft");
        await AssertWhollyInViewAsync(strip, titles[^2]);
    }

    private static ILocator Tab(ILocator strip, string title) =>
        strip.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = title, Exact = true });

    private static async Task AssertWhollyInViewAsync(ILocator strip, string title)
    {
        var tab = Tab(strip, title);
        await Expect(tab).ToBeFocusedAsync();

        // Measured once the strip has settled: the reveal runs on focusin, so by
        // the time the tab has the focus it has been scrolled for.
        var overhang = await tab.EvaluateAsync<double>(
            """
            tab => {
                let strip = tab.parentElement;
                while (strip.scrollWidth <= strip.clientWidth && strip.parentElement !== document.body) strip = strip.parentElement;
                const view = strip.getBoundingClientRect();
                const box = tab.getBoundingClientRect();
                const left = view.left + strip.clientLeft;
                const right = left + strip.clientWidth;
                return Math.max(0, left - box.left) + Math.max(0, box.right - right);
            }
            """);

        Assert.True(overhang < 1, $"'{title}' sticks {overhang:0.#} px out of the strip");
    }

    private static Task<IBrowser> LaunchAsync(IPlaywright playwright) =>
        playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = Environment.GetEnvironmentVariable("BACKLOG_E2E_HEADED") != "1",
            Channel = Environment.GetEnvironmentVariable("BACKLOG_E2E_BROWSER_CHANNEL") is { Length: > 0 } channel ? channel : null,
        });

    private static async Task<(IPage Page, Evidence Evidence)> OpenLayoutAsync(IBrowser browser, ViewportSize viewport, string scenario, CancellationToken cancellationToken)
    {
        var appHost = AspireAppHost.ForThisWorktree();
        await appHost.WaitHealthyAsync(Resource, TimeSpan.FromMinutes(10), cancellationToken);

        var context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = viewport });
        var page = await context.NewPageAsync();

        await page.GotoAsync(new Uri(await appHost.UrlAsync(Resource, cancellationToken), "layout").ToString());
        // Visible is not interactive: the page is prerendered, so each test presses
        // its first tab through Interactive until the strip answers.
        await Expect(page.GetByTestId("demo-tabs").GetByRole(AriaRole.Tab).First).ToBeVisibleAsync();

        return (page, Evidence.For(scenario));
    }

    private static void SkipUnlessEnabled() =>
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("BACKLOG_E2E") == "1",
            "Browser end-to-end tests run against a running AppHost; set BACKLOG_E2E=1 to run them (tests/Backlog.EndToEndTests/README.md).");
}
