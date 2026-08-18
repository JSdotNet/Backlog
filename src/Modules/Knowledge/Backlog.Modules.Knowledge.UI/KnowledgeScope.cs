using Backlog.Modules.Knowledge.Abstractions;
using Backlog.SharedKernel;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// Answers "what knowledge is there to read for this scope?" from a repository
/// alias alone.
/// <para>
/// The shell needs that answer to decide whether the Knowledge option is worth
/// offering at all, and it should not have to know that the answer comes from
/// repository knowledge-folder settings falling back to the storage folder.
/// Asking Second Brain a question is the boundary; computing it in the shell was
/// the leak.
/// </para>
/// </summary>
public sealed class KnowledgeScope(GitHubSettingsStore repositories, IKnowledgeFolderSource folders, IAppFeatureSettings features)
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
    public IReadOnlyList<KnowledgeFolderSetting> Folders(string? repositoryAlias) =>
        folders.Folders(repositoryAlias);

    /// <summary>The sections that actually have something behind them, or none
    /// at all while knowledge sections are turned off.</summary>
    public IReadOnlyList<KnowledgeArea> VisibleAreas(string? repositoryAlias) =>
        features.IsEnabled(KnowledgeFeatures.KnowledgeSections)
            ? KnowledgeAreaCatalog.VisibleAreas(Folders(repositoryAlias))
            : [];
}
