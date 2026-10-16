using Backlog.SharedKernel;
using Backlog.UI.Components.Selects;

namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// What the roadmap's repository pickers offer (local ADR 0029, §3): every active
/// repository; an archived one only while show archived is on, and then named so; and
/// one the work or date is already filed under, named so as well, so the picker never
/// holds a value it does not show and a Save never drops it.
/// </summary>
public static class PlannedRepositoryOptions
{
    /// <summary>The options, in Settings order.</summary>
    /// <param name="repositories">The configured repositories.</param>
    /// <param name="showArchived">Whether this device shows archived repositories.</param>
    /// <param name="filedUnder">The aliases the edited work is stored under, kept
    /// offered whatever the switch says.</param>
    public static IReadOnlyList<SelectorOption> For(
        IEnumerable<PlannedRepository> repositories,
        bool showArchived,
        IEnumerable<string> filedUnder)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(filedUnder);

        var kept = new HashSet<string>(filedUnder, StringComparer.OrdinalIgnoreCase);

        return
        [
            .. repositories
                .Where(repository => !repository.Archived || showArchived || kept.Contains(repository.Alias))
                .Select(repository => new SelectorOption(
                    repository.Alias,
                    ArchivedRepositoryLabel.Of(repository.Title, repository.Archived)))
        ];
    }
}
