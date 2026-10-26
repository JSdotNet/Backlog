using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Infrastructure.Devbook;

/// <summary>
/// The Devbook context's answer to the startup probe (<see cref="INotificationSubjectProbe"/>):
/// a remark a notification is about still lives while the store holds it, not deleted and
/// not resolved. Read from the remark store, so Notifications never reads it.
/// </summary>
public sealed class RemarkSubjectProbe(IDevbookAnnotationStore remarks) : INotificationSubjectProbe
{
    private readonly IDevbookAnnotationStore _remarks = remarks ?? throw new ArgumentNullException(nameof(remarks));

    public string Context => DevbookNotificationSubjects.Context;

    public Task<bool> IsLiveAsync(NotificationSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        // An id that is not a remark's is nothing this context can say ended.
        if (subject.Type != DevbookNotificationSubjects.Remark || !Guid.TryParse(subject.Id, out var id)) return Task.FromResult(true);

        return Task.FromResult(DevbookNotificationSubjects.IsOpen(_remarks.Find(id)));
    }
}
