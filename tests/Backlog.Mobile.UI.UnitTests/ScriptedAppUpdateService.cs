using Backlog.UI.Components.AppUpdate;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The update service as the platform would answer it, from a script: what a check
/// finds, what an install says, and how often each was asked.
/// </summary>
internal sealed class ScriptedAppUpdateService : IAppUpdateService
{
    public string CurrentVersion { get; init; } = "0.1.59";

    public bool IsSupported { get; init; } = true;

    /// <summary>What the next check answers. Changeable between checks.</summary>
    public AppUpdateCheckResult CheckAnswer { get; set; } = AppUpdateCheckResult.UpToDate();

    /// <summary>What an install answers once <see cref="InstallGate"/> lets it.</summary>
    public AppUpdateInstallResult InstallAnswer { get; set; } = AppUpdateInstallResult.InProgress("Confirm the update in the Android installer.");

    /// <summary>Holds an install open until the test completes it; null answers at once.</summary>
    public TaskCompletionSource? InstallGate { get; set; }

    public bool CheckThrows { get; set; }

    public int Checks { get; private set; }

    public int Installs { get; private set; }

    public Task<AppUpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        Checks++;
        return CheckThrows
            ? Task.FromException<AppUpdateCheckResult>(new HttpRequestException("No such host is known."))
            : Task.FromResult(CheckAnswer);
    }

    public async Task<AppUpdateInstallResult> StartUpdateAsync(CancellationToken ct = default)
    {
        Installs++;
        if (InstallGate is not null) await InstallGate.Task;
        return InstallAnswer;
    }
}
