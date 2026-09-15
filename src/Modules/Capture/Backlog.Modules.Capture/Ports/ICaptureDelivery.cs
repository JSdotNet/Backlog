using Backlog.Modules.Capture.Abstractions;

namespace Backlog.Modules.Capture.Ports;

/// <summary>What the receiving side did with one capture. Three answers
/// rather than a bool, because the run counts only the first as new, and a
/// person reading the run's line deserves "already had it" to be different from
/// "nothing to keep".</summary>
public enum CaptureDeliveryOutcome
{
    /// <summary>A new item was made of it.</summary>
    Delivered,

    /// <summary>The id was already there. A re-run over the same feed, which
    /// is the ordinary case and costs nothing.</summary>
    AlreadyKnown,

    /// <summary>Nothing to make an item of — a capture with no title.</summary>
    Ignored
}

/// <summary>
/// One capture as the run hands it over: everything the receiving side needs
/// and nothing about the source it was read from.
/// </summary>
/// <param name="Id">Deterministic, from the source kind and the entry's own id
/// (<see cref="Features.RunCapture.CaptureIds"/>), so the same entry read on a
/// later run arrives under the same id and the receiving side can say it
/// already has it. That is the whole of what makes a re-run free: the run does
/// not remember what it delivered, and does not need to.</param>
/// <param name="Kind">Where it came from.</param>
/// <param name="Title">The Inbox row's title.</param>
/// <param name="SourceUrl">Where it lives, when the source said.</param>
/// <param name="BodyMd">What the source offered beneath the title, or null.</param>
/// <param name="CapturedAt">When the entry appeared at the source, or when the
/// run saw it if the source did not say.</param>
public sealed record CaptureItem(
    Guid Id,
    CaptureSourceKind Kind,
    string Title,
    string? SourceUrl,
    string? BodyMd,
    DateTimeOffset CapturedAt);

/// <summary>
/// Where a capture goes once the run has made one.
/// <para>
/// A port declared here and answered in <c>src/Infrastructure</c> by an adapter
/// over the Inbox's intake — Capture may not reference Inbox, and the Inbox
/// does not know Capture exists, so the join between them is an adapter's the
/// way the Inbox's own backlog target is. The run sees only the outcome.
/// </para>
/// </summary>
public interface ICaptureDelivery
{
    Task<CaptureDeliveryOutcome> DeliverAsync(CaptureItem item, CancellationToken cancellationToken = default);
}
