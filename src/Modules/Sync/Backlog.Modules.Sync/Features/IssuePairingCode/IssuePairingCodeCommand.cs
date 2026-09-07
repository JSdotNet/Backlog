using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.IssuePairingCode;

/// <summary>
/// An already-paired device asks for a code to read out to the next one. The
/// scope comes from the caller's token and is the only thing that decides which
/// owner the new device joins — the command carries no owner id for a caller to
/// state.
/// </summary>
public sealed record IssuePairingCodeCommand(OwnerScope Scope);

/// <summary>
/// Mints a code, stores its hash against the owner, and hands the plaintext
/// back once. Ten minutes is short enough that a code left on a screen stops
/// mattering by itself, and long enough to walk to the other device.
/// </summary>
public sealed class IssuePairingCodeCommandHandler(
    IPairingCodeStore codes,
    IPairingCodeGenerator generator,
    ICredentialHasher hasher,
    TimeProvider clock)
    : ICommandHandler<IssuePairingCodeCommand, Result<PairingCodeResponse>>
{
    /// <summary>How long a code lives. Not configurable: it is a property of
    /// how the pairing gesture is meant to feel, not of a deployment.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public async Task<Result<PairingCodeResponse>> Handle(
        IssuePairingCodeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = generator.Next();
        var expiresAt = clock.GetUtcNow().Add(Lifetime);

        await codes.Add(
            new PairingCode(
                hasher.Hash(PairingCodeFormat.Normalize(code)),
                command.Scope.OwnerId,
                command.Scope.DeviceId,
                expiresAt),
            cancellationToken);

        return new PairingCodeResponse(code, expiresAt);
    }
}
