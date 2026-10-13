namespace Backlog.SharedKernel;

/// <summary>
/// A block another project adds to every repository's card on the Repositories
/// settings page.
/// <para>
/// The page is the Tasks module's, and a repository's card carries settings that
/// are not Tasks' to draw: where its devbook is read from and which of its
/// folders, which belong to Devbook, a context the Tasks UI may not reach. The
/// project that may — the shell — registers a part, and the card renders
/// <paramref name="Component"/> once per repository, in ascending
/// <paramref name="Order"/>, beside its own settings. Here beside
/// <see cref="SettingsSection"/> and for the same reason: it is a contract between
/// the page and its contributors, and both can already see the shared kernel.
/// </para>
/// </summary>
/// <param name="Id">The part's id, stable and lower-case; it keys the part on the
/// card.</param>
/// <param name="Order">Where the part sits among the registered parts, lowest
/// first.</param>
/// <param name="Component">The Razor component the card renders. It takes one
/// parameter, a string named <see cref="RepositoryAliasParameter"/> carrying the
/// alias of the repository whose card it is on, and the rest from the container.
/// Its root element sits in the card's column grid as a column of its own.</param>
public sealed record RepositorySettingsPart(string Id, int Order, Type Component)
{
    /// <summary>The name of the parameter every part's component takes: the alias
    /// of the repository the card is for.</summary>
    public const string RepositoryAliasParameter = "RepositoryAlias";
}
