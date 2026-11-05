using Backlog.Mobile.UI.Components.Notifications;
using Backlog.Mobile.UI.Notifications;
using Backlog.Modules.Notifications.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// While a reply is on its way every answer control is busy, not disabled
/// (<c>.devbook/design/interaction-guidelines.md#accessibility</c>): it keeps its tab stop,
/// says <c>aria-disabled</c> instead of the native <c>disabled</c>, and ignores a press or
/// an edit. A control that is unavailable rather than waiting keeps the native attribute.
/// </summary>
public sealed class NotificationAnswerBusyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 41, 0, TimeSpan.Zero);

    private static AnswerSpec EveryBox() =>
        TestNotifications.Validation(new OpenAnswer("Write an answer…", 200)) with { Note = new OpenAnswer("Anything else", 4000) };

    private static (BunitContext Context, IRenderedComponent<NotificationAnswerControls> Controls, List<NotificationAnswerPick> Replies) Render(
        AnswerSpec spec, bool busy)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<ISpeechTranscriber>(new ScriptedSpeechTranscriber { Supported = true });

        var question = TestNotifications.Question("Approve the change?", Now.AddMinutes(-5), spec);
        var notification = new PhoneNotification(question, "WORKSTATION-01", OwnerPresence.Online, Now, NotificationStanding.Answerable, null, null);
        var replies = new List<NotificationAnswerPick>();

        var controls = context.Render<NotificationAnswerControls>(parameters => parameters
            .Add(p => p.Notification, notification)
            .Add(p => p.Busy, busy)
            .Add(p => p.OnReply, (NotificationAnswerPick pick) => replies.Add(pick)));

        return (context, controls, replies);
    }

    private static void AssertBusy(AngleSharp.Dom.IElement element)
    {
        Assert.Equal("true", element.GetAttribute("aria-disabled"));
        Assert.False(element.HasAttribute("disabled"), $"{element.OuterHtml} is natively disabled while busy.");
    }

    [Fact]
    public void Busy_choices_stay_focusable_and_ignore_the_press()
    {
        var (context, controls, replies) = Render(EveryBox(), busy: true);
        using var disposing = context;

        foreach (var id in new[] { "approve", "revise", "reject" })
            AssertBusy(controls.Find($"[data-testid='notification-choice-{id}']"));

        controls.Find("[data-testid='notification-choice-approve']").Click();
        controls.Find("[data-testid='notification-choice-revise']").Click();

        Assert.Empty(replies);
        Assert.Empty(controls.FindAll("[data-testid='notification-choice-text']"));
    }

    [Fact]
    public void Busy_text_boxes_are_read_only_rather_than_disabled()
    {
        var (context, controls, _) = Render(EveryBox(), busy: true);
        using var disposing = context;

        foreach (var box in new[] { "notification-text", "notification-note-text" })
        {
            var textarea = controls.Find($"[data-testid='{box}'] textarea");
            AssertBusy(textarea);
            Assert.True(textarea.HasAttribute("readonly"));
        }
    }

    [Fact]
    public void A_busy_choice_text_box_keeps_its_cancel_and_ignores_it()
    {
        var (context, controls, _) = Render(EveryBox(), busy: false);
        using var disposing = context;
        controls.Find("[data-testid='notification-choice-revise']").Click();
        controls.Find("[data-testid='notification-choice-text'] textarea").Input("Rename it");

        controls.Render(parameters => parameters.Add(p => p.Busy, true));

        var cancel = controls.Find("[data-testid='notification-choice-cancel']");
        AssertBusy(cancel);
        var textarea = controls.Find("[data-testid='notification-choice-text'] textarea");
        AssertBusy(textarea);
        Assert.True(textarea.HasAttribute("readonly"));

        cancel.Click();

        Assert.Equal("Rename it", controls.Find("[data-testid='notification-choice-text'] textarea").GetAttribute("value"));
    }

    [Fact]
    public void Busy_checkboxes_stay_focusable_and_ignore_a_change()
    {
        var (context, controls, replies) = Render(TestNotifications.Journeys, busy: true);
        using var disposing = context;

        var box = controls.Find("[data-testid='notification-check-pair'] input");
        AssertBusy(box);

        box.Change(true);
        controls.Render(parameters => parameters.Add(p => p.Busy, false));

        Assert.False(controls.Find("[data-testid='notification-check-pair'] input").HasAttribute("checked"));
        Assert.True(controls.Find("[data-testid='notification-submit']").HasAttribute("disabled"));
        Assert.Empty(replies);
    }

    [Fact]
    public void An_unavailable_control_keeps_the_native_disabled_while_busy()
    {
        // Answer with nothing picked, and Send with an empty box, are unavailable rather than waiting.
        var (context, controls, _) = Render(TestNotifications.Journeys, busy: true);
        using var disposing = context;
        var (openContext, open, _) = Render(TestNotifications.ReleaseNote, busy: true);
        using var disposingOpen = openContext;

        Assert.True(controls.Find("[data-testid='notification-submit']").HasAttribute("disabled"));
        Assert.True(open.Find("[data-testid='notification-send']").HasAttribute("disabled"));
    }

    [Fact]
    public void The_pressed_choice_shows_the_spinner_and_the_rest_wait()
    {
        var (context, controls, _) = Render(EveryBox(), busy: false);
        using var disposing = context;
        var hold = new TaskCompletionSource();

        controls.Render(parameters => parameters.Add(p => p.OnReply, async (NotificationAnswerPick _) =>
        {
            controls.Render(busy => busy.Add(p => p.Busy, true));
            await hold.Task;
        }));

        controls.Find("[data-testid='notification-choice-approve']").Click();

        controls.WaitForAssertion(() =>
        {
            Assert.Equal("true", controls.Find("[data-testid='notification-choice-approve']").GetAttribute("aria-busy"));
            Assert.False(controls.Find("[data-testid='notification-choice-reject']").HasAttribute("aria-busy"));
            AssertBusy(controls.Find("[data-testid='notification-choice-reject']"));
        });

        hold.SetResult();
        controls.Render(parameters => parameters.Add(p => p.Busy, false));

        controls.WaitForAssertion(() =>
        {
            foreach (var id in new[] { "approve", "reject" })
            {
                var choice = controls.Find($"[data-testid='notification-choice-{id}']");
                Assert.False(choice.HasAttribute("aria-busy"));
                Assert.False(choice.HasAttribute("aria-disabled"));
            }
        });
    }
}
