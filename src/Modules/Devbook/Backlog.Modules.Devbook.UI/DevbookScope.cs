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
/// repository devbook-folder settings falling back to the storage folder.
/// Asking Devbook a question is the boundary; computing it in the shell was
/// the leak.
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
    /// scoped, the storage folder's otherwise. Which of the two it is, and where
    /// the storage folder is, is the port's business — this context never learns
    /// that half the answer comes from where the backlog is kept.</summary>
    public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) =>
        folders.Folders(repositoryAlias);

    /// <summary>The sections that actually have something behind them, or none
    /// at all while knowledge sections are turned off.</summary>
    public IReadOnlyList<DevbookArea> VisibleAreas(string? repositoryAlias) =>
        features.IsEnabled(DevbookFeatures.DevbookSections)
            ? DevbookAreaCatalog.VisibleAreas(Folders(repositoryAlias))
            : [];

    /// <summary>
    /// Whether the pane offers to search this knowledge base.
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
