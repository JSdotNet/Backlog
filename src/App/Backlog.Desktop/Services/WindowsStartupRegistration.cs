using System.Runtime.InteropServices;

using Backlog.Desktop.UI.Shell;

using Microsoft.Extensions.Logging;

using Windows.ApplicationModel;

namespace Backlog.Desktop.Services;

/// <summary>
/// Starting Backlog at sign-in through the MSIX <c>StartupTask</c> the packaged head
/// declares in <c>Package.appxmanifest</c> (local ADR 0043 §5). A process with no
/// package identity — a Debug or Aspire build — has no task to read, and answers
/// <see cref="StartupRegistrationState.Unavailable"/>.
/// </summary>
internal sealed class WindowsStartupRegistration(ILogger<WindowsStartupRegistration> logger) : IStartupRegistration
{
    /// <summary>The <c>TaskId</c> the manifest declares.</summary>
    public const string TaskId = "BacklogStartup";

    private const int AppModelErrorNoPackage = 15700;

    public async Task<StartupRegistrationState> GetStateAsync()
    {
        if (!HasPackageIdentity()) return StartupRegistrationState.Unavailable;

        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            return Map(task.State);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Reading the startup task failed.");
            return StartupRegistrationState.Unavailable;
        }
    }

    public async Task<StartupRegistrationState> SetEnabledAsync(bool enabled)
    {
        if (!HasPackageIdentity()) return StartupRegistrationState.Unavailable;

        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (enabled) return Map(await task.RequestEnableAsync());

            task.Disable();
            return Map(task.State);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Changing the startup task failed.");
            return await GetStateAsync();
        }
    }

    private static StartupRegistrationState Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => StartupRegistrationState.Enabled,
        StartupTaskState.DisabledByUser => StartupRegistrationState.DisabledByUser,
        StartupTaskState.DisabledByPolicy => StartupRegistrationState.DisabledByPolicy,
        _ => StartupRegistrationState.Disabled,
    };

    /// <summary>Whether this process runs from an installed package. Asked of Windows
    /// directly rather than by touching <c>Package.Current</c>, which throws when the
    /// answer is no.</summary>
    private static bool HasPackageIdentity()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, char[]? packageFullName);
}
