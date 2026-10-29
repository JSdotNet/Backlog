using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Notifications.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Devbook;

/// <summary>
/// The remark store as every caller sees it, watched for a remark that arrives and one that
/// ends (<c>.devbook/domain/notifications/context.md</c>):
/// <list type="bullet">
/// <item>A remark left on another device — new here, open and typed into, synced in through
/// <see cref="IDevbookAnnotationStore.Apply"/> — raises an info alert about it, with a deep
/// link to its chapter (<see cref="DevbookNotificationSubjects.ChapterLink"/>). A remark
/// typed on this desktop raises none: the person who left it already knows. Nor does one
/// made more than <see cref="AlertWindow"/> ago: a device pulling every remark for the
/// first time, or after its store was cleared, would otherwise raise one per remark.</item>
/// <item>A remark that was open and is resolved or deleted by this call closes every
/// notification about it with <see cref="DevbookNotificationSubjects.ResolvedReason"/>.</item>
/// </list>
/// <para>
/// A decorator over the port rather than a line in each caller, because a remark is
/// resolved from three places — a chapter panel's toggle, the MCP
/// <c>resolve_annotation</c> tool, and <see cref="IDevbookAnnotationStore.Apply"/> when a
/// resolve made on another device syncs in — and all three go through this port. Only the
/// step out of open counts: re-applying a resolved remark closes nothing, and a remark
/// reopened later reopens no notification.
/// </para>
/// <para>
/// The store's methods are synchronous, so the publish and the close are started rather
/// than awaited, after the write, and a failing one is logged and never reaches the caller.
/// The next start's probe (<see cref="RemarkSubjectProbe"/>) asks again about any close it
/// missed; a missed alert is not raised again.
/// </para>
/// </summary>
public sealed class RemarkNotificationStore(
    IDevbookAnnotationStore inner,
    Func<INotifications?> notifications,
    ILogger<RemarkNotificationStore>? logger = null,
    TimeProvider? time = null) : IDevbookAnnotationStore
{
    /// <summary>How recently a remark arriving from another device must have been made to
    /// raise an alert.</summary>
    public static readonly TimeSpan AlertWindow = TimeSpan.FromDays(1);

    private const int MaxTitleLength = 80;
    private const int MaxSummaryLength = 180;

    private readonly IDevbookAnnotationStore _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Func<INotifications?> _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;
    private readonly TimeProvider _time = time ?? TimeProvider.System;

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

        var before = _inner.Find(replicated.Id);

        Watching(replicated.Id, () => _inner.Apply(replicated));

        // Left elsewhere, lately: unknown here, or known only as an empty draft, and now an
        // open remark with words in it, made within the window.
        if ((before is null || before.IsDraft)
            && _inner.Find(replicated.Id) is { IsDraft: false } arrived
            && DevbookNotificationSubjects.IsOpen(arrived)
            && arrived.CreatedAt > _time.GetUtcNow() - AlertWindow)
        {
            LastAlert = AlertAsync(arrived);
        }
    }

    public IReadOnlyList<DevbookAnnotation> ListChangedSince(DateTimeOffset watermark) =>
        _inner.ListChangedSince(watermark);

    /// <summary>The close started by the last call that ended a remark; what a test awaits.</summary>
    internal Task LastClose { get; private set; } = Task.CompletedTask;

    /// <summary>The alert started by the last remark that arrived; what a test awaits.</summary>
    internal Task LastAlert { get; private set; } = Task.CompletedTask;

    private void Watching(Guid id, Action write)
    {
        var wasOpen = DevbookNotificationSubjects.IsOpen(_inner.Find(id));

        write();

        if (wasOpen && !DevbookNotificationSubjects.IsOpen(_inner.Find(id)))
        {
            LastClose = CloseAsync(id);
        }
    }

    private static NotificationSubject Subject(Guid id) =>
        new(DevbookNotificationSubjects.Context, DevbookNotificationSubjects.Remark, DevbookNotificationSubjects.Id(id));

    private async Task AlertAsync(DevbookAnnotation remark)
    {
        try
        {
            if (_notifications() is not { } notifications) return;

            var published = await notifications.PublishAsync(Draft(remark)).ConfigureAwait(false);
            if (published.IsFailure) _logger.LogDebug("No alert was raised for remark {Id}: {Error}", remark.Id, published.Error.Message);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Raising the alert for remark {Id} failed; the remark is kept.", remark.Id);
        }
    }

    /// <summary>The alert: "Remark left on &lt;chapter&gt;", the remark's words as the summary
    /// and quoted in the body, about the remark, linking to its chapter.</summary>
    internal static NotificationDraft Draft(DevbookAnnotation remark)
    {
        var chapter = OneLine(remark.ChapterPath);
        var words = OneLine(remark.Body);
        var quoted = string.Join('\n', remark.Body.Trim().Split('\n').Select(line => "> " + line.TrimEnd('\r')));

        return new NotificationDraft(
            new NotificationSource(DevbookNotificationSubjects.Context, DevbookNotificationSubjects.Id(remark.Id)),
            Subject(remark.Id),
            NotificationKind.Alert,
            NotificationSeverity.Info,
            Fit("Remark left on " + chapter, MaxTitleLength),
            Fit(words, MaxSummaryLength),
            $"""
            A remark was left on `{chapter}`{(string.IsNullOrWhiteSpace(remark.Author) ? string.Empty : $" by {OneLine(remark.Author)}")}:

            {quoted}
            """,
            DeepLink: DevbookNotificationSubjects.ChapterLink(remark.RepositoryAlias, remark.ChapterPath),
            Origin: NotificationOrigin.Fit(NotificationOrigins.Backlog, chapter, askedBy: remark.Author));
    }

    private static string Fit(string text, int length) =>
        text.Length <= length ? text : text[..(length - 1)] + "…";

    private static string OneLine(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private async Task CloseAsync(Guid id)
    {
        try
        {
            if (_notifications() is not { } notifications) return;

            var closed = await notifications.CloseForSubjectAsync(Subject(id), DevbookNotificationSubjects.ResolvedReason).ConfigureAwait(false);
            if (closed.IsFailure) _logger.LogDebug("Notifications about remark {Id} were not closed: {Error}", id, closed.Error.Message);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Closing the notifications about remark {Id} failed; the next start's probe asks again.", id);
        }
    }
}
