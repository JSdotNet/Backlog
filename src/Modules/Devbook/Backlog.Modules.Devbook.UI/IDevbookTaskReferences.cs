namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The Devbook references of the tasks the window's backlog files to one repository,
/// for the Domain view's features and requirements (local ADR 0044). The tasks are
/// another module's, so the Devbook asks rather than reads them, as it asks
/// <see cref="Backlog.Modules.Devbook.Abstractions.IBrainTaskTitles"/> for their
/// titles; a host that registers none shows no task on any page.
/// </summary>
public interface IDevbookTaskReferences
{
    /// <summary>Every task filed to <paramref name="repositoryAlias"/> that carries a
    /// Devbook reference, with those references.</summary>
    IReadOnlyList<DevbookTaskReferences> For(string repositoryAlias);
}
