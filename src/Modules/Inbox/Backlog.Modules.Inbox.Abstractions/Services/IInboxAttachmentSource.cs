using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>
/// Where a capture's files come from: the attachment store behind the sync
/// service (local ADR 0014), asked for one file by the id the capture named.
/// <para>
/// A port declared here and answered in <c>src/Infrastructure</c>, for the
/// reason <see cref="IInboxBacklogTarget"/> is: the module sees neither HTTP nor
/// the sync service's routes. The whole file comes back as bytes rather than a
/// stream, because the service caps an attachment at a size a desktop holds in
/// memory without noticing, and a stream would hand the intake a response whose
/// lifetime it then has to manage around every early return.
/// </para>
/// <para>
/// Optional on every handler that takes it. A head composed without sync — or
/// a test — leaves it out, and an arriving capture's files are recorded by name
/// and left waiting rather than refused.
/// </para>
/// </summary>
public interface IInboxAttachmentSource
{
    /// <summary>The bytes stored under <paramref name="attachmentId"/>, or a
    /// failure whose message says why in words a person can act on — the file is
    /// gone, the service did not answer. Never throws for an answer the service
    /// meant to give.</summary>
    Task<Result<byte[]>> FetchAsync(Guid attachmentId, CancellationToken cancellationToken = default);
}
