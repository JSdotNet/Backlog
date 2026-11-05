using Backlog.Infrastructure.Sync.Notifications;
using Backlog.Mobile.UI.Notifications;
using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone's From and Repository filters over its Notifications tab
/// (<c>.devbook/domain/notifications/requirements.md</c> — "The phone filters its notifications by
/// system and repository", "Each phone filter counts what it keeps and shows only a real choice"):
/// the options, what each choice keeps, and the counts under the other filter.
/// </summary>
public sealed class NotificationFiltersTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    private static PhoneNotification Phone(string title, string? system, PublishedRepository? repository, int minutesAgo = 1)
    {
        var snapshot = TestNotifications.Question(title, Now.AddMinutes(-minutesAgo)) with
        {
            Origin = system is null ? null : new NotificationOrigin(system, "Somewhere"),
        };

        return new PhoneNotification(snapshot, "WORKSTATION-01", OwnerPresence.Online, null, NotificationStanding.Answerable, null, null, repository);
    }

    private static readonly PublishedRepository Backlog = new("Backlog");

    private static readonly PublishedRepository Fincent = new("fincent");

    private static IReadOnlyList<string> Titles(IEnumerable<PhoneNotification> items) =>
        [.. items.Select(item => item.Notification.Title)];

    // From: the desktop's rule (criterion 1)

    [Fact]
    public void From_is_the_origins_system()
    {
        Assert.Equal("Claude Code", NotificationFilters.From(Phone("A", "Claude Code", null)));
    }

    [Fact]
    public void From_without_an_origin_is_the_source_label_as_the_desktop_writes_it()
    {
        // TestNotifications' source context is "mcp": three letters or fewer read upper-cased.
        Assert.Equal("MCP", NotificationFilters.From(Phone("A", system: null, null)));
    }

    [Fact]
    public void The_from_options_are_each_system_once_in_alphabetical_order()
    {
        var items = new[] { Phone("A", "spec-manager", null), Phone("B", "Claude Code", null), Phone("C", "Backlog", null), Phone("D", "Claude Code", null) };

        Assert.Equal(["Backlog", "Claude Code", "spec-manager"], NotificationFilters.FromOptions(items));
    }

    [Fact]
    public void A_picked_system_no_longer_held_stays_among_the_from_options()
    {
        var items = new[] { Phone("A", "Claude Code", null) };

        Assert.Equal(["Claude Code", "Copilot"], NotificationFilters.FromOptions(items, chosen: "Copilot"));
    }

    // Repository (criteria 7 and 8)

    /// <summary>The Brain leads, as it leads the desktop's scope and its repository picker
    /// (local ADR 0027 §1, §2).</summary>
    [Fact]
    public void The_repository_options_are_the_brain_then_each_alias_once_and_never_not_known()
    {
        var items = new[]
        {
            Phone("A", "Claude Code", Fincent), Phone("B", "Claude Code", PublishedRepository.Brain),
            Phone("C", "Claude Code", Backlog), Phone("D", "Claude Code", null), Phone("E", "Claude Code", new PublishedRepository("backlog")),
        };

        Assert.Equal([NotificationFilters.Brain, "Backlog", "fincent"], NotificationFilters.RepositoryOptions(items));
    }

    /// <summary>Review round 1's blocker: a repository picked in one casing while the feed now
    /// names it first in another is still one option, not two.</summary>
    [Fact]
    public void A_picked_repository_in_another_casing_than_the_feeds_is_one_option()
    {
        var items = new[] { Phone("Newer", "Claude Code", Backlog, minutesAgo: 1), Phone("Older", "Claude Code", new PublishedRepository("BACKLOG"), minutesAgo: 5) };

        var options = NotificationFilters.RepositoryOptions(items, chosen: "BACKLOG");

        Assert.Equal(["Backlog"], options);
        Assert.Equal("Backlog", NotificationFilters.RepositoryOption(options, "BACKLOG"));
    }

    [Fact]
    public void A_picked_repository_no_longer_held_stays_among_the_repository_options()
    {
        var items = new[] { Phone("A", "Claude Code", Backlog) };

        Assert.Equal(["Backlog", "fincent"], NotificationFilters.RepositoryOptions(items, chosen: "fincent"));
        Assert.Equal([NotificationFilters.Brain, "Backlog"], NotificationFilters.RepositoryOptions(items, chosen: NotificationFilters.Brain));
    }

    [Fact]
    public void Picking_the_brain_keeps_the_brains_and_the_not_known_and_drops_a_repositorys()
    {
        var items = new[] { Phone("Pairing", "Backlog", PublishedRepository.Brain), Phone("About Backlog", "Claude Code", Backlog), Phone("Not known", "Claude Code", null) };

        var kept = NotificationFilters.Apply(items, new NotificationFilter(null, NotificationFilters.Brain));

        Assert.Equal(["Pairing", "Not known"], Titles(kept));
    }

    [Fact]
    public void Picking_a_repository_keeps_its_notifications_whatever_the_case_and_the_not_known()
    {
        var items = new[] { Phone("About Backlog", "Claude Code", new PublishedRepository("BACKLOG")), Phone("About Fincent", "Claude Code", Fincent), Phone("Not known", "Claude Code", null) };

        var kept = NotificationFilters.Apply(items, new NotificationFilter(null, "Backlog"));

        Assert.Equal(["About Backlog", "Not known"], Titles(kept));
    }

    // Both filters together (criterion 2)

    [Fact]
    public void Picking_a_system_keeps_only_its_notifications_in_the_order_given()
    {
        var items = new[] { Phone("First", "Claude Code", Backlog), Phone("Second", "spec-manager", Backlog), Phone("Third", "Claude Code", null) };

        var kept = NotificationFilters.Apply(items, new NotificationFilter("Claude Code", null));

        Assert.Equal(["First", "Third"], Titles(kept));
    }

    [Fact]
    public void No_choice_keeps_everything()
    {
        var items = new[] { Phone("First", "Claude Code", Backlog), Phone("Second", "spec-manager", null) };

        Assert.Same(items, NotificationFilters.Apply(items, NotificationFilter.None));
        Assert.False(NotificationFilter.None.IsActive);
        Assert.True(new NotificationFilter("Claude Code", null).IsActive);
    }

    // Counts under the other filter (criterion 3)

    [Fact]
    public void Each_from_option_counts_what_it_keeps_under_the_repository_choice()
    {
        var items = new[]
        {
            Phone("A", "Claude Code", Backlog), Phone("B", "Claude Code", Backlog), Phone("C", "spec-manager", Backlog),
            Phone("D", "spec-manager", Fincent), Phone("E", "Claude Code", Fincent),
        };
        var filter = new NotificationFilter("spec-manager", "Backlog");

        Assert.Equal(2, NotificationFilters.CountFrom(items, filter, "Claude Code"));
        Assert.Equal(1, NotificationFilters.CountFrom(items, filter, "spec-manager"));
        Assert.Equal(3, NotificationFilters.CountFrom(items, filter, from: null));
    }

    [Fact]
    public void Each_repository_option_counts_what_it_keeps_under_the_from_choice()
    {
        var items = new[]
        {
            Phone("A", "Claude Code", Backlog), Phone("B", "Claude Code", null), Phone("C", "spec-manager", Backlog),
            Phone("D", "Claude Code", PublishedRepository.Brain),
        };
        var filter = new NotificationFilter("Claude Code", "Backlog");

        // The not-known one counts under every repository choice, as it is kept under each.
        Assert.Equal(2, NotificationFilters.CountRepository(items, filter, "Backlog"));
        Assert.Equal(2, NotificationFilters.CountRepository(items, filter, NotificationFilters.Brain));
        Assert.Equal(3, NotificationFilters.CountRepository(items, filter, repository: null));
    }
}
