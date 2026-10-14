namespace Backlog.SharedKernel;

/// <summary>
/// The groups the settings menu lists its pages under, in the order the menu
/// draws them. Within a group the shell's own pages come first, then the pages
/// modules register, by their <see cref="SettingsSection.Order"/>.
/// </summary>
public enum SettingsGroup
{
    /// <summary>What the whole app works with: its features, where it stores the
    /// backlog, the repositories and the GitHub accounts.</summary>
    Workspace,

    /// <summary>The AI providers the app reads usage and cost from, and the
    /// pages about pricing a run.</summary>
    Ai,

    /// <summary>One page per area of the product that has settings of its own.</summary>
    ProductAreas,
}
