using System.Collections.Concurrent;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Adapters;

/// <summary>
/// In-memory stand-in that the Cosmos-backed store replaces — the same status
/// as <see cref="InMemoryTaskReplica"/> beside it. A code lives ten minutes, so
/// losing the table on restart costs at most one re-issued code.
/// </summary>
public sealed class InMemoryPairingCodeStore : IPairingCodeStore
{
    private readonly ConcurrentDictionary<string, PairingCode> _codes = new(StringComparer.Ordinal);

    public Task Add(PairingCode code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        _codes[code.CodeHash] = code;
        return Task.CompletedTask;
    }

    public Task<PairingCode?> FindByHash(string codeHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(_codes.TryGetValue(codeHash, out var code) ? code : null);

    /// <summary>
    /// Compare-and-swap rather than read-then-write, so two devices racing on
    /// the same code cannot both be told they won. The real store owes the same
    /// guarantee — an ETag precondition in Cosmos — which is why the port hands
    /// back a bool instead of returning void and trusting the caller's check.
    /// </summary>
    public Task<bool> TryBurn(string codeHash, CancellationToken cancellationToken = default)
    {
        if (!_codes.TryGetValue(codeHash, out var existing) || existing.Redeemed)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_codes.TryUpdate(codeHash, existing with { Redeemed = true }, existing));
    }
}
