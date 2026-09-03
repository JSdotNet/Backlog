namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The channel between a screen that has something to say and the tray that says
/// it.
/// <para>
/// It is tested without rendering anything, and that is the point of it holding
/// the queue rather than the tray holding it: the "no more than three at once"
/// rule from <c>.design/interaction-guidelines.md#feedback-and-toasts</c> is a
/// design decision, not a layout accident, so it is stated in one place and
/// checked here rather than being read back out of markup.
/// </para>
/// </summary>
public sealed class ToastChannelTests
{
    [Fact]
    public void A_published_message_is_visible_and_a_dismissed_one_is_not()
    {
        var channel = new ToastChannel();
        var message = ToastMessage.Info("Imported: 2 created.", "import-plan-result");

        channel.Publish(message);

        var visible = Assert.Single(channel.Visible);
        Assert.Equal("Imported: 2 created.", visible.Message);
        Assert.Equal("import-plan-result", visible.TestId);
        Assert.Equal(ToastSeverity.Info, visible.Severity);

        channel.Dismiss(message.Id);

        Assert.Empty(channel.Visible);
    }

    /// <summary>Dismissing something that is not there is a no-op rather than a
    /// throw. Toast auto-dismisses on a timer of its own, so the tray can raise
    /// the same id twice — once from the timer and once from the button — and a
    /// channel that threw on the second would take the whole circuit down for a
    /// message that had already gone.</summary>
    [Fact]
    public void Dismissing_an_unknown_message_changes_nothing()
    {
        var channel = new ToastChannel();
        channel.Publish(ToastMessage.Info("Still here."));

        channel.Dismiss(Guid.NewGuid());

        Assert.Single(channel.Visible);
    }

    [Fact]
    public void Three_show_and_the_rest_wait_their_turn()
    {
        var channel = new ToastChannel();
        var messages = Enumerable.Range(1, 4)
            .Select(number => ToastMessage.Info($"Message {number}"))
            .ToArray();

        foreach (var message in messages) channel.Publish(message);

        Assert.Equal(ToastChannel.MaxVisible, channel.Visible.Count);
        Assert.Equal(
            ["Message 1", "Message 2", "Message 3"],
            channel.Visible.Select(toast => toast.Message).ToArray());

        // Dismissing one is what promotes the fourth: the queue is a queue, not a
        // window that drops what does not fit.
        channel.Dismiss(messages[0].Id);

        Assert.Equal(
            ["Message 2", "Message 3", "Message 4"],
            channel.Visible.Select(toast => toast.Message).ToArray());
    }

    [Fact]
    public void Both_operations_tell_the_tray_to_redraw()
    {
        var channel = new ToastChannel();
        var raised = 0;
        channel.Changed += () => raised++;

        var message = ToastMessage.Error("Couldn't push to GitHub.");
        channel.Publish(message);

        Assert.Equal(1, raised);

        channel.Dismiss(message.Id);

        Assert.Equal(2, raised);
    }

    /// <summary>
    /// Publishers are background continuations — a GitHub sync, a Copilot CLI run,
    /// an AI call — so nothing about this channel may assume the renderer's thread.
    /// </summary>
    [Fact]
    public async Task Publishing_from_many_threads_loses_nothing()
    {
        var channel = new ToastChannel();
        const int perThread = 50;
        const int threads = 8;

        await Task.WhenAll(Enumerable.Range(0, threads).Select(thread => Task.Run(() =>
        {
            for (var number = 0; number < perThread; number++)
            {
                channel.Publish(ToastMessage.Info($"{thread}-{number}"));
            }
        })));

        // Drained through the visible window, which is the only way anything ever
        // leaves: if a concurrent publish had been lost or double-counted the
        // drain would come up short or never end.
        var drained = 0;
        while (channel.Visible.Count > 0)
        {
            channel.Dismiss(channel.Visible[0].Id);
            drained++;
        }

        Assert.Equal(threads * perThread, drained);
    }

    /// <summary>The severity a factory picks and the time it stays up are one
    /// decision, taken here so no caller has to remember that an error is owed
    /// longer on screen than a result.</summary>
    [Fact]
    public void The_factories_pair_a_severity_with_how_long_it_stays_up()
    {
        Assert.Equal(ToastSeverity.Info, ToastMessage.Info("x").Severity);
        Assert.Equal(ToastMessage.DefaultDuration, ToastMessage.Info("x").DurationMilliseconds);

        Assert.Equal(ToastSeverity.Warning, ToastMessage.Warning("x").Severity);
        Assert.Equal(ToastMessage.ErrorDuration, ToastMessage.Warning("x").DurationMilliseconds);

        Assert.Equal(ToastSeverity.Error, ToastMessage.Error("x").Severity);
        Assert.Equal(ToastMessage.ErrorDuration, ToastMessage.Error("x").DurationMilliseconds);
    }

    /// <summary>Every message is its own identity from the moment it is made. The
    /// tray keys on it, so two messages reading the same words must still be two
    /// toasts rather than one that never re-enters.</summary>
    [Fact]
    public void Two_messages_with_the_same_words_are_still_two_messages()
    {
        var one = ToastMessage.Info("Saved.");
        var two = ToastMessage.Info("Saved.");

        Assert.NotEqual(one.Id, two.Id);
    }
}
