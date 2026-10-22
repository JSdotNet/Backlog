using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Notifications.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Devbook;

/// <summary>
/// The remark store as every caller sees it, watched for a remark that ends: one that was
/// open and is resolved or deleted by this call closes every notification about it with
/// <see cref="DevbookNotificationSubjects.ResolvedReason"/>
/// (<c>.devbook/domain/notifications/domain.md#subject</c>).
/// <para>
/// A decorator over the port rather than a line in each caller, because a remark is
/// resolved from three places — a chapter panel's toggle, the MCP
/// <c>resolve_annotation</c> tool, and <see cref="IDevbookAnnotationStore.Apply"/> when a
/// resolve made on another device syncs in — and all three go through this port. Only the
/// step out of open counts: re-applying a resolved remark closes nothing, and a remark
/// reopened later reopens no notification.
/// </para>
/// <para>
/// The store's methods are synchronous, so the close is started rather than awaited, after
/// the write, and a failing close is logged and never reaches the caller. The next start's
/// probe (<see cref="RemarkSubjectProbe"/>) asks again about any it missed.
/// </para>
/// </summary>
public sealed class RemarkSubjectClosingStore(
    IDevbookAnnotationStore inner,
    Func<INotifications?> notifications,
    ILogger<RemarkSubjectClosingStore>? logger = null) : IDevbookAnnotationStore
{
    private readonly IDevbookAnnotationStore _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Func<INotifications?> _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public event Action? Changed
    {
        add => _inner.Changed += value;
        remove => _inner.Changed -= value;
    }

    public IReadOnlyList<DevbookAnnotation> List(string? repositoryAlias, string chapterPath) =>
        _inner.List(repositoryAlias, chapterPath);

    public DevbookAnnotation? Find(Guid id) => _inner.Find(id);

    public DevbookAnnotation Add(string? repositoryAlias, string chapterPath, int blockIndex, string author, string? blockHash = null) =>
        _inner.Add(repositoryAlias, chapterPath, blockIndex, author, blockHash);

    public void Edit(Guid id, string body) => _inner.Edit(id, body);

    public void SetResolved(Guid id, bool resolved) => Watching(id, () => _inner.SetResolved(id, resolved));

    public void Delete(Guid id) => Watching(id, () => _inner.Delete(id));

    public void Apply(DevbookAnnotation replicated)
    {
        ArgumentNullException.ThrowIfNull(replicated);

        Watching(replicated.Id, () => _inner.Apply(replicated));
    }

    public IReadOnlyList<DevbookAnnotation> ListChangedSince(DateTimeOffset watermark) =>
        _inner.ListChangedSince(watermark);

    /// <summary>The close started by the last call that ended a remark; what a test awaits.</summary>
    internal Task LastClose { get; private set; } = Task.CompletedTask;

    private void Watching(Guid id, Action write)
    {
        var wasOpen = DevbookNotificationSubjects.IsOpen(_inner.Find(id));

        write();

        if (wasOpen && !DevbookNotificationSubjects.IsOpen(_inner.Find(id)))
        {
            LastClose = CloseAsync(id);
        }
    }

    private async Task CloseAsync(Guid id)
    {
        var subject = new NotificationSubject(DevbookNotificationSubjects.Context, DevbookNotificationSubjects.Remark, DevbookNotificationSubjects.Id(id));

        try
        {
            if (_notifications() is not { } notifications) return;

            var closed = await notifications.CloseForSubjectAsync(subject, DevbookNotificationSubjects.ResolvedReason).ConfigureAwait(false);
            if (closed.IsFailure) _logger.LogDebug("Notifications about remark {Id} were not closed: {Error}", id, closed.Error.Message);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Closing the notifications about remark {Id} failed; the next start's probe asks again.", id);
        }
    }
}
