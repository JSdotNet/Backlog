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
/// rather than five built-in ones and the rest. Each declares the titles of the
/// cards it draws, which is what the search matches without rendering a page; a
/// change to a page's cards changes its list here in the same change.
/// </para>
/// </summary>
public static class SettingsPages
{
    public const string Features = "features";

    public const string Ai = "ai";

    public const string Storage = "storage";

    public const string Accounts = "accounts";

    public const string Repositories = "repositories";

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
            CardTitles: ["Signed in with the GitHub CLI", "Add an account by login"]),
        new(Ai, "AI providers", 0, typeof(AiSettingsPage),
            Group: SettingsGroup.Ai,
            CardTitles: ["Claude usage API", "GitHub usage API", "Azure Foundry AI"]),
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
    /// order, with <paramref name="currentId"/> marked as the page on screen. A
    /// group with no page offered is left out.</summary>
    public static IReadOnlyList<(SettingsGroup Group, IReadOnlyList<SettingsMenuItem> Items)> Menu(
        IReadOnlyList<SettingsSection> pages,
        string currentId) =>
        [
            .. pages
                .GroupBy(page => page.Group)
                .Select(group => (group.Key, (IReadOnlyList<SettingsMenuItem>)[
                    .. group.Select(page => new SettingsMenuItem(
                        page.Id,
                        page.Title,
                        Href(page.Id),
                        string.Equals(page.Id, currentId, StringComparison.Ordinal)))]))
        ];
}
