using System.Collections.Concurrent;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Adapters;

/// <summary>
/// The enrolment tickets, in memory. Unlike the pairing codes this is the store a
/// deployed service uses too: a ticket lives minutes, the service runs one replica,
/// and a restart between the start and the registration costs one more sign-in. So
/// does a revision swap, when the old and new revision run side by side and the
/// callback can reach the one that did not take the start: it answers
/// <c>enrolment.sign_in_unknown</c>, never a code. Expired tickets are dropped as new
/// ones arrive, and the anonymous routes are rate-limited, so the table stays small.
/// </summary>
public sealed class InMemoryEnrolmentTicketStore(TimeProvider clock) : IEnrolmentTicketStore
{
    private readonly ConcurrentDictionary<string, EnrolmentTicket> _tickets = new(StringComparer.Ordinal);

    public Task Add(EnrolmentTicket ticket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var now = clock.GetUtcNow();
        foreach (var (key, stale) in _tickets)
        {
            if (stale.HasExpired(now)) _tickets.TryRemove(key, out _);
        }

        _tickets[ticket.KeyHash] = ticket;
        return Task.CompletedTask;
    }

    public Task<EnrolmentTicket?> FindByHash(string keyHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tickets.TryGetValue(keyHash, out var ticket) ? ticket : null);

    /// <summary>Compare-and-swap, so exactly one caller burns a ticket.</summary>
    public Task<bool> TryBurn(string keyHash, CancellationToken cancellationToken = default)
    {
        if (!_tickets.TryGetValue(keyHash, out var existing) || existing.Redeemed)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_tickets.TryUpdate(keyHash, existing with { Redeemed = true }, existing));
    }
}
