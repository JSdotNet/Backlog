namespace Backlog.Infrastructure.Sync;

/// <summary>
/// How a development host picks a sync-state store.
/// <para>
/// The twin of <see cref="DeviceCredentialStoreFactory"/>, and written beside it
/// for the same reason: a harness's progress has to be scoped the way its
/// credential is, or the two harnesses stop being two devices. A pair that
/// shared a watermark would have each side skipping what the other had pushed,
/// which is the one failure a pairing demo cannot show.
/// </para>
/// <para>
/// Unlike the credential factory there is no platform branch. The credential one
/// has to choose between DPAPI and keeping a secret in memory; nothing here is a
/// secret, so the same plain file is right on every platform — and a store that
/// forgot its progress when the process ended would re-push the whole machine on
/// every run.
/// </para>
/// <para>
/// Development only. The shipped heads register their own store, under whichever
/// per-user location that head keeps its local state in.
/// </para>
/// </summary>
public static class TaskSyncStateStoreFactory
{
    /// <summary>
    /// A store for a locally run host, at the path
    /// <paramref name="environmentVariableName"/> names or, failing that, under
    /// the host's own content root.
    /// </summary>
    /// <param name="contentRootPath">The host's content root, which is what makes
    /// two harnesses on one machine two devices rather than one seen twice.</param>
    /// <param name="environmentVariableName">The variable a session sets to move
    /// the progress elsewhere. Each harness has its own, so pointing one of them
    /// somewhere does not silently move the other onto the same file and make one
    /// device skip the other's work.</param>
    /// <param name="defaultRelativePath">Where the progress goes when that
    /// variable is unset, relative to <paramref name="contentRootPath"/>.</param>
    public static ITaskSyncStateStore CreateLocalDevelopmentStore(
        string contentRootPath,
        string environmentVariableName,
        string defaultRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultRelativePath);

        var statePath = Environment.GetEnvironmentVariable(environmentVariableName);
        if (string.IsNullOrWhiteSpace(statePath))
        {
            statePath = Path.Combine(contentRootPath, defaultRelativePath);
        }

        return new FileTaskSyncStateStore(statePath);
    }
}
