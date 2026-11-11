using Backlog.Modules.Events.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Events.Abstractions.Services;

/// <summary>What <see cref="IKnowledgeEventReplication"/> did with a copy from another
/// device.</summary>
public enum KnowledgeEventReplicaOutcome
{
    /// <summary>The copy was later than this device's, or this device held none, and it
    /// replaced it whole at the copy's stamp.</summary>
    Taken,

    /// <summary>This device holds the same version or a later one. Nothing was written.</summary>
    NotLater,

    /// <summary>The copy does not read as an event or a series — no id, a blank title or name, a
    /// value its own rules refuse — or this device keeps no events. Nothing was written.</summary>
    Unreadable,
}

/// <summary>
/// The port the desktop's knowledge event exchange reads events and series from and hands
/// other desktops' copies to (local ADR 0050 §2): each event and each series one whole
/// document, the later stamp winning per id, nothing deleted — the devbook subject feed's terms
/// (local ADR 0044). The exchange routes and never looks inside; which copy wins, and whether a
/// copy reads at all, is decided here.
/// </summary>
public interface IKnowledgeEventReplication
{
    /// <summary>Every event changed on this device or taken from another after
    /// <paramref name="watermark"/>, oldest stamp first.</summary>
    Task<IReadOnlyList<KnowledgeEventReplicaDto>> ListEventsChangedSinceAsync(
        DateTimeOffset watermark,
        CancellationToken cancellationToken = default);

    /// <summary>Every series changed after <paramref name="watermark"/>, oldest stamp first.</summary>
    Task<IReadOnlyList<EventSeriesReplicaDto>> ListSeriesChangedSinceAsync(
        DateTimeOffset watermark,
        CancellationToken cancellationToken = default);

    /// <summary>Offers an event that arrived from another device. Taken whole at its
    /// <see cref="KnowledgeEventReplicaDto.UpdatedAt"/> when that is later than this device's
    /// copy, or this device holds none.</summary>
    Task<KnowledgeEventReplicaOutcome> ApplyEventAsync(
        KnowledgeEventReplicaDto copy,
        CancellationToken cancellationToken = default);

    /// <summary>Offers a series that arrived from another device, on the same terms.</summary>
    Task<KnowledgeEventReplicaOutcome> ApplySeriesAsync(
        EventSeriesReplicaDto copy,
        CancellationToken cancellationToken = default);
}
