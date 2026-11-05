using Backlog.Mobile.UI.Notifications;
using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The pager's stack on the phone's Notifications tab
/// (<c>.devbook/domain/notifications/features.md#answering-from-another-device</c>): which
/// notifications wait on the person, one per screen oldest first, and how the pager moves
/// through them, skips, answers and takes a refresh.
/// </summary>
public sealed class NotificationStackTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private static PhoneNotification Phone(
        string title,
        int minutesAgo,
        NotificationStanding standing = NotificationStanding.Answerable,
        AnswerSpec? spec = null) =>
        new(
            standing == NotificationStanding.Alert
                ? TestNotifications.Alert(title, Now.AddMinutes(-minutesAgo))
                : TestNotifications.Question(title, Now.AddMinutes(-minutesAgo), spec),
            "WORKSTATION-01",
            OwnerPresence.Online,
            OwnerSeenAt: null,
            standing,
            Verdict: null,
            Waiting: null);

    private static PhoneNotification As(PhoneNotification notification, NotificationStanding standing) =>
        notification with { Standing = standing };

    private static IReadOnlyList<string> Titles(NotificationStack stack) =>
        [.. stack.Items.Select(item => item.Notification.Title)];

    // Stack membership (criterion 2)

    [Theory]
    [InlineData(NotificationStanding.Answerable)]
    [InlineData(NotificationStanding.Rejected)]
    [InlineData(NotificationStanding.Alert)]
    [InlineData(NotificationStanding.OwnerOnly)]
    public void An_open_item_waiting_on_the_person_is_in_the_stack(NotificationStanding standing) =>
        Assert.True(NotificationStack.IsWaiting(Phone("Deploy?", 5, standing)));

    [Theory]
    [InlineData(NotificationStanding.Sending)]
    [InlineData(NotificationStanding.AwaitingOwner)]
    [InlineData(NotificationStanding.AnsweredHere)]
    [InlineData(NotificationStanding.AnsweredElsewhere)]
    [InlineData(NotificationStanding.Acknowledged)]
    [InlineData(NotificationStanding.Closed)]
    public void An_item_already_answered_or_settled_is_not_in_the_stack(NotificationStanding standing) =>
        Assert.False(NotificationStack.IsWaiting(Phone("Deploy?", 5, standing)));

    [Fact]
    public void The_stack_holds_only_waiting_items_oldest_first()
    {
        // The projection's order: open newest first, then settled.
        var stack = new NotificationStack(
        [
            Phone("Newest", 1),
            Phone("Sent already", 2, NotificationStanding.Sending),
            Phone("Middle alert", 10, NotificationStanding.Alert),
            Phone("Oldest", 30, NotificationStanding.OwnerOnly),
            Phone("Settled", 60, NotificationStanding.AnsweredElsewhere)
        ]);

        Assert.Equal(["Oldest", "Middle alert", "Newest"], Titles(stack));
    }

    // One per screen and navigation (criteria 3 and 4)

    [Fact]
    public void The_pager_opens_on_the_oldest_item_at_position_one()
    {
        var stack = new NotificationStack([Phone("Newer", 1), Phone("Older", 9)]);

        Assert.Equal("Older", stack.Current?.Notification.Title);
        Assert.Equal(1, stack.Position);
        Assert.Equal(2, stack.Total);
        Assert.Null(stack.End);
    }

    [Fact]
    public void Next_and_previous_move_by_one()
    {
        var stack = new NotificationStack([Phone("C", 1), Phone("B", 2), Phone("A", 3)]);

        stack.Next();
        Assert.Equal(("B", 2), (stack.Current?.Notification.Title, stack.Position));

        stack.Next();
        Assert.Equal(("C", 3), (stack.Current?.Notification.Title, stack.Position));

        stack.Previous();
        Assert.Equal(("B", 2), (stack.Current?.Notification.Title, stack.Position));
    }

    [Fact]
    public void Previous_on_the_first_item_does_nothing()
    {
        var stack = new NotificationStack([Phone("B", 1), Phone("A", 2)]);

        Assert.False(stack.CanGoPrevious);
        stack.Previous();

        Assert.Equal(("A", 1), (stack.Current?.Notification.Title, stack.Position));
    }

    [Fact]
    public void Next_past_the_last_item_reaches_the_end_and_goes_no_further()
    {
        var stack = new NotificationStack([Phone("A", 1)]);

        stack.Next();

        Assert.Null(stack.Current);
        Assert.Null(stack.Position);
        Assert.NotNull(stack.End);
        Assert.False(stack.CanGoNext);

        stack.Next();
        Assert.Null(stack.Current);
    }

    [Fact]
    public void Previous_from_the_end_returns_to_the_last_item()
    {
        var stack = new NotificationStack([Phone("B", 1), Phone("A", 2)]);
        stack.Next();
        stack.Next();

        Assert.True(stack.CanGoPrevious);
        stack.Previous();

        Assert.Equal(("B", 2), (stack.Current?.Notification.Title, stack.Position));
    }

    // Skipped stays open, end of stack (criteria 7 and 8)

    [Fact]
    public void An_empty_stack_opens_on_all_caught_up()
    {
        var stack = new NotificationStack([Phone("Settled", 5, NotificationStanding.Closed)]);

        Assert.Null(stack.Current);
        Assert.Equal(0, stack.Total);
        Assert.True(stack.End?.AllCaughtUp);
    }

    [Fact]
    public void Items_moved_past_unanswered_stay_in_the_stack_and_the_end_counts_them()
    {
        var stack = new NotificationStack([Phone("B", 1), Phone("A", 2)]);

        stack.Next();
        stack.Next();

        Assert.Equal(["A", "B"], Titles(stack));
        Assert.Equal(new NotificationStackEnd(2), stack.End);
        Assert.False(stack.End?.AllCaughtUp);
    }

    [Fact]
    public void Going_through_them_again_lands_on_the_first_skipped_item()
    {
        var stack = new NotificationStack([Phone("C", 1), Phone("B", 2), Phone("A", 3)]);
        stack.Next();
        stack.AfterAnswer(stack.Current!.Id);
        stack.Next();

        stack.Restart();

        Assert.Equal(("A", 1), (stack.Current?.Notification.Title, stack.Position));
        Assert.Equal(2, stack.Total);
    }

    // After answering (criterion 6)

    [Fact]
    public void An_answered_item_leaves_and_the_pager_moves_to_the_next()
    {
        var stack = new NotificationStack([Phone("C", 1), Phone("B", 2), Phone("A", 3)]);

        stack.AfterAnswer(stack.Current!.Id);

        Assert.Equal(("B", 1), (stack.Current?.Notification.Title, stack.Position));
        Assert.Equal(["B", "C"], Titles(stack));
    }

    [Fact]
    public void Answering_the_last_item_with_earlier_ones_skipped_reports_them()
    {
        var stack = new NotificationStack([Phone("B", 1), Phone("A", 2)]);
        stack.Next();

        stack.AfterAnswer(stack.Current!.Id);

        Assert.Null(stack.Current);
        Assert.Equal(new NotificationStackEnd(1), stack.End);
    }

    [Fact]
    public void Answering_the_only_item_is_all_caught_up()
    {
        var stack = new NotificationStack([Phone("A", 1)]);

        stack.AfterAnswer(stack.Current!.Id);

        Assert.True(stack.End?.AllCaughtUp);
    }

    [Fact]
    public void Answering_an_item_not_on_screen_keeps_the_current_one()
    {
        var stack = new NotificationStack([Phone("C", 1), Phone("B", 2), Phone("A", 3)]);
        var oldest = stack.Current!.Id;
        stack.Next();
        stack.Next();

        stack.AfterAnswer(oldest);

        Assert.Equal(("C", 2), (stack.Current?.Notification.Title, stack.Position));
    }

    // Live updates (criterion 9)

    [Fact]
    public void A_refresh_keeps_the_current_item_while_it_still_waits()
    {
        var a = Phone("A", 3);
        var b = Phone("B", 2);
        var c = Phone("C", 1);
        var stack = new NotificationStack([c, b, a]);
        stack.Next();

        stack.Refresh([c, b, As(a, NotificationStanding.AnsweredElsewhere)]);

        Assert.Equal(("B", 1), (stack.Current?.Notification.Title, stack.Position));
        Assert.Equal(2, stack.Total);
    }

    [Fact]
    public void New_arrivals_join_the_stack_without_moving_the_current_item()
    {
        var a = Phone("A", 3);
        var b = Phone("B", 2);
        var stack = new NotificationStack([b, a]);
        stack.Next();

        stack.Refresh([Phone("Newest", 0), b, a, Phone("Late older", 9)]);

        Assert.Equal("B", stack.Current?.Notification.Title);
        Assert.Equal(["Late older", "A", "B", "Newest"], Titles(stack));
    }

    [Fact]
    public void A_current_item_that_left_gives_way_to_the_next_one_after_it()
    {
        var a = Phone("A", 3);
        var b = Phone("B", 2);
        var c = Phone("C", 1);
        var stack = new NotificationStack([c, b, a]);
        stack.Next();

        stack.Refresh([c, As(b, NotificationStanding.Closed), a]);

        Assert.Equal(("C", 2), (stack.Current?.Notification.Title, stack.Position));
    }

    [Fact]
    public void A_last_item_that_left_gives_way_to_the_end()
    {
        var a = Phone("A", 2);
        var b = Phone("B", 1);
        var stack = new NotificationStack([b, a]);
        stack.Next();

        stack.Refresh([As(b, NotificationStanding.AnsweredElsewhere), a]);

        Assert.Null(stack.Current);
        Assert.Equal(new NotificationStackEnd(1), stack.End);
    }

    [Fact]
    public void A_refresh_that_empties_the_stack_is_all_caught_up()
    {
        var a = Phone("A", 1);
        var stack = new NotificationStack([a]);

        stack.Refresh([As(a, NotificationStanding.Closed)]);

        Assert.True(stack.End?.AllCaughtUp);
    }

    [Fact]
    public void A_refresh_at_the_end_stays_at_the_end_and_counts_an_arrival_as_new()
    {
        var a = Phone("A", 2);
        var stack = new NotificationStack([a]);
        stack.Next();

        stack.Refresh([Phone("Arrived", 0), a]);

        Assert.Null(stack.Current);
        Assert.Equal(new NotificationStackEnd(Skipped: 1, New: 1), stack.End);
    }

    [Fact]
    public void An_arrival_on_an_empty_stack_is_new_not_skipped()
    {
        var stack = new NotificationStack([]);

        stack.Refresh([Phone("Arrived", 0)]);

        Assert.Equal(new NotificationStackEnd(Skipped: 0, New: 1), stack.End);
        Assert.False(stack.End?.AllCaughtUp);
    }

    [Fact]
    public void An_older_arrival_before_the_current_item_is_new_not_skipped()
    {
        var a = Phone("A", 3);
        var b = Phone("B", 2);
        var stack = new NotificationStack([b, a]);
        stack.Next();

        stack.Refresh([b, a, Phone("Late older", 9)]);
        stack.Next();

        Assert.Equal(new NotificationStackEnd(Skipped: 2, New: 1), stack.End);
    }

    [Fact]
    public void Going_through_them_again_lands_on_the_oldest_of_the_skipped_and_new_items()
    {
        var a = Phone("A", 2);
        var stack = new NotificationStack([a]);
        stack.AfterAnswer(a.Id);

        stack.Refresh([Phone("Late older", 9), As(a, NotificationStanding.Sending), Phone("Newer", 0)]);
        stack.Restart();

        Assert.Equal(("Late older", 1), (stack.Current?.Notification.Title, stack.Position));
    }

    [Fact]
    public void A_reply_the_owner_rejected_brings_the_item_back()
    {
        var a = Phone("A", 2);
        var b = Phone("B", 1);
        var stack = new NotificationStack([b, a]);
        stack.AfterAnswer(a.Id);

        stack.Refresh([b, As(a, NotificationStanding.Rejected)]);

        Assert.Equal(["A", "B"], Titles(stack));
        Assert.Equal("B", stack.Current?.Notification.Title);
    }

    // An answer being confirmed (criterion 6, against the projection raising Changed at once)

    [Fact]
    public void The_card_being_answered_stays_on_screen_through_a_refresh_until_after_the_answer()
    {
        var a = Phone("A", 3);
        var b = Phone("B", 2);
        var stack = new NotificationStack([b, a]);

        stack.BeginAnswer(a.Id);
        stack.Refresh([b, As(a, NotificationStanding.Sending)]);

        Assert.Equal(("A", 1), (stack.Current?.Notification.Title, stack.Position));
        Assert.Equal(2, stack.Total);

        stack.AfterAnswer(a.Id);

        Assert.Equal(("B", 1), (stack.Current?.Notification.Title, stack.Position));
        Assert.Equal(1, stack.Total);
    }

    [Fact]
    public void After_the_answer_a_refresh_no_longer_holds_the_card()
    {
        var a = Phone("A", 3);
        var b = Phone("B", 2);
        var stack = new NotificationStack([b, a]);
        stack.BeginAnswer(a.Id);
        stack.AfterAnswer(a.Id);

        stack.Refresh([b, As(a, NotificationStanding.Sending)]);

        Assert.Equal(["B"], Titles(stack));
    }

    [Fact]
    public void An_answer_the_phone_could_not_keep_leaves_the_card_waiting()
    {
        var a = Phone("A", 3);
        var b = Phone("B", 2);
        var stack = new NotificationStack([b, a]);
        stack.BeginAnswer(a.Id);

        stack.CancelAnswer();
        stack.Refresh([b, a]);

        Assert.Equal(("A", 1), (stack.Current?.Notification.Title, stack.Position));
        Assert.Equal(2, stack.Total);
    }
}
