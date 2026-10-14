using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Backlog.EndToEndTests;

/// <summary>
/// The Settings screen on the desktop harness, end to end: a page opened by its
/// address and by the menu, Back to the page before, Find a setting to a card that
/// is brought into view and lit, and the grouped list and the page alone at phone
/// width. Listed under the <c>tests</c> of the chapters of
/// <c>.devbook/design/settings-pages.md</c> it proves.
///
/// <para>It reads the pages every host offers — Storage &amp; backup and
/// Repositories — and writes nothing: the search text lives only in the page. It
/// runs in the Brain collection because a Brain test restarts
/// <c>desktop-web-harness</c>, which would cut this one's circuit.</para>
/// </summary>
[Collection(BrainCollection.Name)]
public sealed class SettingsTests
{
    [Fact(Timeout = 10 * 60 * 1000)]
    public async Task A_page_opens_by_address_and_menu_back_returns_find_reveals_a_card_and_a_phone_gets_the_list()
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
        var desktop = await DesktopHarness.OpenAsync(browser, appHost, cancellationToken);
        var evidence = Evidence.For("settings");
        var page = desktop.Page;

        // 1. By address: settings/repositories opens Repositories beside the menu,
        //    under the eyebrow of its group.
        await desktop.OpenSettingsAsync("repositories");
        Assert.Equal("settings/repositories", desktop.Address);
        await Expect(page.GetByTestId("settings-eyebrow")).ToHaveTextAsync("Settings · Workspace");
        await Expect(desktop.ShownSettingsPage).ToHaveAttributeAsync("aria-label", "Repository settings");
        await Expect(desktop.SettingsMenu).ToBeVisibleAsync();
        await evidence.ScreenshotAsync(page, "repositories-by-address");

        // 2. By the menu: a link, so choosing Storage & backup moves the address.
        Assert.Null(await desktop.SettingsMenuLink("storage").GetAttributeAsync("aria-current"));
        Assert.Equal("settings/storage", await desktop.SettingsMenuLink("storage").GetAttributeAsync("href"));
        await desktop.ChooseSettingsPageAsync("storage");
        await Interactive.EventuallyAsync(
            () => Task.FromResult(desktop.Address == "settings/storage"),
            TimeSpan.FromSeconds(15),
            "the address to name the storage page");
        await Expect(desktop.ShownSettingsPage).ToHaveAttributeAsync("aria-label", "Storage settings");
        await Expect(desktop.SettingsMenuLink("repositories")).Not.ToHaveAttributeAsync("aria-current", "page");
        await evidence.ScreenshotAsync(page, "storage-by-menu");

        // 3. Back returns to the page before.
        await page.GoBackAsync();
        await desktop.SettingsPageIsShownAsync("repositories");
        Assert.Equal("settings/repositories", desktop.Address);
        await Expect(desktop.ShownSettingsPage).ToHaveAttributeAsync("aria-label", "Repository settings");
        await evidence.ScreenshotAsync(page, "back-to-repositories");

        // 4. Find a setting lists the Backup card under its page, as a link to the
        //    card's address. Following it opens Storage & backup with the card in
        //    view and its border lit.
        var backup = await desktop.FindSettingAsync("backup", "Backup");
        await Expect(backup).ToHaveAttributeAsync("href", "settings/storage#card-backup");
        await Expect(desktop.SettingsMenuLink("storage")).ToBeVisibleAsync();
        await Expect(desktop.SettingsMenuLink("repositories")).ToHaveCountAsync(0);
        await evidence.ScreenshotAsync(page, "find-a-setting-lists-the-card");

        await desktop.ListenForLitSettingsCardsAsync();
        await backup.ClickAsync();
        await desktop.SettingsPageIsShownAsync("storage");
        Assert.Equal("settings/storage#card-backup", desktop.Address);
        await Interactive.EventuallyAsync(
            async () => (await desktop.LitSettingsCardsAsync()).Contains("Backup"),
            TimeSpan.FromSeconds(10),
            "the Backup card to be lit");
        await evidence.ScreenshotAsync(page, "backup-card-lit");
        Assert.Equal(["Backup"], (await desktop.LitSettingsCardsAsync()).Distinct());
        await Interactive.EventuallyAsync(
            () => desktop.SettingsCardIsInViewAsync("Backup"),
            TimeSpan.FromSeconds(10),
            "the Backup card to be scrolled into view");

        // 5. At phone width settings is the grouped list alone, with the search
        //    above it, and a row opens its page alone with the way back to the list.
        await page.SetViewportSizeAsync(390, 844);
        await desktop.OpenSettingsIndexAsync();
        await Expect(page.GetByTestId("settings-frame")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bsettings-frame--index\b"));
        await Expect(desktop.SettingsMenu).ToBeVisibleAsync();
        await Expect(page.Locator(".settings-frame__content")).ToBeHiddenAsync();
        await Expect(page.GetByTestId("settings-search-input")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("settings-menu-workspace")).ToBeVisibleAsync();
        await Expect(desktop.SettingsMenuLink("storage").Locator(".settings-menu-group__chevron")).ToBeVisibleAsync();
        var rowHeight = await desktop.SettingsMenuLink("storage").EvaluateAsync<double>("link => link.getBoundingClientRect().height");
        Assert.True(rowHeight >= 52, $"A row of the grouped list is {rowHeight} px tall, under 52.");
        await evidence.ScreenshotAsync(page, "phone-grouped-list");

        await Interactive.ClickAsync(desktop.SettingsMenuLink("storage"), page.GetByTestId("back-to-menu-link"));
        Assert.Equal("settings/storage", desktop.Address);
        await Expect(desktop.ShownSettingsPage).ToHaveAttributeAsync("aria-label", "Storage settings");
        await Expect(desktop.SettingsMenu).ToBeHiddenAsync();
        await Expect(page.GetByTestId("settings-search-slot")).ToBeHiddenAsync();
        await Expect(page.GetByTestId("back-link")).ToBeHiddenAsync();
        await Expect(page.GetByTestId("back-to-menu-link")).ToHaveAttributeAsync("aria-label", "Back to all settings");
        await evidence.ScreenshotAsync(page, "phone-page-alone");

        await Interactive.ClickAsync(page.GetByTestId("back-to-menu-link"), desktop.SettingsMenu);
        Assert.Equal("settings", desktop.Address);
        await Expect(page.Locator(".settings-frame__content")).ToBeHiddenAsync();
        await evidence.ScreenshotAsync(page, "phone-back-to-the-list");
    }
}
