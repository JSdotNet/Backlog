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
/// <param name="Id">The page's id in the settings tab strip. Stable and
/// lower-case, and distinct from the shell's own page ids — <c>features</c>,
/// <c>ai</c>, <c>storage</c>, <c>accounts</c> and <c>repositories</c> — which
/// win a collision.</param>
/// <param name="Title">The tab's label and the page's heading.</param>
/// <param name="Order">Where the page sits among the registered sections, lowest
/// first. Registered sections always follow the shell's own pages.</param>
/// <param name="Component">The Razor component the page renders, with no
/// parameters: it takes what it needs from the container. One instance lives
/// from the page's first opening until the settings screen closes, so its state
/// survives a switch to another tab. Switching the section's feature off takes
/// the page off the strip and disposes it, so switching it back on starts a new
/// instance.</param>
/// <param name="FeatureKey">A feature the page is offered behind, or null for a
/// page that is always offered. Checked by the shell, so a switched-off feature
/// has no empty page left on the strip.</param>
public sealed record SettingsSection(
    string Id,
    string Title,
    int Order,
    Type Component,
    string? FeatureKey = null);
