using Backlog.UI.Components.AppUpdate;

namespace Backlog.Mobile.UI.AppUpdate;

/// <summary>
/// When the last update check that got an answer ran. The Android head keeps it in
/// the app's preferences so the eight-hour spacing holds across launches; the browser
/// harness and the tests keep it in memory.
/// </summary>
public interface IAppUpdateCheckStamp
{
    DateTimeOffset? LastChecked { get; set; }
}

/// <summary>A check stamp that lasts as long as the process.</summary>
public sealed class InMemoryAppUpdateCheckStamp : IAppUpdateCheckStamp
{
    public DateTimeOffset? LastChecked { get; set; }
}

/// <summary>
/// What the shell knows about updates between visits to the update window: the
/// last answer, and the quiet check the app makes on its own after it is on screen.
/// </summary>
/// <remarks>
/// <para>
/// The quiet check runs at most once every <see cref="QuietInterval"/> — the
/// desktop's App Installer checks on launch every eight hours — only on a build that
/// can update itself, and never says anything when it fails: a person who did not ask
/// is not told that the network was down. An answer only marks the version control.
/// Installing is never started from here.
/// </para>
/// <para>
/// Only an answer is stamped. A failed check leaves the stamp alone, so the next
/// launch tries again instead of waiting eight hours behind a dead network.
/// </para>
/// </remarks>
public sealed class AppUpdateWatch(IAppUpdateService updates, IAppUpdateCheckStamp stamp, TimeProvider time)
{
    /// <summary>The least time between two quiet checks.</summary>
    public static readonly TimeSpan QuietInterval = TimeSpan.FromHours(8);

    /// <summary>Raised when <see cref="Latest"/> changes.</summary>
    public event Action? Changed;

    /// <summary>The last check that got an answer in this session, or null.</summary>
    public AppUpdateCheckResult? Latest { get; private set; }

    /// <summary>True when the last answer found a newer version.</summary>
    public bool UpdateReady => Latest?.UpdateReady == true;

    /// <summary>
    /// Checks if the last answer is older than <see cref="QuietInterval"/>. Never
    /// throws and never reports a failure.
    /// </summary>
    public async Task CheckQuietlyAsync(CancellationToken cancellationToken = default)
    {
        if (!updates.IsSupported) return;

        var last = stamp.LastChecked;
        if (last is not null && time.GetUtcNow() - last.Value < QuietInterval) return;

        try
        {
            Record(await updates.CheckForUpdateAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception)
        {
            // Quiet means quiet: the update window reports failures when asked.
        }
    }

    /// <summary>A check the person asked for: always asks, and returns the answer to show.</summary>
    public async Task<AppUpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var result = await updates.CheckForUpdateAsync(cancellationToken).ConfigureAwait(false);
        Record(result);
        return result;
    }

    private void Record(AppUpdateCheckResult result)
    {
        if (result.Availability is not (AppUpdateAvailability.UpToDate or AppUpdateAvailability.Available or AppUpdateAvailability.Required))
        {
            return;
        }

        stamp.LastChecked = time.GetUtcNow();
        Latest = result;
        Changed?.Invoke();
    }
}
