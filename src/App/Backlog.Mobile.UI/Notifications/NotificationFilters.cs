using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Mobile.UI.Notifications;

/// <summary>
/// What the phone's Notifications tab is narrowed to: a system under From, a repository — or
/// <see cref="NotificationFilters.Brain"/> — under Repository; null for All. View state only,
/// kept on the device (<see cref="INotificationFilterStore"/>), never sent anywhere.
/// </summary>
/// <param name="From">The system picked under From, or null for every system.</param>
/// <param name="Repository">The alias picked under Repository, <see cref="NotificationFilters.Brain"/>
/// for the Brain, or null for every repository.</param>
public sealed record NotificationFilter(string? From, string? Repository)
{
    /// <summary>Both filters on All.</summary>
    public static NotificationFilter None { get; } = new(null, null);

    /// <summary>Whether either filter narrows anything — what shows Clear filters.</summary>
    public bool IsActive => !string.IsNullOrEmpty(From) || !string.IsNullOrEmpty(Repository);
}

/// <summary>
/// The phone's From and Repository filters
/// (<c>.devbook/domain/notifications/features.md#answering-from-another-device</c>): the options
/// each offers, what a choice keeps, and how many each option would keep under the other filter.
/// Pure, so the rules are tested without a screen. From is the desktop centre's rule, shared
/// through <see cref="NotificationOrigins.From"/>; Repository reads the repository the owner
/// published (local ADR 0039 §6) and keeps one it could not work out under every choice, as the
/// desktop's header scope does.
/// </summary>
public static class NotificationFilters
{
    /// <summary>The Repository choice that means the Brain. Not an alias any repository can
    /// have, and not empty, which a select's All option already holds.</summary>
    public const string Brain = "~brain";

    /// <summary>The system <paramref name="notification"/> comes from.</summary>
    public static string From(PhoneNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return NotificationOrigins.From(notification.Notification.Origin, notification.Notification.Source);
    }

    /// <summary>The From filter's options: every system the notifications come from, once each,
    /// in alphabetical order, and <paramref name="chosen"/> after them when no notification is
    /// from it any longer, so the filter still names what is in force.</summary>
    public static IReadOnlyList<string> FromOptions(IEnumerable<PhoneNotification> notifications, string? chosen = null)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        IReadOnlyList<string> held = [.. notifications.Select(From).Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase)];
        return string.IsNullOrEmpty(chosen) || held.Contains(chosen, StringComparer.Ordinal) ? held : [.. held, chosen];
    }

    /// <summary>
    /// The Repository filter's options: <see cref="Brain"/> first when any notification is the
    /// Brain's — it leads wherever repositories are offered (local ADR 0027 §1, §2) — then every
    /// repository the notifications name, once each without regard to case, in alphabetical
    /// order. One whose repository is not known adds no option. <paramref name="chosen"/> is
    /// added at its place when nothing matches it any longer, matched without regard to case as
    /// the filter itself matches, so a choice kept in another casing is never a second option.
    /// </summary>
    public static IReadOnlyList<string> RepositoryOptions(IEnumerable<PhoneNotification> notifications, string? chosen = null)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        var held = notifications.Select(notification => notification.Repository).Where(repository => repository is not null).ToList();
        var brain = held.Any(repository => repository!.IsBrain) || string.Equals(chosen, Brain, StringComparison.Ordinal);
        var aliases = held
            .Where(repository => !repository!.IsBrain)
            .Select(repository => repository!.Alias!)
            .ToList();

        if (!string.IsNullOrEmpty(chosen) && !string.Equals(chosen, Brain, StringComparison.Ordinal)
            && !aliases.Contains(chosen, StringComparer.OrdinalIgnoreCase))
        {
            aliases.Add(chosen);
        }

        return
        [
            .. brain ? [Brain] : Array.Empty<string>(),
            .. aliases.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>The option of <paramref name="options"/> that stands for the Repository choice
    /// <paramref name="chosen"/>, in the option's casing — what the select shows as picked — or
    /// null for All or a choice no option matches.</summary>
    public static string? RepositoryOption(IReadOnlyList<string> options, string? chosen)
    {
        ArgumentNullException.ThrowIfNull(options);

        return string.IsNullOrEmpty(chosen)
            ? null
            : options.FirstOrDefault(option => string.Equals(option, chosen, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What a Repository choice is called on screen: "Brain" for the Brain, else the alias.</summary>
    public static string RepositoryLabel(string choice) =>
        string.Equals(choice, Brain, StringComparison.Ordinal) ? Backlog.SharedKernel.BrainScope.Name : choice;

    /// <summary>Whether <paramref name="notification"/> is from <paramref name="from"/>; every
    /// notification is under a null or empty choice.</summary>
    public static bool KeepsFrom(PhoneNotification notification, string? from) =>
        string.IsNullOrEmpty(from) || string.Equals(From(notification), from, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="notification"/> is under the Repository choice
    /// <paramref name="repository"/>: every one is under All, and one whose repository is not
    /// known is under every choice.</summary>
    public static bool KeepsRepository(PhoneNotification notification, string? repository)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (string.IsNullOrEmpty(repository) || notification.Repository is not { } published) return true;

        return string.Equals(repository, Brain, StringComparison.Ordinal)
            ? published.IsBrain
            : !published.IsBrain && string.Equals(published.Alias, repository, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The notifications <paramref name="filter"/> keeps, in the order given; the same
    /// list when it narrows nothing.</summary>
    public static IReadOnlyList<PhoneNotification> Apply(IReadOnlyList<PhoneNotification> notifications, NotificationFilter filter)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(filter);

        if (!filter.IsActive) return notifications;

        return [.. notifications.Where(notification => KeepsFrom(notification, filter.From) && KeepsRepository(notification, filter.Repository))];
    }

    /// <summary>How many notifications the From option <paramref name="from"/> — null for All —
    /// keeps under <paramref name="filter"/>'s Repository choice.</summary>
    public static int CountFrom(IEnumerable<PhoneNotification> notifications, NotificationFilter filter, string? from)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(filter);

        return notifications.Count(notification => KeepsRepository(notification, filter.Repository) && KeepsFrom(notification, from));
    }

    /// <summary>How many notifications the Repository option <paramref name="repository"/> — null
    /// for All — keeps under <paramref name="filter"/>'s From choice.</summary>
    public static int CountRepository(IEnumerable<PhoneNotification> notifications, NotificationFilter filter, string? repository)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(filter);

        return notifications.Count(notification => KeepsFrom(notification, filter.From) && KeepsRepository(notification, repository));
    }
}
