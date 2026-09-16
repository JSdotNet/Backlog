using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Answers "what knowledge is there to read for this scope?" from a repository
/// alias alone.
/// <para>
/// The shell needs that answer to decide whether the Devbook option is worth
/// offering at all, and it should not have to know that the answer comes from
/// repository devbook-folder settings. Asking Devbook a question is the
/// boundary; computing it in the shell was the leak.
/// </para>
/// </summary>
public sealed class DevbookScope(GitHubSettingsStore repositories, IDevbookFolderSource folders, IAppFeatureSettings features)
{
    /// <summary>The repository the alias names, or null when nothing is scoped.
    /// An alias that no longer matches a configured repository is the same
    /// answer as no alias at all.</summary>
    public GitHubRepositoryRef? Repository(string? repositoryAlias) =>
        string.IsNullOrWhiteSpace(repositoryAlias) ? null : repositories.Current.Find(repositoryAlias);

    /// <summary>The knowledge folders in play: the repository's when one is
    /// scoped, none otherwise — a devbook belongs to a repository. The port
    /// answers, so this context never learns where a snapshot is kept.</summary>
    public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) =>
        folders.Folders(repositoryAlias);

    /// <summary>The sections that actually have something behind them, or none
    /// at all while Devbook sections are turned off — or while nothing is
    /// scoped. The second case is asked here rather than left to the catalog,
    /// because the catalog normalises whatever it is handed against the
    /// defaults, and "no folders" would come back as "every folder".</summary>
    public IReadOnlyList<DevbookArea> VisibleAreas(string? repositoryAlias)
    {
        if (!features.IsEnabled(DevbookFeatures.DevbookSections)) return [];

        var folders = Folders(repositoryAlias);
        return folders.Count == 0 ? [] : DevbookAreaCatalog.VisibleAreas(folders);
    }

    /// <summary>
    /// Whether the pane offers to search this devbook.
    /// <para>
    /// Asked here rather than by the pane reading the feature settings itself, for
    /// the reason this whole class exists: the pane's question is "is there
    /// searching to offer", and whether that is answered by a flag, by a folder
    /// setting, or by both is Devbook's business and not the strip's. It is
    /// also only half of what the reader ends up seeing — a repository whose index
    /// has never been generated has the surface and no index, which the surface
    /// itself says in words.
    /// </para>
    /// </summary>
    public bool SearchOffered => features.IsEnabled(DevbookFeatures.Search);
}
