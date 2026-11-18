using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.KnowledgeEvents;

/// <summary>
/// Where this desktop keeps the phone's attachment requests it could not answer yet — met while
/// the <c>events</c> area was off, or before their event arrived — between runs, each as the
/// task-feed document it arrived as (<see cref="PhoneRequestRetirements"/>).
/// </summary>
public interface IHeldAttachmentRequestStore
{
    /// <summary>Every request held, in the order first held.</summary>
    IReadOnlyList<TaskChange> ReadAll();

    /// <summary>Keeps <paramref name="request"/>, replacing one held under its id.</summary>
    void Hold(TaskChange request);

    /// <summary>Lets go of the request held under <paramref name="requestId"/>; none held is
    /// no error.</summary>
    void Release(Guid requestId);
}
