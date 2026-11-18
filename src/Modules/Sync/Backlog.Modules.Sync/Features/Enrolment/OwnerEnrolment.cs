using System.Security.Cryptography;
using System.Text;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Features.Enrolment;

/// <summary>
/// Who may enrol a first device, and under which owner (local ADR 0052 §1). The host
/// builds it from configuration; the module only reads it.
/// </summary>
/// <param name="GitHubUserId">The numeric id of the one GitHub user who is the owner.
/// Null only on a Development service, where an enrolment signs nobody in and mints
/// a new owner each time — the shape every local run and test had before.</param>
/// <param name="ConfiguredOwnerId">The owner id the GitHub user's devices join. Set
/// to keep an owner minted before sign-in existed; otherwise derived from
/// <paramref name="GitHubUserId"/>, so it is the same on every start.</param>
public sealed record OwnerEnrolment(long? GitHubUserId, Guid? ConfiguredOwnerId = null)
{
    /// <summary>No owner configured: the Development stand-in.</summary>
    public static OwnerEnrolment Open { get; } = new(GitHubUserId: null);

    /// <summary>Whether enrolling needs the owner's GitHub sign-in.</summary>
    public bool RequiresSignIn => GitHubUserId is not null;

    /// <summary>The owner a signed-in enrolment joins, or null when there is no
    /// configured owner and each enrolment mints its own.</summary>
    public OwnerId? Owner =>
        ConfiguredOwnerId is { } configured ? new OwnerId(configured)
        : GitHubUserId is { } id ? new OwnerId(DeriveOwnerId(id))
        : null;

    /// <summary>A name-based (version 8) GUID over the GitHub user id: stable across
    /// restarts and deployments, and never the same as a minted version 7 id.</summary>
    private static Guid DeriveOwnerId(long gitHubUserId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"backlog-owner/github/{gitHubUserId}"));
        var bytes = hash.AsSpan(0, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
