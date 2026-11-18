using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// Where the steps of an owner sign-in are kept, by hash. Short-lived state, the
/// pairing-code store's shape: a ticket lives minutes, and an implementation may
/// drop one once it has expired.
/// </summary>
public interface IEnrolmentTicketStore
{
    /// <summary>Stores a fresh ticket.</summary>
    Task Add(EnrolmentTicket ticket, CancellationToken cancellationToken = default);

    /// <summary>The ticket with this hash, or null.</summary>
    Task<EnrolmentTicket?> FindByHash(string keyHash, CancellationToken cancellationToken = default);

    /// <summary>Consumes a ticket and reports whether this caller was the one that
    /// did, so two callers racing on one code cannot both register.</summary>
    Task<bool> TryBurn(string keyHash, CancellationToken cancellationToken = default);
}
