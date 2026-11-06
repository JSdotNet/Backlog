using System.Globalization;

using global::Backlog.Modules.Notifications.Abstractions;
using global::Backlog.SharedKernel;
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

    /// <summary>The group a notification whose repository could not be read sits in, grouped by
    /// repository.</summary>
    public const string RepositoryUnknownLabel = "Repository not known";

    /// <summary>The one group every notification sits in when the list is not grouped.</summary>
    public const string AllLabel = "All notifications";

    // The source context the session relay publishes under. Notifications may not see the
    // Sessions context (ModuleBoundaryTests), so its name is spelled here; it is a stored
    // value and does not change.
    private const string SessionsContext = "sessions";

    /// <summary>The notifications grouped by subject: groups in the order their newest
    /// notification comes, each newest first, an open question leading its group.</summary>
    public static IReadOnlyList<NotificationGroup> Group(IEnumerable<NotificationView> notifications) =>
        Group(notifications, NotificationGrouping.Subject, new Dictionary<Guid, string?>());

    /// <summary>
    /// The notifications grouped by <paramref name="grouping"/>, in the order
    /// <see cref="Group(IEnumerable{NotificationView})"/> keeps: a group with an open question
    /// first, then by its newest notification; inside a group an open question first, then
    /// newest first. <paramref name="repositories"/> is the host's answer of which repository
    /// each is about, by id — an alias, or null for the Brain's — and a notification it leaves
    /// out sits under <see cref="RepositoryUnknownLabel"/>.
    /// </summary>
    public static IReadOnlyList<NotificationGroup> Group(
        IEnumerable<NotificationView> notifications,
        NotificationGrouping grouping,
        IReadOnlyDictionary<Guid, string?> repositories)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(repositories);

        return
        [
            .. notifications
                .GroupBy(n => GroupKey(n, grouping, repositories), StringComparer.Ordinal)
                .Select(group =>
                {
                    var ordered = group.OrderByDescending(n => n.IsOpenQuestion).ThenByDescending(n => n.CreatedAt).ToList();
                    return new NotificationGroup(group.Key, GroupLabel(ordered[0], grouping, repositories), ordered);
                })
                .OrderByDescending(group => group.Notifications.Any(n => n.IsOpenQuestion))
                .ThenByDescending(group => group.Notifications.Max(n => n.CreatedAt))
        ];
    }

    /// <summary>
    /// What the centre groups by when nothing was chosen: by repository while the scope is
    /// empty or holds several, where the repository is what tells the notifications apart;
    /// by system while one repository — or the Brain alone — is pressed.
    /// </summary>
    public static NotificationGrouping DefaultGrouping(IReadOnlyCollection<string> scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return scope.Count == 1 ? NotificationGrouping.System : NotificationGrouping.Repository;
    }

    /// <summary>A subject as a heading — "Session sess-91", "Task T-123". A subject named by a
    /// GUID — a spec-manager question on no linked task — reads as its type alone, since a GUID
    /// says nothing to a reader; the list heads such a group by its place or title instead.</summary>
    public static string SubjectLabel(NotificationSubject? subject)
    {
        if (subject is null) return NoSubjectLabel;

        var type = subject.Type.Replace('-', ' ').Replace('_', ' ');
        type = type.Length == 0 ? type : char.ToUpperInvariant(type[0]) + type[1..];
        return Guid.TryParse(subject.Id, out _) ? type : $"{type} {subject.Id}";
    }

    /// <summary>
    /// Who raised a notification, as a kind, keyed on the words Backlog's own sources write
    /// (<see cref="NotificationOrigins"/>): an AI agent for what came through an agent session
    /// or the MCP tools, and for a session question with no origin; a person for a question
    /// typed in spec-manager and for a remark its author left; Backlog itself for the rest —
    /// pairing, the settings test.
    /// </summary>
    public static NotificationAskerKind AskerKind(NotificationView notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var via = notification.Origin?.Via;

        if (via is not null
            && (via.StartsWith(NotificationOrigins.AgentSession, StringComparison.Ordinal)
                || via == NotificationOrigins.McpAsk
                || via == NotificationOrigins.McpNotify))
        {
            return NotificationAskerKind.AiAgent;
        }

        if (notification.Origin is null && string.Equals(notification.Source.Context, SessionsContext, StringComparison.Ordinal))
        {
            return NotificationAskerKind.AiAgent;
        }

        if (via == NotificationOrigins.TypedInSpecManager) return NotificationAskerKind.Person;

        // A remark names its author and no way of asking: a person wrote it.
        if (via is null && !string.IsNullOrWhiteSpace(notification.Origin?.AskedBy)) return NotificationAskerKind.Person;

        return NotificationAskerKind.Backlog;
    }

    /// <summary>The kind in words: "AI agent", "Person", "Backlog" — the asker badge's own.</summary>
    public static string AskerLabel(NotificationAskerKind kind) => NotificationAskerBadge.Label(CardAsker(kind));

    /// <summary>The notifications <paramref name="kind"/> asked, or all of them for null.</summary>
    public static IReadOnlyList<NotificationView> AskedByOnly(IReadOnlyList<NotificationView> notifications, NotificationAskerKind? kind)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        return kind is { } only ? [.. notifications.Where(n => AskerKind(n) == only)] : notifications;
    }

    /// <summary>The questions, the alerts, or whatever is still open; all of them for null.</summary>
    public static IReadOnlyList<NotificationView> KindOnly(IReadOnlyList<NotificationView> notifications, NotificationKindFilter? filter)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        return filter is { } only ? [.. notifications.Where(n => Keeps(only, n))] : notifications;
    }

    /// <summary>Whether <paramref name="filter"/> keeps <paramref name="notification"/>.</summary>
    public static bool Keeps(NotificationKindFilter filter, NotificationView notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return filter switch
        {
            NotificationKindFilter.Questions => notification.Kind == NotificationKind.Question,
            NotificationKindFilter.Alerts => notification.Kind == NotificationKind.Alert,
            _ => notification.IsOpen
        };
    }

    /// <summary>
    /// Whether a notification is in the header's scope, by the rule the Sessions list keeps:
    /// everything is while nothing is scoped; one that resolves to a scoped repository is; one
    /// that resolves to no repository is the Brain's, in while the Brain is scoped (local ADR
    /// 0027). One the host could not resolve — left out of <paramref name="repositories"/> — is
    /// in under every scope, so a source that could not be read never hides a question.
    /// </summary>
    public static bool InScope(NotificationView notification, IReadOnlyList<string> scope, IReadOnlyDictionary<Guid, string?> repositories)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(repositories);

        if (scope.Count == 0 || !repositories.TryGetValue(notification.Id, out var alias)) return true;

        return alias is null
            ? BrainScope.In(scope)
            : BrainScope.Repositories(scope).Contains(alias, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The notifications <see cref="InScope"/> keeps.</summary>
    public static IReadOnlyList<NotificationView> ScopeOnly(
        IReadOnlyList<NotificationView> notifications,
        IReadOnlyList<string> scope,
        IReadOnlyDictionary<Guid, string?> repositories)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        return scope.Count == 0 ? notifications : [.. notifications.Where(n => InScope(n, scope, repositories))];
    }

    /// <summary>Who raised it, by its source alone — "Sessions", "MCP", "Devbook". What a
    /// notification without an origin is drawn by.</summary>
    public static string SourceLabel(NotificationSource source) => NotificationOrigins.SourceLabel(source);

    /// <summary>Who raised it, as a person reads it: the origin's "System · Place" where the
    /// notification names one, otherwise its source — "Sessions", "MCP".</summary>
    public static string SourceLabel(NotificationView notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return notification.Origin is { } origin ? origin.Line : SourceLabel(notification.Source);
    }

    /// <summary>The system a notification comes from, which the centre's "From" filter picks by:
    /// its origin's system, or its source for one that names no origin — the rule the phone's
    /// From filter shares (<see cref="NotificationOrigins.From"/>).</summary>
    public static string From(NotificationView notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return NotificationOrigins.From(notification.Origin, notification.Source);
    }

    /// <summary>Every system the notifications come from, once each, in alphabetical order — the
    /// "From" filter's options.</summary>
    public static IReadOnlyList<string> Froms(IEnumerable<NotificationView> notifications)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        return [.. notifications.Select(From).Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The notifications from <paramref name="from"/>, or all of them when it is
    /// null or empty.</summary>
    public static IReadOnlyList<NotificationView> FromOnly(IReadOnlyList<NotificationView> notifications, string? from)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        return string.IsNullOrEmpty(from)
            ? notifications
            : [.. notifications.Where(n => string.Equals(From(n), from, StringComparison.Ordinal))];
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
            NotificationState.Acknowledged => Join("Acknowledged", SourceLabel(notification)),
            NotificationState.Closed => Join("Closed", notification.CloseReason),
            _ => Join(SourceLabel(notification), Ago(notification.CreatedAt, now))
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
            SourceLabel(notification),
            Ago(notification.CreatedAt, now),
            notification.AnswerSpec is { } spec ? ToCardSpec(spec) : null,
            notification.State switch
            {
                NotificationState.Acknowledged => NotificationCardState.Acknowledged,
                NotificationState.Answered => NotificationCardState.Answered,
                NotificationState.Closed => NotificationCardState.Closed,
                _ => NotificationCardState.Open
            },
            Outcome(notification, now, thisDeviceId),
            notification.Origin is { } origin ? ToCardOrigin(notification, origin) : null,
            // Every card marks the kind its row marks, origin or not.
            CardAsker(AskerKind(notification)));
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
            },
            choice.TakesText,
            choice.Placeholder))],
        spec.AllowMultiple,
        spec.Open is { } open ? new NotificationCardOpenAnswer(open.Placeholder, open.MaxLength) : null,
        spec.MinChoices,
        spec.MaxChoices,
        spec.Note is { } note ? new NotificationCardOpenAnswer(note.Placeholder, note.MaxLength) : null);

    /// <summary>The kind as the library's card and asker badge name it.</summary>
    public static NotificationCardAsker CardAsker(NotificationAskerKind kind) => kind switch
    {
        NotificationAskerKind.AiAgent => NotificationCardAsker.Agent,
        NotificationAskerKind.Person => NotificationCardAsker.Person,
        _ => NotificationCardAsker.System
    };

    /// <summary>The card's origin: the origin whole, with its asker kind. For an agent's the
    /// way it was asked drops the "Agent session" the marker already says, keeping the session
    /// it names, so the card reads "for Job · Fix the toast".</summary>
    private static NotificationCardOrigin ToCardOrigin(NotificationView notification, NotificationOrigin origin)
    {
        var asker = AskerKind(notification);
        var via = origin.Via;

        if (asker == NotificationAskerKind.AiAgent && via is not null && via.StartsWith(NotificationOrigins.AgentSession, StringComparison.Ordinal))
        {
            var rest = via[NotificationOrigins.AgentSession.Length..];
            rest = rest.StartsWith(NotificationOrigin.Separator, StringComparison.Ordinal) ? rest[NotificationOrigin.Separator.Length..] : rest;
            via = string.IsNullOrWhiteSpace(rest) ? null : rest.Trim();
        }

        return new NotificationCardOrigin(origin.System, origin.Place, origin.AskedBy, via, origin.OpenLink, CardAsker(asker));
    }

    private static string GroupKey(NotificationView notification, NotificationGrouping grouping, IReadOnlyDictionary<Guid, string?> repositories) =>
        grouping switch
        {
            NotificationGrouping.Repository => repositories.TryGetValue(notification.Id, out var alias)
                ? alias is null ? "brain" : "repository/" + alias.ToLowerInvariant()
                : "unknown",
            NotificationGrouping.System => "system/" + From(notification),
            NotificationGrouping.AskedBy => "asked-by/" + AskerKind(notification),
            NotificationGrouping.None => "all",
            _ => notification.Subject is { } subject ? $"{subject.Context}/{subject.Type}/{subject.Id}" : "none"
        };

    /// <summary>A group's heading, from its first notification.</summary>
    private static string GroupLabel(NotificationView first, NotificationGrouping grouping, IReadOnlyDictionary<Guid, string?> repositories) =>
        grouping switch
        {
            NotificationGrouping.Repository => repositories.TryGetValue(first.Id, out var alias)
                ? alias ?? BrainScope.Name
                : RepositoryUnknownLabel,
            NotificationGrouping.System => From(first),
            NotificationGrouping.AskedBy => AskerLabel(AskerKind(first)),
            NotificationGrouping.None => AllLabel,
            _ => first.Subject is { } subject && Guid.TryParse(subject.Id, out _)
                // A GUID names nothing a reader knows: the place it was asked in, else its title.
                ? string.IsNullOrWhiteSpace(first.Origin?.Place) ? first.Title : first.Origin.Place
                : SubjectLabel(first.Subject)
        };

    private static string Join(string lead, string? tail) =>
        string.IsNullOrWhiteSpace(tail) ? lead : $"{lead} · {tail}";
}

/// <summary>One subject's notifications, as the centre's list groups them.</summary>
/// <param name="Key">A stable key for the subject.</param>
/// <param name="Label">The subject as a heading.</param>
/// <param name="Notifications">Its notifications, an open question first, then newest first.</param>
public sealed record NotificationGroup(string Key, string Label, IReadOnlyList<NotificationView> Notifications);

/// <summary>Who raised a notification, as a kind; see <see cref="NotificationPresentation.AskerKind"/>.</summary>
public enum NotificationAskerKind
{
    /// <summary>An agent in a session, or one calling Backlog's MCP tools.</summary>
    AiAgent,

    /// <summary>A person: one who typed a question in spec-manager, or left a remark.</summary>
    Person,

    /// <summary>Backlog itself: pairing, the settings test.</summary>
    Backlog
}

/// <summary>What the centre's list is grouped by.</summary>
public enum NotificationGrouping
{
    /// <summary>The repository each is about; the Brain for none.</summary>
    Repository,

    /// <summary>The system each comes from — the From filter's word.</summary>
    System,

    /// <summary>Who asked, as a kind: AI agent, person, Backlog.</summary>
    AskedBy,

    /// <summary>The entity each is about.</summary>
    Subject,

    /// <summary>One list, not grouped.</summary>
    None
}

/// <summary>The centre's Kind filter.</summary>
public enum NotificationKindFilter
{
    /// <summary>Questions, open or settled.</summary>
    Questions,

    /// <summary>Alerts, open or settled.</summary>
    Alerts,

    /// <summary>Whatever is still open, question or alert.</summary>
    Open
}
