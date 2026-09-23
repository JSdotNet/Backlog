using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.Sync.Sessions;

/// <summary>
/// Sync's half of the Sessions pane's update button: once this machine's records have
/// been amended, every one of them is sent again, so the other machines — and the
/// service's own copy — are amended too. The records are this machine's whether or not
/// this runs; it only decides whether they also travel.
/// </summary>
internal sealed class SessionSyncRecordPublisher(SessionSyncWorker worker, IAppFeatureSettings features) : ISessionRecordPublisher
{
    public bool RepublishAll()
    {
        if (!features.IsEnabled(SyncFeatures.Sync)) return false;

        worker.RepublishEverything();

        return true;
    }
}
