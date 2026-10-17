namespace Backlog.Modules.Progress.Services;

/// <summary>
/// Held by every command from load to save, so two writes in one host cannot save over
/// each other — and so the rules across goals (an item belongs to one) are checked
/// against what every other write has already saved. One for the whole host.
/// </summary>
public sealed class ProgressGate
{
    // Never disposed: it lives as long as the host, and no wait handle is ever asked of it.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Held(_gate);
    }

    private sealed class Held(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}
