using Backlog.Desktop.Services;
using Backlog.Infrastructure.Mcp;

#if WINDOWS
using Microsoft.Maui.ApplicationModel;
using Windows.Security.Credentials.UI;
#endif

namespace Backlog.Desktop.Mcp;

/// <summary>
/// <see cref="IUserConsent"/> for the desktop head (local ADR 0052 §6): Windows Hello, through
/// <c>UserConsentVerifier</c> — face, fingerprint or PIN, which a script or UI automation cannot
/// pass.
/// <para>
/// An unpackaged desktop process has no core window for the prompt to stand over, so it is asked
/// through <c>UserConsentVerifierInterop</c> for the main window's handle, on the UI thread, the
/// way <see cref="WindowsBrainFolderPicker"/> opens its picker. A device with no Hello set up —
/// or one whose prompt cannot be raised — answers <see cref="UserConsentOutcome.NotAvailable"/>,
/// and the caller refuses what it was about to allow.
/// </para>
/// </summary>
public sealed class WindowsHelloUserConsent(ShellWindows windows) : IUserConsent
{
    /// <inheritdoc />
    public async Task<UserConsentOutcome> VerifyAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

#if WINDOWS
        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync().AsTask(cancellationToken).ConfigureAwait(false);
            if (availability != UserConsentVerifierAvailability.Available) return UserConsentOutcome.NotAvailable;

            var result = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var window = windows.MainWindow?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                if (window is null) return (UserConsentVerificationResult?)null;

                var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                return await UserConsentVerifierInterop.RequestVerificationForWindowAsync(handle, message).AsTask(cancellationToken);
            }).ConfigureAwait(false);

            return result switch
            {
                null => UserConsentOutcome.NotAvailable,
                UserConsentVerificationResult.Verified => UserConsentOutcome.Verified,
                UserConsentVerificationResult.DeviceNotPresent
                    or UserConsentVerificationResult.NotConfiguredForUser
                    or UserConsentVerificationResult.DisabledByPolicy => UserConsentOutcome.NotAvailable,
                _ => UserConsentOutcome.Declined,
            };
        }
        catch (OperationCanceledException)
        {
            return UserConsentOutcome.Declined;
        }
#pragma warning disable CA1031 // A prompt that cannot be raised allows nothing; the caller says so.
        catch (Exception)
#pragma warning restore CA1031
        {
            return UserConsentOutcome.NotAvailable;
        }
#else
        await Task.CompletedTask;
        return UserConsentOutcome.NotAvailable;
#endif
    }
}
