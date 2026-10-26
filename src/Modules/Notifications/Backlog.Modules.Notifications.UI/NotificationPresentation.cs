using System.Globalization;

using global::Backlog.Modules.Notifications.Abstractions;
using global::Backlog.UI.Components.Notifications;

namespace Backlog.Modules.Notifications.UI;

/// <summary>
/// How the centre puts a notification into words: its subject group, who raised it, how long
/// ago, and the card the library draws. Everything a reader sees that the library cannot work out
/// for itself, because it holds no clock and knows no module.
/// </summary>
public static class NotificationPresentation
{
    /// <summary>The group a notification without a subject — an alert like "Sync failed" — sits in.</summary>
    public const string NoSubjectLabel = "Not about one item";

    /// <summary>The notifications grouped by subject: groups in the order their newest
    /// notification comes, each newest first, an open question leading its group.</summary>
    public static IReadOnlyList<NotificationGroup> Group(IEnumerable<NotificationView> notifications)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        return
        [
            .. notifications
                .GroupBy(n => n.Subject)
                .Select(group => new NotificationGroup(
                    GroupKey(group.Key),
                    SubjectLabel(group.Key),
                    [.. group.OrderByDescending(n => n.IsOpenQuestion).ThenByDescending(n => n.CreatedAt)]))
                .OrderByDescending(group => group.Notifications.Any(n => n.IsOpenQuestion))
                .ThenByDescending(group => group.Notifications.Max(n => n.CreatedAt))
        ];
    }

    /// <summary>A subject as a heading — "Session sess-91", "Task T-123".</summary>
    public static string SubjectLabel(NotificationSubject? subject)
    {
        if (subject is null) return NoSubjectLabel;

        var type = subject.Type.Replace('-', ' ').Replace('_', ' ');
        type = type.Length == 0 ? type : char.ToUpperInvariant(type[0]) + type[1..];
        return $"{type} {subject.Id}";
    }

    /// <summary>Who raised it — "Sessions", "MCP", "Devbook".</summary>
    public static string SourceLabel(NotificationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var context = source.Context.Trim();
        if (context.Length == 0) return context;
        if (context.Length <= 3) return context.ToUpperInvariant();
        return char.ToUpperInvariant(context[0]) + context[1..];
    }

    /// <summary>How long ago, the way the centre says it — "just now", "2 min ago", "1 h ago",
    /// then the date.</summary>
    public static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;
        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) return $"{(int)elapsed.TotalMinutes} min ago";
        if (elapsed < TimeSpan.FromDays(1)) return $"{(int)elapsed.TotalHours} h ago";
        return at.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture);
    }

    /// <summary>The device an answer came from — this desktop, or another paired device.</summary>
    public static string DeviceLabel(Guid deviceId, Guid thisDeviceId) =>
        deviceId == thisDeviceId ? "this desktop" : "another device";

    /// <summary>The winning answer as a person reads it: the chosen choices' labels, then the
    /// text written in the box, then the note written beside it.</summary>
    public static string? AnswerText(NotificationView notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification.Answer is not { } answer) return null;

        var choices = notification.AnswerSpec?.Choices ?? [];
        var labels = answer.ChoiceIds
            .Select(id => choices.FirstOrDefault(choice => choice.Id == id)?.Label ?? id)
            .ToList();

        if (!string.IsNullOrWhiteSpace(answer.Text)) labels.Add($"“{answer.Text}”");
        if (!string.IsNullOrWhiteSpace(answer.Note)) labels.Add($"note: “{answer.Note}”");

        return labels.Count == 0 ? null : string.Join(", ", labels);
    }

    /// <summary>The line under a row's title: who raised it and when, or how it was settled.</summary>
    public static string RowLine(NotificationView notification, DateTimeOffset now, Guid thisDeviceId)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return notification.State switch
        {
            NotificationState.Answered when notification.Answer is { } answer =>
                Join($"Answered from {DeviceLabel(answer.FromDeviceId, thisDeviceId)}", AnswerText(notification)),
            NotificationState.Acknowledged => Join("Acknowledged", SourceLabel(notification.Source)),
            NotificationState.Closed => Join("Closed", notification.CloseReason),
            _ => Join(SourceLabel(notification.Source), Ago(notification.CreatedAt, now))
        };
    }

    /// <summary>The library card for one notification.</summary>
    public static NotificationCardModel ToCard(NotificationView notification, DateTimeOffset now, Guid thisDeviceId)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return new NotificationCardModel(
            notification.Id.ToString("N", CultureInfo.InvariantCulture),
            notification.Kind == NotificationKind.Question ? NotificationCardKind.Question : NotificationCardKind.Alert,
            notification.Severity switch
            {
                NotificationSeverity.Critical => NotificationCardSeverity.Critical,
                NotificationSeverity.Warning => NotificationCardSeverity.Warning,
                _ => NotificationCardSeverity.Info
            },
            notification.Title,
            notification.Summary,
            notification.BodyMarkdown,
            SourceLabel(notification.Source),
            Ago(notification.CreatedAt, now),
            notification.AnswerSpec is { } spec ? ToCardSpec(spec) : null,
            notification.State switch
            {
                NotificationState.Acknowledged => NotificationCardState.Acknowledged,
                NotificationState.Answered => NotificationCardState.Answered,
                NotificationState.Closed => NotificationCardState.Closed,
                _ => NotificationCardState.Open
            },
            Outcome(notification, now, thisDeviceId));
    }

    private static NotificationCardOutcome? Outcome(NotificationView notification, DateTimeOffset now, Guid thisDeviceId)
    {
        var when = notification.SettledAt is { } settled ? Ago(settled, now) : null;

        return notification.State switch
        {
            NotificationState.Answered => new NotificationCardOutcome(
                Answer: AnswerText(notification),
                Device: notification.Answer is { } answer ? DeviceLabel(answer.FromDeviceId, thisDeviceId) : null,
                When: when),
            NotificationState.Acknowledged => new NotificationCardOutcome(When: when),
            NotificationState.Closed => new NotificationCardOutcome(Reason: notification.CloseReason, When: when),
            _ => null
        };
    }

    private static NotificationCardAnswerSpec ToCardSpec(AnswerSpec spec) => new(
        [.. (spec.Choices ?? []).Select(choice => new NotificationCardChoice(
            choice.Id,
            choice.Label,
            choice.Style switch
            {
                AnswerChoiceStyle.Primary => NotificationCardChoiceStyle.Primary,
                AnswerChoiceStyle.Destructive => NotificationCardChoiceStyle.Destructive,
                _ => NotificationCardChoiceStyle.Default
            }))],
        spec.AllowMultiple,
        spec.Open is { } open ? new NotificationCardOpenAnswer(open.Placeholder, open.MaxLength) : null,
        spec.MinChoices,
        spec.MaxChoices,
        spec.Note is { } note ? new NotificationCardOpenAnswer(note.Placeholder, note.MaxLength) : null);

    private static string GroupKey(NotificationSubject? subject) =>
        subject is null ? "none" : $"{subject.Context}/{subject.Type}/{subject.Id}";

    private static string Join(string lead, string? tail) =>
        string.IsNullOrWhiteSpace(tail) ? lead : $"{lead} · {tail}";
}

/// <summary>One subject's notifications, as the centre's list groups them.</summary>
/// <param name="Key">A stable key for the subject.</param>
/// <param name="Label">The subject as a heading.</param>
/// <param name="Notifications">Its notifications, an open question first, then newest first.</param>
public sealed record NotificationGroup(string Key, string Label, IReadOnlyList<NotificationView> Notifications);
