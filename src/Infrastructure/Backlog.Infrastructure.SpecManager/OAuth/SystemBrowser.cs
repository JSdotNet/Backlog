using System.Diagnostics;

namespace Backlog.Infrastructure.SpecManager.OAuth;

/// <summary>
/// Opens the authorization page in the person's own browser — where their
/// spec-manager session already is — through the shell's association for
/// <c>https</c>. An embedded view would ask them to sign in again and teach them to
/// type a password into something that is not their browser (RFC 8252 §8.12).
/// </summary>
internal static class SystemBrowser
{
    public static Task OpenAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();

        // The browser is the window; the launch itself needs no console.
        using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true, CreateNoWindow = true });
        return Task.CompletedTask;
    }
}
