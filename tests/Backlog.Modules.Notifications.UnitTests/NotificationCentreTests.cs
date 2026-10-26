using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.Extensions;
using Backlog.Modules.Notifications.Services;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using static Backlog.Modules.Notifications.UnitTests.Drafts;

namespace Backlog.Modules.Notifications.UnitTests;

/// <summary>
/// The owner's <see cref="INotificationCentre"/>: what the shell's centre and the Windows
/// notifications list and answer through, and the change events both redraw on.
/// </summary>
public sealed class NotificationCentreTests
{
    private readonly InMemoryNotificationRepository _inner = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly ObservedNotificationRepository _store;
    private readonly OwnerNotifications _owner;
    private readonly NotificationCentre _centre;

    public NotificationCentreTests()
    {
        _store = new ObservedNotificationRepository(_inner);
        _owner = new OwnerNotifications(_store, new FixedDevice(Owner), new AnswerDelivery([], _store, _time), _time);
        _centre = new NotificationCentre(_store, _owner, new FixedDevice(Owner), _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_open_notifications_newest_first()
    {
        var first = (await _owner.PublishAsync(Question(), Ct)).Value;
        _time.Advance(TimeSpan.FromMinutes(1));
        var second = (await _owner.PublishAsync(Alert(), Ct)).Value;

        var listed = await _centre.ListAsync(Ct);

        Assert.Equal([second, first], listed.Select(view => view.Id));
    }

    [Fact]
    public async Task Keeps_a_settled_notification_for_a_week_and_then_drops_it()
    {
        var id = (await _owner.PublishAsync(Alert(), Ct)).Value;
        await _centre.AcknowledgeAsync(id, Ct);

        _time.Advance(TimeSpan.FromDays(6));
        Assert.Single(await _centre.ListAsync(Ct));

        _time.Advance(TimeSpan.FromDays(2));
        Assert.Empty(await _centre.ListAsync(Ct));
    }

    [Fact]
    public async Task An_answer_given_here_is_accepted_through_the_reply_path_from_this_device()
    {
        var id = (await _owner.PublishAsync(Question(), Ct)).Value;

        var answer = await _centre.AnswerAsync(id, ["yes"], null, cancellationToken: Ct);

        Assert.True(answer.IsSuccess);
        Assert.Equal(Owner, answer.Value.FromDeviceId);
        var view = Assert.Single(await _centre.ListAsync(Ct));
        Assert.Equal(NotificationState.Answered, view.State);
        Assert.Equal(["yes"], view.Answer!.ChoiceIds);
    }

    [Fact]
    public async Task An_invalid_answer_is_rejected_with_its_reason_and_the_question_stays_open()
    {
        var id = (await _owner.PublishAsync(Question(), Ct)).Value;

        var answer = await _centre.AnswerAsync(id, ["maybe"], null, cancellationToken: Ct);

        Assert.Equal("reply.invalid", answer.Error.Code);
        Assert.True(Assert.Single(await _centre.ListAsync(Ct)).IsOpenQuestion);
    }

    [Fact]
    public async Task An_open_answer_carries_its_text()
    {
        var spec = new AnswerSpec([], Open: new OpenAnswer("Write an answer…", 200));
        var id = (await _owner.PublishAsync(Question(spec), Ct)).Value;

        var answer = await _centre.AnswerAsync(id, [], "Ship it on Friday", cancellationToken: Ct);

        Assert.Equal("Ship it on Friday", answer.Value.Text);
    }

    [Fact]
    public async Task An_answer_carries_the_note_written_beside_it()
    {
        var spec = new AnswerSpec(
            [new AnswerChoice("yes", "Yes"), new AnswerChoice("no", "No")],
            Note: new OpenAnswer("Add a note", 4000));
        var id = (await _owner.PublishAsync(Question(spec), Ct)).Value;

        var answer = await _centre.AnswerAsync(id, ["yes"], null, "After the backup", Ct);

        Assert.Equal(["yes"], answer.Value.ChoiceIds);
        Assert.Equal("After the backup", answer.Value.Note);
        Assert.Equal("After the backup", Assert.Single(await _centre.ListAsync(Ct)).Answer!.Note);
    }

    [Fact]
    public async Task Acknowledging_an_alert_settles_it()
    {
        var id = (await _owner.PublishAsync(Alert(), Ct)).Value;

        Assert.True((await _centre.AcknowledgeAsync(id, Ct)).IsSuccess);
        Assert.Equal(NotificationState.Acknowledged, Assert.Single(await _centre.ListAsync(Ct)).State);
    }

    [Fact]
    public async Task Raises_changed_for_a_publish_as_new_and_for_a_settle_as_not_new()
    {
        var changes = new List<NotificationChanged>();
        _centre.Changed += (_, change) => changes.Add(change);

        var id = (await _owner.PublishAsync(Question(), Ct)).Value;
        await _owner.CloseAsync(id, "Session archived", Ct);

        Assert.Collection(
            changes,
            published => Assert.True(published.IsNew && published.Notification.IsOpenQuestion),
            closed =>
            {
                Assert.False(closed.IsNew);
                Assert.Equal(NotificationState.Closed, closed.Notification.State);
                Assert.Equal("Session archived", closed.Notification.CloseReason);
            });
    }

    [Fact]
    public async Task A_throwing_listener_does_not_undo_the_write()
    {
        _centre.Changed += (_, _) => throw new InvalidOperationException("A screen fell over.");

        var id = (await _owner.PublishAsync(Question(), Ct)).Value;

        Assert.NotNull(await _inner.GetAsync(id, Ct));
    }

    [Fact]
    public void The_owner_role_registers_the_centre_over_the_observed_store()
    {
        var services = new ServiceCollection()
            .AddSingleton<INotificationRepository>(_inner)
            .AddSingleton<IDeviceIdentitySource>(new FixedDevice(Owner))
            .AddNotificationsModule();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<NotificationCentre>(provider.GetRequiredService<INotificationCentre>());
    }

    [Fact]
    public void The_viewer_role_registers_no_centre()
    {
        using var provider = new ServiceCollection().AddNotificationsModule(NotificationsRole.Viewer).BuildServiceProvider();

        Assert.Null(provider.GetService<INotificationCentre>());
    }
}
