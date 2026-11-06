using Backlog.Infrastructure.Sync.Notifications;
using Backlog.Mobile.UI.Components;
using Backlog.Mobile.UI.Notifications;
using Backlog.Modules.Notifications.Abstractions;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The From and Repository filters on the phone's Notifications tab
/// (<c>.devbook/domain/notifications/requirements.md</c> — "The phone filters its notifications by
/// system and repository", "Each phone filter counts what it keeps and shows only a real choice",
/// "The phone remembers its filters until the person clears them", "Phone filters never hide what
/// waits"), over both views.
/// </summary>
public sealed class NotificationFilterScreenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 41, 0, TimeSpan.Zero);

    private static readonly PublishedRepository Backlog = new("Backlog");

    private static NotificationSnapshot Asked(string title, string system, int minutesAgo) =>
        TestNotifications.Question(title, Now.AddMinutes(-minutesAgo)) with { Origin = new NotificationOrigin(system, "Somewhere") };

    private static (ShellHost Host, ScriptedNotificationService Service) Phone(params (NotificationSnapshot Notification, PublishedRepository? Repository)[] notifications)
    {
        var service = new ScriptedNotificationService();
        foreach (var (notification, repository) in notifications) service.Publish(notification, Now.AddMinutes(-1), repository: repository);

        return (ShellHost.Paired(clock: new FakeTimeProvider(Now), notifications: service), service);
    }

    private static IReadOnlyList<string> ListedTitles(IRenderedComponent<Routes> app) =>
        [.. app.FindAll("[data-testid='notifications-list'] [data-testid='notification-open']").Select(title => title.TextContent.Trim())];

    private static string Position(IRenderedComponent<Routes> app) =>
        app.Find("[data-testid='notifications-pager-position']").TextContent.Trim();

    private static IReadOnlyList<string> OptionLabels(IRenderedComponent<Routes> app, string filter) =>
        [.. app.FindAll($"[data-testid='notifications-filter-{filter}'] option").Select(option => option.TextContent.Trim())];

    private static void Choose(IRenderedComponent<Routes> app, string filter, string value) =>
        app.Find($"[data-testid='notifications-filter-{filter}'] select").Change(value);

    [Fact]
    public void Picking_a_system_narrows_the_list_and_keeps_the_waiting_count_global()
    {
        var (host, _) = Phone(
            (Asked("Approve the run?", "Claude Code", 3), Backlog),
            (Asked("Which rounding rule?", "spec-manager", 2), Backlog),
            (Asked("Merge the fix?", "Claude Code", 1), null));
        using var disposing = host;

        var app = host.Open("notifications?view=list");
        app.WaitForAssertion(() => Assert.Equal(3, ListedTitles(app).Count));

        Choose(app, "from", "Claude Code");

        app.WaitForAssertion(() => Assert.Equal(["Merge the fix?", "Approve the run?"], ListedTitles(app)));
        Assert.Equal("3 questions waiting", app.Find("[data-testid='notifications-count']").TextContent);
        Assert.Equal("Claude Code", host.NotificationFilters.Current.From);
    }

    [Fact]
    public void Picking_a_system_narrows_the_pagers_stack_and_its_position()
    {
        var (host, _) = Phone(
            (Asked("Approve the run?", "Claude Code", 3), null),
            (Asked("Which rounding rule?", "spec-manager", 2), null),
            (Asked("Merge the fix?", "Claude Code", 1), null));
        using var disposing = host;

        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 3", Position(app)));

        Choose(app, "from", "spec-manager");

        app.WaitForAssertion(() => Assert.Equal("1 / 1", Position(app)));
        Assert.Equal("Which rounding rule?", app.Find("[data-testid='notification-open']").TextContent.Trim());
    }

    [Fact]
    public void Picking_the_brain_keeps_the_pairing_question_and_the_not_known_and_drops_the_repositorys()
    {
        var (host, _) = Phone(
            (Asked("Pair Pixel 9?", "Backlog", 3), PublishedRepository.Brain),
            (Asked("Approve the run?", "Claude Code", 2), Backlog),
            (Asked("Ask from nowhere", "Claude Code", 1), null));
        using var disposing = host;

        var app = host.Open("notifications?view=list");
        app.WaitForAssertion(() => Assert.Equal(3, ListedTitles(app).Count));

        Choose(app, "repository", NotificationFilters.Brain);

        app.WaitForAssertion(() => Assert.Equal(["Ask from nowhere", "Pair Pixel 9?"], ListedTitles(app)));
    }

    [Fact]
    public void Each_option_counts_what_it_keeps_under_the_other_filter()
    {
        var (host, _) = Phone(
            (Asked("A", "Claude Code", 5), Backlog),
            (Asked("B", "Claude Code", 4), Backlog),
            (Asked("C", "spec-manager", 3), Backlog),
            (Asked("D", "spec-manager", 2), new PublishedRepository("fincent")));
        using var disposing = host;

        var app = host.Open("notifications?view=list");
        app.WaitForAssertion(() => Assert.Equal(4, ListedTitles(app).Count));

        Choose(app, "repository", "Backlog");

        app.WaitForAssertion(() => Assert.Equal(["All (3)", "Claude Code (2)", "spec-manager (1)"], OptionLabels(app, "from")));
        Assert.Equal(["All (4)", "Backlog (3)", "fincent (1)"], OptionLabels(app, "repository"));
    }

    /// <summary>Review round 1's blocker: a choice kept as "BACKLOG" while the newest notification
    /// names the repository "Backlog" lists it once, picked, not twice.</summary>
    [Fact]
    public void A_kept_repository_choice_in_another_casing_is_one_picked_option()
    {
        var (host, _) = Phone(
            (Asked("Older", "Claude Code", 5), new PublishedRepository("BACKLOG")),
            (Asked("Newer", "Claude Code", 1), Backlog),
            (Asked("Pair Pixel 9?", "Backlog", 3), PublishedRepository.Brain));
        using var disposing = host;
        host.NotificationFilters.Current = new NotificationFilter(null, "BACKLOG");

        var app = host.Open("notifications?view=list");

        app.WaitForAssertion(() => Assert.Equal(["Newer", "Older"], ListedTitles(app)));
        Assert.Equal(["All (3)", "Brain (1)", "Backlog (2)"], OptionLabels(app, "repository"));
        Assert.Equal("Backlog", app.Find("[data-testid='notifications-filter-repository'] select").GetAttribute("value"));
    }

    [Fact]
    public void A_filter_with_one_option_is_not_shown()
    {
        var (host, _) = Phone(
            (Asked("A", "Claude Code", 2), Backlog),
            (Asked("B", "Claude Code", 1), new PublishedRepository("fincent")));
        using var disposing = host;

        var app = host.Open("notifications?view=list");
        app.WaitForAssertion(() => Assert.Equal(2, ListedTitles(app).Count));

        Assert.Empty(app.FindAll("[data-testid='notifications-filter-from']"));
        Assert.Single(app.FindAll("[data-testid='notifications-filter-repository']"));
    }

    [Fact]
    public void No_filter_at_all_with_one_system_and_no_repository()
    {
        var (host, _) = Phone((Asked("A", "Claude Code", 2), null), (Asked("B", "Claude Code", 1), null));
        using var disposing = host;

        var app = host.Open("notifications?view=list");
        app.WaitForAssertion(() => Assert.Equal(2, ListedTitles(app).Count));

        Assert.Empty(app.FindAll("[data-testid='notifications-filters']"));
    }

    [Fact]
    public void A_choice_made_in_an_earlier_launch_narrows_both_views()
    {
        var (host, _) = Phone(
            (Asked("Approve the run?", "Claude Code", 3), null),
            (Asked("Which rounding rule?", "spec-manager", 2), null));
        using var disposing = host;
        host.NotificationFilters.Current = new NotificationFilter("Claude Code", null);

        var app = host.Open("notifications");

        app.WaitForAssertion(() => Assert.Equal("1 / 1", Position(app)));
        Assert.Equal("Claude Code", app.Find("[data-testid='notifications-filter-from'] select").GetAttribute("value"));

        app.Find("[data-testid='notifications-view-toggle']").Click();

        app.WaitForAssertion(() => Assert.Equal(["Approve the run?"], ListedTitles(app)));
    }

    [Fact]
    public void A_filter_that_leaves_nothing_says_so_and_clear_filters_shows_everything_again()
    {
        var (host, _) = Phone(
            (Asked("Approve the run?", "Claude Code", 3), Backlog),
            (Asked("Which rounding rule?", "spec-manager", 2), null));
        using var disposing = host;
        host.NotificationFilters.Current = new NotificationFilter("Copilot", null);

        var app = host.Open("notifications?view=list");

        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='notifications-filtered-empty']")));
        Assert.Empty(app.FindAll("[data-testid='notifications-empty']"));

        app.Find("[data-testid='notifications-filtered-clear']").Click();

        app.WaitForAssertion(() => Assert.Equal(2, ListedTitles(app).Count));
        Assert.Equal(NotificationFilter.None, host.NotificationFilters.Current);
        Assert.Empty(app.FindAll("[data-testid='notifications-clear-filters']"));
    }
}
