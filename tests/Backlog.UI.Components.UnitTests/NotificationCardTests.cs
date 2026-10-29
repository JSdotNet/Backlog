using Backlog.UI.Components.Notifications;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The notification centre's question card: its Markdown body with Mermaid through DiagramView,
/// the answer control each question shape gets, Acknowledge on an alert, and how a settled card
/// says what happened.
/// </summary>
public sealed class NotificationCardTests
{
    private static readonly NotificationCardAnswerSpec YesNo = new(
    [
        new NotificationCardChoice("yes", "Yes, run it", NotificationCardChoiceStyle.Primary),
        new NotificationCardChoice("no", "Not now")
    ]);

    private static NotificationCardModel Question(NotificationCardAnswerSpec? spec = null) => new(
        "n1",
        NotificationCardKind.Question,
        NotificationCardSeverity.Warning,
        "Run the migration against staging?",
        "Session sync-version-check wants to apply 0042_add_device_kind.",
        "3 tables changed.\n\n```mermaid\nflowchart LR\n  A --> B\n```",
        "MCP",
        "2 min ago",
        spec ?? YesNo,
        NotificationCardState.Open);

    private static NotificationCardModel Alert() => new(
        "n2", NotificationCardKind.Alert, NotificationCardSeverity.Info, "Remark left on Storage and sync",
        "Does the reply container need its own TTL?", string.Empty, "Devbook", "1 h ago", null, NotificationCardState.Open);

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    [Fact]
    public void The_body_is_markdown_and_a_mermaid_fence_draws_through_DiagramView_without_Archify()
    {
        using var context = Context();

        var card = context.Render<NotificationCard>(p => p.Add(c => c.Model, Question()));

        var body = card.Find("[data-testid='notification-card-body']");
        Assert.Contains("3 tables changed.", body.TextContent);
        Assert.NotNull(body.QuerySelector("[data-testid='diagram-view']"));
        Assert.Null(body.QuerySelector("[data-testid='diagram-view-artifact']"));
        Assert.Null(body.QuerySelector("[data-testid='diagram-view-renderers']"));
    }

    [Fact]
    public void A_single_choice_question_draws_one_button_per_set_answer_and_answers_with_the_one_pressed()
    {
        using var context = Context();
        NotificationCardReply? reply = null;

        var card = context.Render<NotificationCard>(p => p
            .Add(c => c.Model, Question())
            .Add(c => c.OnAnswer, r => reply = r));

        Assert.Equal(2, card.FindAll("[data-testid^='notification-card-choice-']").Count);
        Assert.Contains("btn--primary", card.Find("[data-testid='notification-card-choice-yes']").ClassName);

        card.Find("[data-testid='notification-card-choice-no']").Click();

        Assert.Equal(["no"], reply!.ChoiceIds);
        Assert.Null(reply.Text);
    }

    [Fact]
    public void A_destructive_choice_draws_as_a_danger_button()
    {
        using var context = Context();
        var spec = new NotificationCardAnswerSpec([new("keep", "Keep"), new("drop", "Drop it", NotificationCardChoiceStyle.Destructive)]);

        var card = context.Render<NotificationCard>(p => p.Add(c => c.Model, Question(spec)));

        Assert.Contains("btn--danger", card.Find("[data-testid='notification-card-choice-drop']").ClassName);
    }

    [Fact]
    public void A_multiple_choice_question_draws_checkboxes_and_answers_with_every_pick_in_the_questions_order()
    {
        using var context = Context();
        NotificationCardReply? reply = null;
        var spec = new NotificationCardAnswerSpec(
            [new("pair", "Pair a phone"), new("capture", "Capture from the phone"), new("pace", "Roadmap pace")],
            AllowMultiple: true);

        var card = context.Render<NotificationCard>(p => p
            .Add(c => c.Model, Question(spec))
            .Add(c => c.OnAnswer, r => reply = r));

        Assert.Empty(card.FindAll("[data-testid^='notification-card-choice-']"));
        card.Find("[data-testid='notification-card-check-pace'] input").Change(true);
        card.Find("[data-testid='notification-card-check-pair'] input").Change(true);
        card.Find("[data-testid='notification-card-submit']").Click();

        Assert.Equal(["pair", "pace"], reply!.ChoiceIds);
    }

    [Fact]
    public void A_multiple_choice_answer_waits_until_the_picks_fit_the_minimum()
    {
        using var context = Context();
        var spec = new NotificationCardAnswerSpec([new("a", "A"), new("b", "B"), new("c", "C")], AllowMultiple: true, MinChoices: 2);

        var card = context.Render<NotificationCard>(p => p.Add(c => c.Model, Question(spec)));
        card.Find("[data-testid='notification-card-check-a'] input").Change(true);

        Assert.True(card.Find("[data-testid='notification-card-submit']").HasAttribute("disabled"));
        Assert.Contains("Pick 2 to 3", card.Markup);
    }

    [Fact]
    public void An_open_question_draws_a_plain_text_box_and_sends_what_was_typed()
    {
        using var context = Context();
        NotificationCardReply? reply = null;
        var spec = new NotificationCardAnswerSpec([], Open: new NotificationCardOpenAnswer("Write an answer…", 200));

        var card = context.Render<NotificationCard>(p => p
            .Add(c => c.Model, Question(spec))
            .Add(c => c.OnAnswer, r => reply = r));

        var box = card.Find("[data-testid='notification-card-open'] textarea");
        Assert.Equal("200", box.GetAttribute("maxlength"));
        box.Input("  Ship it on Friday  ");
        card.Find("[data-testid='notification-card-send']").Click();

        Assert.Empty(reply!.ChoiceIds);
        Assert.Equal("Ship it on Friday", reply.Text);
    }

    [Fact]
    public void Sending_an_empty_box_asks_for_an_answer_instead_of_answering_nothing()
    {
        using var context = Context();
        var answered = false;
        var spec = new NotificationCardAnswerSpec([], Open: new NotificationCardOpenAnswer());

        var card = context.Render<NotificationCard>(p => p
            .Add(c => c.Model, Question(spec))
            .Add(c => c.OnAnswer, _ => answered = true));

        card.Find("[data-testid='notification-card-send']").Click();

        Assert.False(answered);
        Assert.Contains("Write an answer first.", card.Find("[data-testid='notification-card-error']").TextContent);
    }

    [Fact]
    public void Choices_with_an_open_answer_draw_both()
    {
        using var context = Context();
        var spec = YesNo with { Open = new NotificationCardOpenAnswer() };

        var card = context.Render<NotificationCard>(p => p.Add(c => c.Model, Question(spec)));

        Assert.Equal(2, card.FindAll("[data-testid^='notification-card-choice-']").Count);
        Assert.NotNull(card.Find("[data-testid='notification-card-open'] textarea"));
    }

    private static readonly NotificationCardAnswerSpec Validation = new(
    [
        new NotificationCardChoice("approve", "Approve", NotificationCardChoiceStyle.Primary),
        new NotificationCardChoice("revise", "Revise", TakesText: true, Placeholder: "What should change?"),
        new NotificationCardChoice("reject", "Reject", NotificationCardChoiceStyle.Destructive)
    ]);

    [Fact]
    public void Picking_a_choice_that_takes_text_asks_for_its_words_and_sends_nothing_yet()
    {
        using var context = Context();
        var answered = false;

        var card = context.Render<NotificationCard>(p => p
            .Add(c => c.Model, Question(Validation))
            .Add(c => c.OnAnswer, _ => answered = true));

        Assert.Empty(card.FindAll("[data-testid='notification-card-choice-text']"));

        card.Find("[data-testid='notification-card-choice-revise']").Click();

        Assert.False(answered);
        var box = card.Find("[data-testid='notification-card-choice-text'] textarea");
        Assert.Equal("What should change?", box.GetAttribute("placeholder"));
        Assert.Equal("2000", box.GetAttribute("maxlength"));
        Assert.True(card.Find("[data-testid='notification-card-choice-send']").HasAttribute("disabled"));
    }

    [Fact]
    public void A_choice_that_takes_text_answers_with_the_choice_and_the_words()
    {
        using var context = Context();
        NotificationCardReply? reply = null;

        var card = context.Render<NotificationCard>(p => p
            .Add(c => c.Model, Question(Validation))
            .Add(c => c.OnAnswer, r => reply = r));

        card.Find("[data-testid='notification-card-choice-revise']").Click();
        card.Find("[data-testid='notification-card-choice-text'] textarea").Input("   ");
        Assert.True(card.Find("[data-testid='notification-card-choice-send']").HasAttribute("disabled"));

        card.Find("[data-testid='notification-card-choice-text'] textarea").Input("  The toast overlaps the header  ");
        card.Find("[data-testid='notification-card-choice-send']").Click();

        Assert.Equal(["revise"], reply!.ChoiceIds);
        Assert.Equal("The toast overlaps the header", reply.Text);
    }

    [Fact]
    public void A_choices_text_box_keeps_to_the_open_answers_limit()
    {
        using var context = Context();
        var spec = Validation with { Open = new NotificationCardOpenAnswer(MaxLength: 300) };

        var card = context.Render<NotificationCard>(p => p.Add(c => c.Model, Question(spec)));
        card.Find("[data-testid='notification-card-choice-revise']").Click();

        Assert.Equal("300", card.Find("[data-testid='notification-card-choice-text'] textarea").GetAttribute("maxlength"));
    }

    [Fact]
    public void Cancelling_a_choices_words_puts_the_box_away_and_the_plain_choices_still_answer_at_once()
    {
        using var context = Context();
        NotificationCardReply? reply = null;

        var card = context.Render<NotificationCard>(p => p
            .Add(c => c.Model, Question(Validation))
            .Add(c => c.OnAnswer, r => reply = r));

        card.Find("[data-testid='notification-card-choice-revise']").Click();
        card.Find("[data-testid='notification-card-choice-cancel']").Click();

        Assert.Empty(card.FindAll("[data-testid='notification-card-choice-text']"));
        Assert.Null(reply);

        card.Find("[data-testid='notification-card-choice-approve']").Click();

        Assert.Equal(["approve"], reply!.ChoiceIds);
        Assert.Null(reply.Text);
    }

    [Fact]
    public void An_alert_offers_acknowledge()
    {
        using var context = Context();
        var acknowledged = false;

        var card = context.Render<NotificationCard>(p => p
            .Add(c => c.Model, Alert())
            .Add(c => c.OnAcknowledge, () => acknowledged = true));

        card.Find("[data-testid='notification-card-acknowledge']").Click();

        Assert.True(acknowledged);
        Assert.Equal("Alert", card.Find("[data-testid='notification-card-kind']").TextContent);
        Assert.Empty(card.FindAll("[data-testid='notification-card-severity']"));
    }

    [Fact]
    public void A_refused_answer_shows_its_reason_beside_the_controls_that_can_answer_again()
    {
        using var context = Context();

        var card = context.Render<NotificationCard>(p => p
            .Add(c => c.Model, Question())
            .Add(c => c.ErrorMessage, "The question is already answered."));

        Assert.Contains("already answered", card.Find("[data-testid='notification-card-error']").TextContent);
        Assert.NotEmpty(card.FindAll("[data-testid^='notification-card-choice-']"));
    }

    [Fact]
    public void An_answered_question_shows_the_winning_answer_and_the_device_it_came_from_and_no_controls()
    {
        using var context = Context();
        var answered = Question() with
        {
            State = NotificationCardState.Answered,
            Outcome = new NotificationCardOutcome(Answer: "Yes, run it", Device: "phone", When: "1 min ago")
        };

        var card = context.Render<NotificationCard>(p => p.Add(c => c.Model, answered));

        Assert.Equal("Answered from phone · Yes, run it · 1 min ago", card.Find("[data-testid='notification-card-outcome']").TextContent);
        Assert.Empty(card.FindAll("[data-testid='notification-card-answer']"));
        Assert.Equal("Answered", card.Find("[data-testid='notification-card-state']").TextContent);
    }

    [Fact]
    public void A_closed_question_shows_its_reason()
    {
        using var context = Context();
        var closed = Question() with
        {
            State = NotificationCardState.Closed,
            Outcome = new NotificationCardOutcome(Reason: "Session archived")
        };

        var card = context.Render<NotificationCard>(p => p.Add(c => c.Model, closed));

        Assert.Equal("Closed · Session archived", card.Find("[data-testid='notification-card-outcome']").TextContent);
        Assert.Equal("closed", card.Find("article").GetAttribute("data-state"));
    }
}
