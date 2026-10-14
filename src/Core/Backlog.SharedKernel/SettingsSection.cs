namespace Backlog.SharedKernel;

/// <summary>
/// A page of the settings screen that a module brings rather than the shell.
/// <para>
/// The shell draws its own app-wide pages and then one page per section
/// registered in the container, in ascending <paramref name="Order"/>, each
/// rendering <paramref name="Component"/> under <paramref name="Title"/>. A module
/// registers its section from its <c>.UI</c> project's registration extension, so
/// a change to one module's settings touches that module and not the desktop
/// shell. Here rather than in the component library because it is a contract
/// between the shell and the modules, not a control: the library has nothing to
/// render for it, and every module <c>.UI</c> project and the shell can already
/// see the shared kernel.
/// </para>
/// </summary>
/// <param name="Id">The page's id: its address, <c>settings/{Id}</c>, and its
/// key in the settings menu. Stable and
/// lower-case, and distinct from the shell's own page ids — <c>features</c>,
/// <c>ai</c>, <c>storage</c>, <c>accounts</c> and <c>repositories</c> — which
/// win a collision.</param>
/// <param name="Title">The page's label in the settings menu, and its headline
/// while the page supplies no sentence of its own.</param>
/// <param name="Order">Where the page sits among the registered sections of its
/// <paramref name="Group"/>, lowest first. Within a group, registered sections
/// always follow the shell's own pages.</param>
/// <param name="Component">The Razor component the page renders, with no
/// parameters: it takes what it needs from the container. One instance lives
/// from the page's first opening until the settings screen closes, so its state
/// survives a move to another page. Switching the section's feature off takes
/// the page off the menu and disposes it, so switching it back on starts a new
/// instance.</param>
/// <param name="FeatureKey">A feature the page is offered behind, or null for a
/// page that is always offered. Checked by the shell, so a switched-off feature
/// has no empty page left in the menu.</param>
/// <param name="Group">The group of the settings menu the page is listed under,
/// and the second half of its eyebrow — <c>Settings · AI</c>.</param>
/// <param name="CardTitles">The titles of the cards the page draws, in the order
/// it draws them, or null for a page that declares none. The settings search
/// matches page and card titles from these without rendering any page, so a page
/// that adds, renames or drops a card changes this list in the same change.</param>
public sealed record SettingsSection(
    string Id,
    string Title,
    int Order,
    Type Component,
    string? FeatureKey = null,
    SettingsGroup Group = SettingsGroup.ProductAreas,
    IReadOnlyList<string>? CardTitles = null);
