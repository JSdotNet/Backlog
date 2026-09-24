using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Tasks;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Roadmap's <see cref="IRoadmapWorkChanges"/> with every local task write,
/// heard through Tasks' <see cref="ITaskChangeSignal"/>. Plan writes, the plan items
/// an import lays out included, are Roadmap's own to announce and already are.
/// <para>
/// A write the sync pull applies is suppressed at the task signal, so another
/// machine's edits are not heard here either; the band still reads them on its next
/// load.
/// </para>
/// </summary>
public sealed class RoadmapWorkChanges : IRoadmapWorkChanges, IDisposable
{
    private readonly ITaskChangeSignal? _tasks;

    public RoadmapWorkChanges(ITaskChangeSignal? tasks)
    {
        _tasks = tasks;
        if (_tasks is not null) _tasks.Changed += Raise;
    }

    public event Action? Changed;

    /// <summary>Tells the listeners the roadmap may read differently now. Public so a
    /// test can raise it without writing a task.</summary>
    public void Raise() => Changed?.Invoke();

    public void Dispose()
    {
        if (_tasks is not null) _tasks.Changed -= Raise;
    }
}
