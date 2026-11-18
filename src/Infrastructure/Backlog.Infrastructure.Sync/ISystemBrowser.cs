using System.Diagnostics;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// Opens an address in the person's own browser, where the owner signs in to GitHub
/// (local ADR 0052 §1). A seam of its own so a test can play the browser.
/// </summary>
public interface ISystemBrowser
{
    /// <summary>Opens <paramref name="address"/>. Returns once it is handed over; the
    /// sign-in itself comes back to the loopback.</summary>
    Task OpenAsync(Uri address, CancellationToken cancellationToken = default);
}

/// <summary>The shell's default browser, through the URL's protocol handler.</summary>
public sealed class ShellSystemBrowser : ISystemBrowser
{
    public Task OpenAsync(Uri address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("Only a web address is opened in the browser.", nameof(address));
        }

        using var _ = Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true, CreateNoWindow = true });
        return Task.CompletedTask;
    }
}
