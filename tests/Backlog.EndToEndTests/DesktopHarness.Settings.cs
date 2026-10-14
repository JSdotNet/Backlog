using Microsoft.Playwright;

namespace Backlog.EndToEndTests;

/// <summary>The Settings screen's frame, menu and search, as <see cref="SettingsTests"/>
/// drives them.</summary>
internal sealed partial class DesktopHarness
{
    /// <summary>The settings menu.</summary>
    public ILocator SettingsMenu => Page.GetByTestId("settings-menu");

    /// <summary>A page's link in the menu, found by the page id it carries: its name
    /// also holds the page's badge.</summary>
    public ILocator SettingsMenuLink(string page) => SettingsMenu.Locator($"a[data-page='{page}']");

    /// <summary>The page on screen: the one settings region that is not hidden.</summary>
    public ILocator ShownSettingsPage => Page.Locator(".settings-frame__content > section.setting:not([hidden])");

    /// <summary>The address relative to the harness's base: <c>settings/storage#card-backup</c>.</summary>
    public string Address => Page.Url.StartsWith(_baseUrl.ToString(), StringComparison.Ordinal)
        ? Page.Url[_baseUrl.ToString().Length..]
        : Page.Url;

    /// <summary>Opens the screen at <c>settings</c>, the address that names no page.</summary>
    public async Task OpenSettingsIndexAsync()
    {
        await Page.GotoAsync(new Uri(_baseUrl, "settings").ToString());
        await Page.GetByTestId("settings-frame").WaitForAsync();
    }

    /// <summary>Chooses a page from the menu and waits for the menu to mark it the
    /// current one. The link is pressed again only while the page has not changed:
    /// a press on the prerendered page, before the circuit attaches, may go nowhere.</summary>
    public Task ChooseSettingsPageAsync(string page) =>
        Interactive.RepeatAsync(
            async () =>
            {
                if (await SettingsMenuLink(page).GetAttributeAsync("aria-current") != "page") await SettingsMenuLink(page).ClickAsync();
            },
            async () => await SettingsMenuLink(page).GetAttributeAsync("aria-current") == "page",
            $"choosing {page} in the settings menu");

    /// <summary>Waits for the menu to mark <paramref name="page"/> as the one on screen.</summary>
    public Task SettingsPageIsShownAsync(string page) =>
        Interactive.EventuallyAsync(
            async () => await SettingsMenu.Locator($"a[data-page='{page}'][aria-current='page']").CountAsync() > 0,
            TimeSpan.FromSeconds(30),
            $"the {page} settings page to be the current one");

    /// <summary>Types into Find a setting until the menu lists <paramref name="card"/>
    /// under its page, and answers that card's link.</summary>
    public async Task<ILocator> FindSettingAsync(string text, string card)
    {
        var link = SettingsMenu.GetByTestId("settings-menu-card").Filter(new LocatorFilterOptions { HasTextString = card });
        await Interactive.FillAsync(Page.GetByTestId("settings-search-input"), text, async () => await link.CountAsync() > 0);
        return link;
    }

    /// <summary>
    /// Starts listening for the light a revealed card wears. It is lit for 1.6 s, so
    /// rather than race a poll against it the page records the title of every card
    /// that takes the <c>settings-card-found</c> class, and
    /// <see cref="LitSettingsCardsAsync"/> reads them back.
    /// </summary>
    public Task ListenForLitSettingsCardsAsync() =>
        Page.EvaluateAsync(
            """
            () => {
                window.__litSettingsCards = [];
                new MutationObserver(records => {
                    for (const record of records) {
                        const card = record.target;
                        if (!(card instanceof Element) || !card.classList.contains('settings-card-found')) continue;
                        const heading = card.querySelector('h2, h3, h4, h5, h6');
                        window.__litSettingsCards.push(heading ? heading.textContent.trim() : '');
                    }
                }).observe(document.body, { subtree: true, attributes: true, attributeFilter: ['class'] });
            }
            """);

    public async Task<IReadOnlyList<string>> LitSettingsCardsAsync() =>
        await Page.EvaluateAsync<string[]>("() => window.__litSettingsCards ?? []");

    /// <summary>Whether the card whose heading reads <paramref name="title"/> on the
    /// page on screen has its top inside the window.</summary>
    public Task<bool> SettingsCardIsInViewAsync(string title) =>
        Page.EvaluateAsync<bool>(
            """
            title => {
                const page = document.querySelector('.settings-frame__content > section.setting:not([hidden])');
                const heading = [...(page?.querySelectorAll('h2, h3, h4, h5, h6') ?? [])].find(h => h.textContent.trim() === title);
                if (!heading) return false;
                const top = heading.getBoundingClientRect().top;
                return top >= 0 && top < window.innerHeight;
            }
            """,
            title);
}
