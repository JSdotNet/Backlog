using System.Buffers.Text;
using System.Security.Cryptography;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;

namespace Backlog.Modules.Sync.Features.Enrolment;

/// <summary>
/// The one-time enrolment code and the loopback address that carries it home. Shared
/// by the GitHub callback and by a Development start that signs nobody in.
/// </summary>
internal static class EnrolmentCodes
{
    /// <summary>How long a sign-in may stay at GitHub before it comes back.</summary>
    internal static readonly TimeSpan SignInLifetime = TimeSpan.FromMinutes(10);

    /// <summary>How long the device has to redeem the code. Its loopback has it
    /// within a second; the window covers a slow registration call.</summary>
    internal static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>A fresh 256-bit secret, base64url: a code, or the state sent to GitHub.</summary>
    internal static string NewSecret() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>Mints a code for the sign-in <paramref name="from"/> began, stores its
    /// hash, and answers the loopback address that hands it to the device.</summary>
    internal static async Task<Uri> IssueAsync(
        IEnrolmentTicketStore tickets,
        ICredentialHasher hasher,
        TimeProvider clock,
        Uri redirect,
        string codeChallenge,
        string clientState,
        CancellationToken cancellationToken)
    {
        var code = NewSecret();

        await tickets.Add(
            new EnrolmentTicket(
                hasher.Hash(code),
                EnrolmentTicketKind.Code,
                codeChallenge,
                redirect,
                clientState,
                clock.GetUtcNow() + CodeLifetime),
            cancellationToken);

        return Loopback(redirect, ("code", code), ("state", clientState));
    }

    /// <summary>The device's loopback with OAuth's answer parameters on it.</summary>
    internal static Uri Loopback(Uri redirect, params (string Name, string Value)[] parameters)
    {
        var query = string.Join('&', parameters.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"));
        var builder = new UriBuilder(redirect);
        builder.Query = string.IsNullOrEmpty(builder.Query) ? query : $"{builder.Query.TrimStart('?')}&{query}";
        return builder.Uri;
    }
}
