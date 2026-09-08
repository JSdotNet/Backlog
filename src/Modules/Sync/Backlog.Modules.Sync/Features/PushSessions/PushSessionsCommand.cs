using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.PushSessions;

/// <summary>
/// A machine hands over what its coding agents have been doing.
/// <para>
/// The scope comes from the token and the records come from the body, and the
/// two never mix: nothing in a <see cref="SessionRecord"/> can name a machine or
/// an owner, so there is no batch a client could compose that writes a record
/// attributed to somebody else's machine (.arc42/adr/0005 §Session records).
/// </para>
/// </summary>
public sealed record PushSessionsCommand(OwnerScope Scope, IReadOnlyList<SessionRecord> Records);

/// <summary>
/// Writes the batch and says how much of it was taken.
/// <para>
/// There is no per-record outcome and no merge, and unlike the task push that is
/// not because a conflict policy resolved it — it is because there is no
/// conflict to have. A session ran on one machine and only that machine holds
/// the evidence for it, so last-write-wins never applies here and there is never
/// a second version to discard (.arc42/adr/0005 §Session records).
/// </para>
/// <para>
/// The handler does not look inside a record beyond the validation above it. It
/// does not derive a session's state, does not decide whether one has ended, and
/// does not compare a record with the one it replaces: the replica is a relay,
/// and the reading device's own session log is what turns evidence into state.
/// </para>
/// </summary>
public sealed class PushSessionsCommandHandler(ISessionReplica replica)
    : ICommandHandler<PushSessionsCommand, Result<PushSessionsResponse>>
{
    public async Task<Result<PushSessionsResponse>> Handle(
        PushSessionsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = SyncTelemetry.Source.StartActivity("sync.push_sessions", ActivityKind.Internal);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, command.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.DeviceIdTag, command.Scope.DeviceId.ToString());
        activity?.SetTag(SyncTelemetry.BatchSizeTag, command.Records.Count);

        var accepted = await replica.Append(command.Scope, command.Records, cancellationToken);

        return new PushSessionsResponse(accepted);
    }
}
