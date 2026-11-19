using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Calendar.Abstractions.Services;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The window's repository scope and colours as the Calendar reads them (local ADR 0056
/// §2), answered from the window's own view state on the
/// <see cref="ShellDevbookWindowScope"/> precedent: the scope chips narrow the Tasks layer,
/// and the colours switch marks its chips, its hours blocks and the plan bars. The Calendar
/// sees neither the chips nor Tasks; the shell, which sees both, answers.
/// </summary>
public sealed class ShellCalendarWindowScope(TasksDesktopState state) : ICalendarWindowScope
{
    /// <summary>The window's state changed — the chips, the colours switch or show
    /// archived among the rest. Forwarded rather than relayed, so the scope holds no
    /// subscription of its own to release.</summary>
    public event Action? Changed
    {
        add => state.Changed += value;
        remove => state.Changed -= value;
    }

    public bool InScope(IReadOnlyList<string> repositoryIds) => state.RepositoriesInScope(repositoryIds);

    public int? BandFor(IReadOnlyList<string> repositories) => state.RepositoryColourForAny(repositories);
}
