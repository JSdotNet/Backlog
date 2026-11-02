using System.Globalization;
using System.Text;

using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Infrastructure.SpecManager.Questions;

/// <summary>
/// One of the person's other spec-manager notifications — a mention, a reply, a change
/// proposal, an input, review work, administration — as the alert that says it in Backlog
/// (<c>.devbook/domain/notifications/domain.md#notification</c>), and which of them come in:
/// pure, so every rule is asserted without a server.
/// <para>
/// A <c>BacklogQuestion</c> never comes in here: it is asked as a question through
/// <see cref="SpecManagerQuestionSync"/>.
/// </para>
/// </summary>
internal static class SpecManagerNotificationMapper
{
    /// <summary>What the source key of every one of these alerts starts with.</summary>
    public const string KeyPrefix = "notification/";

    /// <summary>The reason an alert read in spec-manager closes with here.</summary>
    public const string ReadReason = "Read in spec-manager";

    /// <summary>The longest body published: the notification's bound.</summary>
    private const int MaxBodyBytes = 32 * 1024;

    /// <summary>The category words a person reads, for the categories spec-manager has
    /// today; another is split from its name (<see cref="ViaOf"/>).</summary>
    private static readonly Dictionary<string, string> CategoryWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mention"] = "Mention",
        ["Reply"] = "Reply",
        ["ChangeProposal"] = "Change proposal",
        ["Input"] = "Input",
        ["ReviewWork"] = "Review work",
        ["Administration"] = "Administration",
    };

    /// <summary>The source key of the alert for <paramref name="notificationId"/>:
    /// <c>notification/{id}</c>, the id lower case.</summary>
    public static string SourceKey(Guid notificationId) =>
        KeyPrefix + notificationId.ToString("D").ToLowerInvariant();

    /// <summary>
    /// The spec-manager notification an alert held here says, or null when it is not one of
    /// these alerts: an alert of the <c>spec-manager</c> source whose key is
    /// <c>notification/{id}</c> and whose own id is the one derived from that id on the
    /// installation at <paramref name="root"/>. The key alone would not do — a refusal alert
    /// about a product slugged <c>notification</c> has a key of that shape — and the root
    /// keeps another installation's alerts apart.
    /// </summary>
    public static Guid? NotificationOf(Guid id, NotificationKind kind, NotificationSource source, string root)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (kind != NotificationKind.Alert) return null;
        if (!string.Equals(source.Context, SpecManagerQuestionMapper.SourceContext, StringComparison.Ordinal)) return null;
        if (source.Key is not { } key || !key.StartsWith(KeyPrefix, StringComparison.Ordinal)) return null;
        if (!Guid.TryParseExact(key[KeyPrefix.Length..], "D", out var notificationId)) return null;

        return SpecManagerQuestionIds.ForNotification(root, notificationId) == id ? notificationId : null;
    }

    /// <summary>
    /// Whether <paramref name="notification"/> comes in as an alert: not a question, not read,
    /// and created at or after <paramref name="firstSignedInAt"/> — the moment this desktop
    /// first signed in to the installation, so a centre full of old ones raises nothing. The
    /// server's clock is compared with this machine's without an allowance, which leaves out
    /// rather than lets in.
    /// </summary>
    public static bool Admits(SpecManagerNotificationDto notification, DateTimeOffset firstSignedInAt)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return !notification.IsRead
            && !string.Equals(notification.Category, SpecManagerNotificationCategories.BacklogQuestion, StringComparison.OrdinalIgnoreCase)
            && notification.CreatedAt >= firstSignedInAt;
    }

    /// <summary>
    /// The alert <paramref name="notification"/> becomes. <paramref name="root"/> is the
    /// installation's, and <paramref name="defaultInstallation"/> whether it is the one that
    /// ships — the origin names the host of any other. <paramref name="productName"/> is the
    /// name of the product it is about, null when it is about none or the product is not
    /// known; <paramref name="objectName"/> what in the product, which the contract does not
    /// name yet.
    /// </summary>
    public static NotificationDraft ToDraft(
        SpecManagerNotificationDto notification,
        string root,
        bool defaultInstallation,
        string? productName,
        string? objectName = null)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var via = ViaOf(notification.Category);
        var link = DeepLinkOf(notification.Link, root);
        var subject = SpecManagerQuestionMapper.OneLine(notification.Subject);

        return new NotificationDraft(
            new NotificationSource(SpecManagerQuestionMapper.SourceContext, SourceKey(notification.Id)),
            Subject: null,
            NotificationKind.Alert,
            string.Equals(notification.Category, SpecManagerNotificationCategories.Mention, StringComparison.OrdinalIgnoreCase)
                ? NotificationSeverity.Warning
                : NotificationSeverity.Info,
            SpecManagerQuestionMapper.Cut(subject.Length > 0 ? subject : via ?? "spec-manager notification", 80),
            SpecManagerQuestionMapper.Cut(SpecManagerQuestionMapper.OneLine(notification.Text), 180),
            SpecManagerQuestionMapper.CutBytes(notification.Text ?? string.Empty, MaxBodyBytes),
            DeepLink: link,
            Origin: NotificationOrigin.Fit(
                SpecManagerQuestionMapper.SystemOf(root, defaultInstallation),
                PlaceOf(productName, objectName),
                askedBy: null,
                via,
                link),
            Id: SpecManagerQuestionIds.ForNotification(root, notification.Id));
    }

    /// <summary>
    /// The category in words: the ones spec-manager has today as a person says them
    /// ("Change proposal"), and one this build does not know split from its name, its first
    /// word capitalised and the rest lower case ("SecurityReviewRequest" is "Security review
    /// request"). Null for no category.
    /// </summary>
    public static string? ViaOf(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return null;

        var name = category.Trim();
        if (CategoryWords.TryGetValue(name, out var words)) return words;

        var split = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
            {
                split.Append(' ');
            }

            split.Append(c);
        }

        var lower = split.ToString().Replace('_', ' ').ToLower(CultureInfo.InvariantCulture);
        return char.ToUpper(lower[0], CultureInfo.InvariantCulture) + lower[1..];
    }

    /// <summary>Where the notification opens: an absolute <c>http</c> or <c>https</c> link as
    /// it is, an application path joined to <paramref name="root"/> with one slash, and
    /// nothing for no link.</summary>
    public static string? DeepLinkOf(string? link, string root)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;

        var trimmed = link.Trim();
        if (NotificationOrigin.IsOpenLink(trimmed)) return trimmed;

        return root.Trim().TrimEnd('/') + "/" + trimmed.TrimStart('/');
    }

    private static string? PlaceOf(string? productName, string? objectName) =>
        string.IsNullOrWhiteSpace(productName) ? null
        : string.IsNullOrWhiteSpace(objectName) ? productName
        : productName + NotificationOrigin.Separator + objectName;
}
