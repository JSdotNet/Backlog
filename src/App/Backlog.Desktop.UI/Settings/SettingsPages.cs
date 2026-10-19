using Backlog.SharedKernel;
using Backlog.UI.Components.Settings;

// The component's published namespace, not the folder's: see the note at the top
// of Settings.razor for why the two must differ.
namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The Settings screen's own pages, described the way a module describes the pages
/// it registers, and the menu order over both.
/// <para>
/// The app-wide pages are drawn by the screen itself, because each takes the
/// screen's shared context, but they are listed here as <see cref="SettingsSection"/>
/// records so the menu, the addresses and the settings search see one list of pages
/// rather than six built-in ones and the rest. Each declares the titles of the
/// cards it draws, which is what the search matches without rendering a page; a
/// change to a page's cards changes its list here in the same change. A page
/// whose cards depend on a feature declares them through
/// <see cref="BuiltInAsDrawn"/>, so the screen offers only the cards it currently draws.
/// </para>
/// </summary>
public static class SettingsPages
{
    public const string Features = "features";

    public const string Ai = "ai";

    public const string Storage = "storage";

    public const string Accounts = "accounts";

    public const string Repositories = "repositories";

    /// <summary>This machine's own MCP server: whether it listens, where, its port
    /// and its token. Offered whatever any feature says - the server is always on
    /// while the app runs, and a person whose client is refused needs this page
    /// most when nothing else is switched on.</summary>
    public const string McpServer = "mcp-server";

    /// <summary>The app-wide pages, in the order each group lists them. The
    /// <see cref="SettingsSection.Order"/> here is only the page's place among the
    /// built-in pages of its group; built-in pages always precede registered ones.</summary>
    public static IReadOnlyList<SettingsSection> BuiltIn { get; } =
    [
        new(Features, "Features", 0, typeof(FeaturesSettingsPage),
            Group: SettingsGroup.Workspace,
            CardTitles: [.. AppFeatures.Sections.Select(section => section.Title)]),
        new(Storage, "Storage & backup", 1, typeof(StorageSettingsPage),
            Group: SettingsGroup.Workspace,
            CardTitles: ["Where the backlog is stored", "Branch snapshot folder", "Backup", "Brain"]),
        new(Repositories, "Repositories", 2, typeof(RepositoriesSettingsPage),
            Group: SettingsGroup.Workspace,
            CardTitles: ["Repositories"]),
        new(Accounts, "GitHub accounts", 3, typeof(GitHubAccountsSettingsPage),
            Group: SettingsGroup.Workspace,
            CardTitles: ["GitHub accounts", "Signed in with the GitHub CLI", "Add an account by login"]),
        new(McpServer, "MCP server", 4, typeof(McpServerSettingsPage),
            Group: SettingsGroup.Workspace,
            CardTitles: [McpServerSettingsPage.EndpointCardTitle, McpServerSettingsPage.TokenCardTitle]),
        new(Ai, "AI providers", 0, typeof(AiSettingsPage),
            Group: SettingsGroup.Ai,
            CardTitles: AiCardTitles(usageMetrics: true, aiAssistant: true)),
    ];

    /// <summary>
    /// The app-wide pages as the screen offers them while <paramref name="isEnabled"/>
    /// says which features are on: <see cref="BuiltIn"/>, with each page declaring
    /// only the cards it draws under those features. The search and a card's address
    /// read these, so a card a switched-off feature leaves undrawn is never offered
    /// as a link to nothing. Only the AI page's cards depend on a feature.
    /// </summary>
    public static IReadOnlyList<SettingsSection> BuiltInAsDrawn(Func<string, bool> isEnabled) =>
    [
        .. BuiltIn.Select(page => page.Id == Ai
            ? page with { CardTitles = AiCardTitles(isEnabled(AppFeatures.UsageMetrics), isEnabled(AppFeatures.AiAssistant)) }
            : page)
    ];

    /// <summary>The cards the AI page draws, in its order: the two usage APIs while
    /// usage metrics are on, Azure Foundry while the AI assistant is. <see cref="BuiltIn"/>
    /// lists all three - every card the page can draw.</summary>
    public static IReadOnlyList<string> AiCardTitles(bool usageMetrics, bool aiAssistant) =>
    [
        .. usageMetrics ? (string[])["Claude usage API", "GitHub usage API"] : [],
        .. aiAssistant ? (string[])["Azure Foundry AI"] : [],
    ];

    /// <summary>Whether <paramref name="id"/> names one of the app-wide pages, which
    /// win over a registered section that takes the same id.</summary>
    public static bool IsBuiltIn(string id) =>
        BuiltIn.Any(page => string.Equals(page.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>A page's address, relative to the app's base.</summary>
    public static string Href(string id) => "settings/" + id;

    /// <summary>The group's label in the menu and in the eyebrow.</summary>
    public static string GroupLabel(SettingsGroup group) => group switch
    {
        SettingsGroup.Workspace => "Workspace",
        SettingsGroup.Ai => "AI",
        SettingsGroup.ProductAreas => "Product areas",
        _ => group.ToString(),
    };

    /// <summary>The eyebrow above a page's headline: <c>Settings · AI</c>.</summary>
    public static string Eyebrow(SettingsGroup group) => $"Settings · {GroupLabel(group)}";

    /// <summary>
    /// Every page the menu lists, in menu order: by group, and within a group the
    /// app-wide pages first, in <paramref name="builtIn"/>'s order, then the
    /// registered sections by their <see cref="SettingsSection.Order"/>. A
    /// registered section that takes an app-wide page's id is left out.
    /// </summary>
    public static IReadOnlyList<SettingsSection> InMenuOrder(
        IEnumerable<SettingsSection> builtIn,
        IEnumerable<SettingsSection> registered)
    {
        var builtInPages = builtIn.ToList();
        var registeredPages = registered
            .Where(section => !IsBuiltIn(section.Id))
            .OrderBy(section => section.Order)
            .ThenBy(section => section.Id, StringComparer.Ordinal)
            .ToList();

        return
        [
            .. Enum.GetValues<SettingsGroup>().SelectMany(group =>
                builtInPages.Where(page => page.Group == group)
                    .Concat(registeredPages.Where(page => page.Group == group)))
        ];
    }

    /// <summary>The menu's groups over <paramref name="pages"/>, already in menu
    /// order, with <paramref name="currentId"/> marked as the page on screen and
    /// each page carrying the value <paramref name="badge"/> gives it, if any. A
    /// group with no page offered is left out.</summary>
    public static IReadOnlyList<(SettingsGroup Group, IReadOnlyList<SettingsMenuItem> Items)> Menu(
        IReadOnlyList<SettingsSection> pages,
        string currentId,
        Func<SettingsSection, string?>? badge = null) =>
        [
            .. pages
                .GroupBy(page => page.Group)
                .Select(group => (group.Key, (IReadOnlyList<SettingsMenuItem>)[
                    .. group.Select(page => new SettingsMenuItem(
                        page.Id,
                        page.Title,
                        Href(page.Id),
                        string.Equals(page.Id, currentId, StringComparison.Ordinal),
                        badge?.Invoke(page)))]))
        ];

    /// <summary>
    /// The pages a settings search keeps, in the order given: every page whose
    /// title or one of whose declared card titles holds <paramref name="text"/>,
    /// ignoring case, each with the card titles that hold it. Only titles are
    /// searched - never a field's label or what a page draws - so the search
    /// answers without rendering a page. Blank text keeps every page, with no
    /// cards. A page <paramref name="switchedOff"/> names is matched on its title
    /// alone: its feature is off, so it draws a note in place of its cards.
    /// </summary>
    public static IReadOnlyList<SettingsSearchMatch> Find(
        IReadOnlyList<SettingsSection> pages,
        string? text,
        Func<SettingsSection, bool>? switchedOff = null)
    {
        var query = text?.Trim();
        if (string.IsNullOrEmpty(query)) return [.. pages.Select(page => new SettingsSearchMatch(page, []))];

        bool Holds(string title) => title.Contains(query, StringComparison.OrdinalIgnoreCase);

        return
        [
            .. pages
                .Select(page => new SettingsSearchMatch(page, switchedOff?.Invoke(page) == true ? [] : [.. (page.CardTitles ?? []).Where(Holds)]))
                .Where(match => match.Cards.Count > 0 || Holds(match.Page.Title))
        ];
    }

    /// <summary>Whether <paramref name="text"/> is a search at all: blank is none.</summary>
    public static bool IsSearching(string? text) => !string.IsNullOrWhiteSpace(text);

    /// <summary>What the menu says in its place when a search keeps no page.</summary>
    public static string NoMatch(string text) => $"No setting matches \"{text.Trim()}\".";

    private const string CardFragmentPrefix = "card-";

    /// <summary>The fragment that names a card in a page's address: <c>card-</c>
    /// and the title in lower case, every run of other characters a hyphen -
    /// <c>card-storage-backup</c>.</summary>
    public static string CardFragment(string cardTitle)
    {
        var slug = new System.Text.StringBuilder(cardTitle.Length);
        foreach (var character in cardTitle.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character)) slug.Append(character);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }

        return CardFragmentPrefix + slug.ToString().TrimEnd('-');
    }

    /// <summary>A card's address: its page's, with the card named in the fragment.</summary>
    public static string CardHref(string pageId, string cardTitle) => Href(pageId) + "#" + CardFragment(cardTitle);

    /// <summary>The card of <paramref name="page"/> that <paramref name="fragment"/>
    /// names, or null where it names none of the cards the page declares.</summary>
    public static string? CardNamedBy(SettingsSection page, string? fragment)
    {
        var name = fragment?.TrimStart('#');
        if (string.IsNullOrEmpty(name) || !name.StartsWith(CardFragmentPrefix, StringComparison.Ordinal)) return null;

        return (page.CardTitles ?? []).FirstOrDefault(title => string.Equals(CardFragment(title), name, StringComparison.Ordinal));
    }
}

/// <summary>A page a settings search kept, and the titles of its cards that
/// matched - empty where only the page's own title did.</summary>
public sealed record SettingsSearchMatch(SettingsSection Page, IReadOnlyList<string> Cards);
