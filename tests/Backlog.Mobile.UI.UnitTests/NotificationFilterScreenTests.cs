using Backlog.Infrastructure.Sync.Notifications;
using Backlog.Mobile.UI.Components;
using Backlog.Mobile.UI.Notifications;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.UI.Components.Overlays;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The From and Repository filters on the phone's Notifications tab
/// (<c>.devbook/domain/notifications/requirements.md</c> — "The phone filters its notifications by
/// system and repository", "Each phone filter counts what it keeps and shows only a real choice",
/// "The phone remembers its filters until the person clears them", "Phone filters never hide what
/// waits"): behind the filter button at the start of the pager's row, whose badge counts what
/// they leave out.
/// </summary>
public sealed class NotificationFilterScreenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 41, 0, TimeSpan.Zero);

    private static readonly PublishedRepository Backlog = new("Backlog");

    private static NotificationSnapshot Asked(string title, string system, int minutesAgo) =>
        TestNotifications.Question(title, Now.AddMinutes(-minutesAgo)) with { Origin = new NotificationOrigin(system, "Somewhere") };

    private static NotificationSnapshot Settled(string title, string system, int minutesAgo) =>
        TestNotifications.Answered(Asked(title, system, minutesAgo), Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(-2), "yes");

    private static (ShellHost Host, ScriptedNotificationService Service) Phone(params (NotificationSnapshot Notification, PublishedRepository? Repository)[] notifications)
    {
        var service = new ScriptedNotificationService();
        foreach (var (notification, repository) in notifications) service.Publish(notification, Now.AddMinutes(-1), repository: repository);

        return (ShellHost.Paired(clock: new FakeTimeProvider(Now), notifications: service), service);
    }

    private static string Position(IRenderedComponent<Routes> app) =>
        app.Find("[data-testid='notifications-pager-position']").TextContent.Trim();

    private static string Title(IRenderedComponent<Routes> app) =>
        app.Find("[data-testid='notification-open']").TextContent.Trim();

    private static AngleSharp.Dom.IElement FilterButton(IRenderedComponent<Routes> app) =>
        app.Find("[data-testid='notifications-filters-button']");

    /// <summary>Opens the filter button's panel, unless it is open already.</summary>
    private static void OpenFilters(IRenderedComponent<Routes> app)
    {
        if (app.FindAll("[data-testid='notifications-filters-panel']").Count == 0) FilterButton(app).Click();
        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='notifications-filters-panel']")));
    }

    private static IReadOnlyList<string> OptionLabels(IRenderedComponent<Routes> app, string filter)
    {
        OpenFilters(app);
        return [.. app.FindAll($"[data-testid='notifications-filter-{filter}'] option").Select(option => option.TextContent.Trim())];
    }

    private static void Choose(IRenderedComponent<Routes> app, string filter, string value)
    {
        OpenFilters(app);
        app.Find($"[data-testid='notifications-filter-{filter}'] select").Change(value);
    }

    private static string? Hidden(IRenderedComponent<Routes> app) =>
        app.FindAll("[data-testid='notifications-filters-hidden']").SingleOrDefault()?.TextContent.Trim();

    // The filter button (Personal Validation's revise: one row above the card)

    [Fact]
    public void The_filters_sit_behind_a_filter_button_at_the_start_of_the_pagers_row()
    {
        var (host, _) = Phone(
            (Asked("Approve the run?", "Claude Code", 3), Backlog),
            (Asked("Which rounding rule?", "spec-manager", 2), new PublishedRepository("fincent")));
        using var disposing = host;

        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));

        var button = app.Find("[data-testid='notifications-pager'] .notifications-pager__nav .notifications-pager__leading [data-testid='notifications-filters-button']");
        Assert.Equal("Filter notifications", button.GetAttribute("aria-label"));
        Assert.Equal("false", button.GetAttribute("aria-expanded"));
        Assert.NotNull(button.QuerySelector("svg.list-filter-icon"));
        Assert.Empty(app.FindAll("[data-testid='notifications-filter-from']"));

        OpenFilters(app);

        Assert.Equal("true", FilterButton(app).GetAttribute("aria-expanded"));
        Assert.Equal(["From", "Repository"], app.FindAll("[data-testid='notifications-filters-panel'] .field__label").Select(label => label.TextContent.Trim()));
        Assert.Empty(app.FindAll("[data-testid='notifications-clear-filters']"));
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
        Assert.Equal("Which rounding rule?", Title(app));
        Assert.Equal("spec-manager", host.NotificationFilters.Current.From);
    }

    [Fact]
    public void Under_a_filter_the_button_is_active_and_its_badge_counts_what_waits_elsewhere()
    {
        var (host, _) = Phone(
            (Asked("Approve the run?", "Claude Code", 3), Backlog),
            (Asked("Which rounding rule?", "spec-manager", 2), Backlog),
            (Asked("Merge the fix?", "Claude Code", 1), null));
        using var disposing = host;

        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 3", Position(app)));
        Assert.Null(Hidden(app));
        Assert.DoesNotContain("notifications-filters__trigger--active", FilterButton(app).ClassList);

        Choose(app, "from", "Claude Code");

        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        Assert.Equal("1", Hidden(app));
        Assert.Equal("Filter notifications, filters on, 1 waiting elsewhere", FilterButton(app).GetAttribute("aria-label"));
        Assert.Contains("notifications-filters__trigger--active", FilterButton(app).ClassList);
    }

    [Fact]
    public void A_filter_that_hides_nothing_waiting_draws_no_badge_and_still_says_it_is_on()
    {
        var (host, _) = Phone(
            (Asked("Approve the run?", "Claude Code", 3), Backlog),
            (Settled("Already answered", "spec-manager", 4), Backlog));
        using var disposing = host;
        host.NotificationFilters.Current = new NotificationFilter("Claude Code", null);

        var app = host.Open("notifications");

        app.WaitForAssertion(() => Assert.Equal("1 / 1", Position(app)));
        Assert.Null(Hidden(app));
        // In force is said, not only drawn in gold (accessibility.md: name, role and state).
        Assert.Equal("Filter notifications, filters on", FilterButton(app).GetAttribute("aria-label"));
        Assert.Contains("notifications-filters__trigger--active", FilterButton(app).ClassList);
    }

    [Fact]
    public void Under_a_filter_the_panel_offers_clear_filters_which_clears_them()
    {
        var (host, _) = Phone(
            (Asked("Approve the run?", "Claude Code", 3), Backlog),
            (Asked("Which rounding rule?", "spec-manager", 2), Backlog));
        using var disposing = host;
        host.NotificationFilters.Current = new NotificationFilter("Claude Code", null);

        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 1", Position(app)));
        Assert.Equal("1", Hidden(app));

        OpenFilters(app);
        var clear = app.Find("[data-testid='notifications-filters-panel'] [data-testid='notifications-clear-filters']");
        Assert.Equal("Clear filters", clear.TextContent.Trim());

        clear.Click();

        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        Assert.Equal(NotificationFilter.None, host.NotificationFilters.Current);
        Assert.Null(Hidden(app));
        Assert.Empty(app.FindAll("[data-testid='notifications-clear-filters']"));
    }

    // Counts of what waits

    [Fact]
    public void A_settled_notification_counts_in_no_option_and_not_in_the_pager()
    {
        var (host, _) = Phone(
            (Asked("Approve the run?", "Claude Code", 3), Backlog),
            (Settled("Already answered", "spec-manager", 4), Backlog),
            (Asked("Which rounding rule?", "spec-manager", 2), new PublishedRepository("fincent")));
        using var disposing = host;

        var app = host.Open("notifications");

        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        Assert.Equal(["All systems (2)", "Claude Code (1)", "spec-manager (1)"], OptionLabels(app, "from"));
        Assert.Equal(["All repos (2)", "Backlog (1)", "fincent (1)"], OptionLabels(app, "repository"));
    }

    [Fact]
    public void An_option_whose_notifications_are_all_settled_stays_and_counts_zero()
    {
        var (host, _) = Phone(
            (Asked("Approve the run?", "Claude Code", 3), Backlog),
            (Settled("Already answered", "spec-manager", 4), Backlog));
        using var disposing = host;

        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 1", Position(app)));

        Assert.Equal(["All systems (1)", "Claude Code (1)", "spec-manager (0)"], OptionLabels(app, "from"));
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

        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 4", Position(app)));

        Choose(app, "repository", "Backlog");

        app.WaitForAssertion(() => Assert.Equal(["All systems (3)", "Claude Code (2)", "spec-manager (1)"], OptionLabels(app, "from")));
        Assert.Equal(["All repos (4)", "Backlog (3)", "fincent (1)"], OptionLabels(app, "repository"));
    }

    // What a choice keeps

    [Fact]
    public void Picking_the_brain_keeps_the_pairing_question_and_the_not_known_and_drops_the_repositorys()
    {
        var (host, _) = Phone(
            (Asked("Pair Pixel 9?", "Backlog", 3), PublishedRepository.Brain),
            (Asked("Approve the run?", "Claude Code", 2), Backlog),
            (Asked("Ask from nowhere", "Claude Code", 1), null));
        using var disposing = host;

        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 3", Position(app)));

        Choose(app, "repository", NotificationFilters.Brain);

        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        Assert.Equal("Pair Pixel 9?", Title(app));
        app.Find("[data-testid='notifications-pager-next']").Click();
        app.WaitForAssertion(() => Assert.Equal("Ask from nowhere", Title(app)));
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

        var app = host.Open("notifications");

        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        Assert.Equal("Older", Title(app));
        Assert.Equal(["All repos (3)", "Brain (1)", "Backlog (2)"], OptionLabels(app, "repository"));
        Assert.Equal("Backlog", app.Find("[data-testid='notifications-filter-repository'] select").GetAttribute("value"));
    }

    [Fact]
    public void A_filter_with_one_option_is_not_shown()
    {
        var (host, _) = Phone(
            (Asked("A", "Claude Code", 2), Backlog),
            (Asked("B", "Claude Code", 1), new PublishedRepository("fincent")));
        using var disposing = host;

        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        OpenFilters(app);

        Assert.Empty(app.FindAll("[data-testid='notifications-filter-from']"));
        Assert.Single(app.FindAll("[data-testid='notifications-filter-repository']"));
    }

    [Fact]
    public void No_filter_button_with_one_system_and_no_repository()
    {
        var (host, _) = Phone((Asked("A", "Claude Code", 2), null), (Asked("B", "Claude Code", 1), null));
        using var disposing = host;

        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));

        Assert.Empty(app.FindAll("[data-testid='notifications-filters']"));
    }

    // Remembered, and cleared

    [Fact]
    public void A_choice_made_in_an_earlier_launch_narrows_the_pager()
    {
        var (host, _) = Phone(
            (Asked("Approve the run?", "Claude Code", 3), null),
            (Asked("Which rounding rule?", "spec-manager", 2), null));
        using var disposing = host;
        host.NotificationFilters.Current = new NotificationFilter("Claude Code", null);

        var app = host.Open("notifications");

        app.WaitForAssertion(() => Assert.Equal("1 / 1", Position(app)));
        Assert.Equal("Approve the run?", Title(app));
        OpenFilters(app);
        Assert.Equal("Claude Code", app.Find("[data-testid='notifications-filter-from'] select").GetAttribute("value"));
    }

    [Fact]
    public void A_filter_that_leaves_nothing_says_so_and_clear_filters_shows_everything_again()
    {
        var (host, _) = Phone(
            (Asked("Approve the run?", "Claude Code", 3), Backlog),
            (Asked("Which rounding rule?", "spec-manager", 2), null));
        using var disposing = host;
        host.NotificationFilters.Current = new NotificationFilter("Copilot", null);

        var app = host.Open("notifications");

        // The pager stays, its surface saying so and its row holding the filter button alone.
        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='notifications-pager-surface'] [data-testid='notifications-filtered-empty']")));
        Assert.Empty(app.FindAll("[data-testid='notifications-pager-previous']"));
        Assert.Empty(app.FindAll("[data-testid='notifications-pager-position']"));
        Assert.Equal("2", Hidden(app));

        app.Find("[data-testid='notifications-filtered-clear']").Click();

        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        Assert.Equal(NotificationFilter.None, host.NotificationFilters.Current);
        Assert.Empty(app.FindAll("[data-testid='notifications-clear-filters']"));
    }

    /// <summary>Review round 3's blocker: a choice that leaves nothing must not draw the filter
    /// button afresh — its panel stays open on the select the person was on, and the focus is
    /// not moved.</summary>
    [Fact]
    public void A_choice_that_leaves_nothing_keeps_the_filter_button_and_its_open_panel()
    {
        var (host, _) = Phone(
            (Asked("Approve the run?", "Claude Code", 3), Backlog),
            (Asked("Which rounding rule?", "spec-manager", 2), new PublishedRepository("fincent")));
        using var disposing = host;

        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        Choose(app, "repository", "fincent");
        app.WaitForAssertion(() => Assert.Equal("1 / 1", Position(app)));
        var popover = app.FindComponent<PopoverButton>().Instance;
        var focuses = Focuses(host);

        Choose(app, "from", "Claude Code");

        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='notifications-filtered-empty']")));
        Assert.Same(popover, app.FindComponent<PopoverButton>().Instance);
        Assert.Single(app.FindAll("[data-testid='notifications-filters-panel']"));
        Assert.Equal("Claude Code", app.Find("[data-testid='notifications-filter-from'] select").GetAttribute("value"));
        Assert.Equal(focuses, Focuses(host));

        // And back: picking All under From again brings the pager back under the same button.
        Choose(app, "from", string.Empty);

        app.WaitForAssertion(() => Assert.Equal("1 / 1", Position(app)));
        Assert.Same(popover, app.FindComponent<PopoverButton>().Instance);
        Assert.Single(app.FindAll("[data-testid='notifications-filters-panel']"));
    }

    [Fact]
    public void Changing_a_filter_leaves_the_focus_in_the_panel()
    {
        var (host, _) = Phone(
            (Asked("Approve the run?", "Claude Code", 3), null),
            (Asked("Which rounding rule?", "spec-manager", 2), null));
        using var disposing = host;

        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        var focuses = Focuses(host);

        Choose(app, "from", "spec-manager");

        app.WaitForAssertion(() => Assert.Equal("1 / 1", Position(app)));
        Assert.Equal("Which rounding rule?", Title(app));
        Assert.Equal(focuses, Focuses(host));
    }

    private static int Focuses(ShellHost host) =>
        host.JSInterop.Invocations.Count(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus");
}
