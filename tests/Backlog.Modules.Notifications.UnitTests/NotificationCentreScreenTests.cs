using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.Services;
using Backlog.Modules.Notifications.UI;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using static Backlog.Modules.Notifications.UnitTests.Drafts;

namespace Backlog.Modules.Notifications.UnitTests;

/// <summary>
/// The centre's screens over the real owner ports and an in-memory store: the list grouped by
/// subject with each row's line, the pane that answers through the centre, and the shell's count of
/// open questions.
/// </summary>
public sealed class NotificationCentreScreenTests : IDisposable
{
    private static readonly NotificationSubject Task123 = new("tasks", "task", "T-123");

    private readonly InMemoryNotificationRepository _inner = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly OwnerNotifications _owner;
    private readonly NotificationCentre _centre;
    private readonly BunitContext _context = new();

    public NotificationCentreScreenTests()
    {
        var store = new ObservedNotificationRepository(_inner);
        _owner = new OwnerNotifications(store, new FixedDevice(Owner), new AnswerDelivery([], store, _time), _time);
        _centre = new NotificationCentre(store, _owner, new FixedDevice(Owner), _time);

        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton<INotificationCentre>(_centre);
        _context.Services.AddSingleton<TimeProvider>(_time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _context.Dispose();
        _centre.Dispose();
        _owner.Dispose();
    }

    private async Task<Guid> PublishAsync(NotificationDraft draft)
    {
        var id = (await _owner.PublishAsync(draft, Ct)).Value;
        _time.Advance(TimeSpan.FromMinutes(1));
        return id;
    }

    // --- The list ------------------------------------------------------------------------

    [Fact]
    public async Task The_list_groups_notifications_under_their_subject_with_the_open_question_group_first()
    {
        await PublishAsync(Alert());
        await PublishAsync(Question(subject: Task123));
        await PublishAsync(Question());

        var list = _context.Render<NotificationList>(p => p
            .Add(c => c.Notifications, _centre.ListAsync(Ct).Result)
            .Add(c => c.Now, _time.GetUtcNow())
            .Add(c => c.ThisDeviceId, Owner));

        var groups = list.FindAll("[data-testid='notification-group']");
        Assert.Equal(
            ["Session sess-91", "Task T-123", NotificationPresentation.NoSubjectLabel],
            groups.Select(group => group.QuerySelector("h3")!.TextContent));
    }

    [Fact]
    public async Task A_row_says_its_kind_who_raised_it_and_how_long_ago()
    {
        var id = await PublishAsync(Question());

        var list = _context.Render<NotificationList>(p => p
            .Add(c => c.Notifications, _centre.ListAsync(Ct).Result)
            .Add(c => c.Now, _time.GetUtcNow())
            .Add(c => c.ThisDeviceId, Owner));

        var row = list.Find($"[data-testid='notification-row-{id:N}']");
        Assert.Equal("Question", row.QuerySelector("[data-testid='notification-row-badge']")!.TextContent);
        Assert.Equal("Sessions · 1 min ago", row.QuerySelector("[data-testid='notification-row-line']")!.TextContent.Trim());
    }

    [Fact]
    public async Task An_answered_row_says_the_winning_answer_and_the_device_it_came_from()
    {
        var id = await PublishAsync(Question());
        await _centre.AnswerAsync(id, ["yes"], null, cancellationToken: Ct);

        var list = _context.Render<NotificationList>(p => p
            .Add(c => c.Notifications, _centre.ListAsync(Ct).Result)
            .Add(c => c.Now, _time.GetUtcNow())
            .Add(c => c.ThisDeviceId, Owner));

        var row = list.Find($"[data-testid='notification-row-{id:N}']");
        Assert.Equal("Answered", row.QuerySelector("[data-testid='notification-row-badge']")!.TextContent);
        Assert.Equal("Answered from this desktop · Yes, run it", row.QuerySelector("[data-testid='notification-row-line']")!.TextContent.Trim());
    }

    [Fact]
    public async Task An_answered_row_shows_the_note_beside_the_winning_answer()
    {
        var spec = new AnswerSpec(
            [new AnswerChoice("yes", "Yes, run it"), new AnswerChoice("no", "Not now")],
            Note: new OpenAnswer());
        var id = await PublishAsync(Question(spec));
        await _centre.AnswerAsync(id, ["yes"], null, "After the backup", Ct);

        var list = _context.Render<NotificationList>(p => p
            .Add(c => c.Notifications, _centre.ListAsync(Ct).Result)
            .Add(c => c.Now, _time.GetUtcNow())
            .Add(c => c.ThisDeviceId, Owner));

        var row = list.Find($"[data-testid='notification-row-{id:N}']");
        Assert.Equal(
            "Answered from this desktop · Yes, run it, note: “After the backup”",
            row.QuerySelector("[data-testid='notification-row-line']")!.TextContent.Trim());
    }

    [Fact]
    public async Task A_closed_row_says_why()
    {
        var id = await PublishAsync(Question());
        await _owner.CloseAsync(id, "Session archived", Ct);

        var list = _context.Render<NotificationList>(p => p
            .Add(c => c.Notifications, _centre.ListAsync(Ct).Result)
            .Add(c => c.Now, _time.GetUtcNow())
            .Add(c => c.ThisDeviceId, Owner));

        Assert.Equal("Closed · Session archived", list.Find("[data-testid='notification-row-line']").TextContent.Trim());
    }

    [Fact]
    public async Task Pressing_a_row_selects_it()
    {
        var id = await PublishAsync(Question());
        Guid? selected = null;

        var list = _context.Render<NotificationList>(p => p
            .Add(c => c.Notifications, _centre.ListAsync(Ct).Result)
            .Add(c => c.OnSelect, picked => selected = picked));

        list.Find($"[data-testid='notification-row-{id:N}']").Click();

        Assert.Equal(id, selected);
    }

    [Fact]
    public void An_empty_list_says_nothing_is_waiting()
    {
        var list = _context.Render<NotificationList>(p => p.Add(c => c.Notifications, []));

        Assert.NotNull(list.Find("[data-testid='notification-list-none']"));
    }

    // --- The pane ------------------------------------------------------------------------

    [Fact]
    public async Task The_pane_opens_on_the_open_question_and_answers_it_through_the_centre()
    {
        var id = await PublishAsync(Question());

        var pane = _context.Render<NotificationCentrePane>();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='notification-card-choice-yes']")));
        Assert.Equal("1 waiting for an answer", pane.Find("[data-testid='notification-centre-subtitle']").TextContent);

        pane.Find("[data-testid='notification-card-choice-yes']").Click();

        pane.WaitForAssertion(() => Assert.Equal(
            "Answered from this desktop · Yes, run it · just now",
            pane.Find("[data-testid='notification-card-outcome']").TextContent));
        Assert.Equal(NotificationState.Answered, (await _inner.GetAsync(id, Ct))!.State);
    }

    [Fact]
    public async Task A_refused_answer_shows_its_reason_and_keeps_the_controls()
    {
        var spec = new AnswerSpec([], Open: new OpenAnswer(MaxLength: 5));
        await PublishAsync(Question(spec));

        var pane = _context.Render<NotificationCentrePane>();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='notification-card-open'] textarea")));

        pane.Find("[data-testid='notification-card-open'] textarea").Input("far too long");
        pane.Find("[data-testid='notification-card-send']").Click();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='notification-card-error']")));
        Assert.NotNull(pane.Find("[data-testid='notification-card-send']"));
    }

    [Fact]
    public async Task An_alert_is_acknowledged_from_the_pane()
    {
        var id = await PublishAsync(Alert());

        var pane = _context.Render<NotificationCentrePane>();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='notification-card-acknowledge']")));

        pane.Find("[data-testid='notification-card-acknowledge']").Click();

        pane.WaitForAssertion(() => Assert.Equal(NotificationState.Acknowledged, _inner.GetAsync(id, Ct).Result!.State));
    }

    [Fact]
    public async Task The_pane_opens_on_the_notification_it_is_focused_on()
    {
        var alert = await PublishAsync(Alert());
        await PublishAsync(Question());

        var pane = _context.Render<NotificationCentrePane>(p => p.Add(c => c.FocusNotificationId, alert));

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='notification-card-acknowledge']")));
    }

    [Fact]
    public async Task The_pane_redraws_when_a_question_is_published_while_it_is_open()
    {
        var pane = _context.Render<NotificationCentrePane>();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='notification-list-none']")));

        await PublishAsync(Question());

        pane.WaitForAssertion(() => Assert.Single(pane.FindAll("[data-testid='notification-group']")));
    }

    // --- The shell's count ---------------------------------------------------------------

    [Fact]
    public async Task The_waiting_badge_counts_open_questions_only_and_follows_answers()
    {
        var question = await PublishAsync(Question());
        await PublishAsync(Question(subject: Task123));
        await PublishAsync(Alert());

        var badge = _context.Render<NotificationsWaitingBadge>();
        badge.WaitForAssertion(() => Assert.Equal("2", badge.Find("[data-testid='notifications-waiting']").TextContent));

        await _centre.AnswerAsync(question, ["no"], null, cancellationToken: Ct);

        badge.WaitForAssertion(() => Assert.Equal("1", badge.Find("[data-testid='notifications-waiting']").TextContent));
    }

    [Fact]
    public void The_waiting_badge_draws_nothing_while_nothing_waits()
    {
        var badge = _context.Render<NotificationsWaitingBadge>();

        Assert.Empty(badge.FindAll("[data-testid='notifications-waiting']"));
    }
}
