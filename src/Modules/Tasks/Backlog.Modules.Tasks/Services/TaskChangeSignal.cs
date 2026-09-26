using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Modules.Tasks.Services;

/// <summary>
/// The one <see cref="ITaskChangeSignal"/>: an event and an asynchronous-local
/// depth counter.
/// <para>
/// A counter rather than a flag so that suppressions nest — the merge wraps each
/// write, and a caller wrapping the whole page around it must not have its scope
/// ended early by the inner one. <see cref="AsyncLocal{T}"/> rather than a field
/// so the scope follows the awaiting flow and nothing else: the pull's writes
/// run inside it, and a save the UI makes on its own thread at the same moment
/// does not.
/// </para>
/// </summary>
public sealed class TaskChangeSignal : ITaskChangeSignal
{
    private readonly AsyncLocal<int> _suppressed = new();

    public event Action? Changed;

    public void Raise()
    {
        if (_suppressed.Value > 0) return;

        Changed?.Invoke();
    }

    public IDisposable Suppress()
    {
        _suppressed.Value++;

        return new Scope(this);
    }

    private sealed class Scope(TaskChangeSignal signal) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            signal._suppressed.Value--;
        }
    }
}
