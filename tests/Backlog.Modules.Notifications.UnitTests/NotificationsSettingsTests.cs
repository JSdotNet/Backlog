using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.Services;
using Backlog.Modules.Notifications.UI;
using Backlog.SharedKernel;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using static Backlog.Modules.Notifications.UnitTests.Drafts;

namespace Backlog.Modules.Notifications.UnitTests;

/// <summary>
/// Settings › Notifications: the test card over the real owner ports and an in-memory store. Send
/// test alert and Send test question publish through <see cref="INotifications"/>; the status under
/// the buttons follows the last test through <see cref="INotificationCentre"/> — published, pushed to
/// the phone, answered with the answer and its device, closed — and Clear test closes it.
/// </summary>
public sealed class NotificationsSettingsTests : IDisposable
{
    private const string SendAlert = "[data-testid='notifications-test-send-alert']";
    private const string SendQuestion = "[data-testid='notifications-test-send-question']";
    private const string Clear = "[data-testid='notifications-test-clear']";

    private readonly InMemoryNotificationRepository _inner = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly OwnerNotifications _owner;
    private readonly NotificationCentre _centre;
    private readonly BunitContext _context = new();

    public NotificationsSettingsTests()
    {
        var store = new ObservedNotificationRepository(_inner);
        _owner = new OwnerNotifications(
            store, new FixedDevice(Owner), new AnswerDelivery([new TestNotificationAnswers()], store, _time), _time);
        _centre = new NotificationCentre(store, _owner, new FixedDevice(Owner), _time);

        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton<INotifications>(_owner);
        _context.Services.AddSingleton<INotificationCentre>(_centre);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _context.Dispose();
        _centre.Dispose();
        _owner.Dispose();
    }

    private async Task<NotificationView> OnlyTestAsync() =>
        Assert.Single(await _centre.ListAsync(Ct), view => TestNotifications.IsTest(view.Source));

    /// <summary>One status row as "Subject: detail".</summary>
    private static string Step(IRenderedComponent<NotificationsSettings> page, string key)
    {
        var row = page.Find($"[data-testid='notifications-test-step-{key}']");
        return $"{row.QuerySelector(".setting__check-subject")!.TextContent.Trim()}: {row.QuerySelector(".setting__check-detail")!.TextContent.Trim()}";
    }

    // --- The page --------------------------------------------------------------------------

    [Fact]
    public void The_page_is_a_product_area_after_devices_behind_the_notifications_switch()
    {
        var services = new ServiceCollection().AddNotificationsSettings();
        var section = Assert.Single(services.BuildServiceProvider().GetServices<SettingsSection>());

        Assert.Equal("notifications", section.Id);
        Assert.Equal("Notifications", section.Title);
        Assert.Equal(SettingsGroup.ProductAreas, section.Group);
        Assert.Equal(NotificationsFeatures.Notifications, section.FeatureKey);
        Assert.Equal(typeof(NotificationsSettings), section.Component);
        Assert.Equal(NotificationsSettingsRegistration.Order, section.Order);
        Assert.Equal(NotificationsSettings.CardTitles, section.CardTitles);
    }

    [Fact]
    public void The_declared_card_titles_are_the_cards_the_page_draws()
    {
        var page = _context.Render<NotificationsSettings>();

        Assert.Equal(
            NotificationsSettings.CardTitles,
            page.FindAll(".settings-card__title").Select(title => title.TextContent.Trim()));
    }

    [Fact]
    public void Before_any_test_the_page_says_none_was_sent_and_offers_no_clear()
    {
        var page = _context.Render<NotificationsSettings>();

        Assert.NotNull(page.Find("[data-testid='notifications-test-none']"));
        Assert.Empty(page.FindAll(Clear));
        Assert.Equal("No test notification sent from this desktop in the last seven days.", NotificationsSettings.Headline(null, Owner));
    }

    // --- Send test alert -------------------------------------------------------------------

    [Fact]
    public async Task Send_test_alert_publishes_an_info_alert_with_markdown_and_a_mermaid_diagram()
    {
        var page = _context.Render<NotificationsSettings>();

        page.Find(SendAlert).Click();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='notifications-test-status']")));
        var alert = await OnlyTestAsync();
        Assert.Equal(NotificationKind.Alert, alert.Kind);
        Assert.Equal(NotificationSeverity.Info, alert.Severity);
        Assert.Null(alert.AnswerSpec);
        Assert.Contains("**This is a test alert**", alert.BodyMarkdown, StringComparison.Ordinal);
        Assert.Contains("```mermaid", alert.BodyMarkdown, StringComparison.Ordinal);
        Assert.Equal(TestNotifications.SourceContext, alert.Subject!.Context);
        Assert.Equal(TestNotifications.SubjectType, alert.Subject.Type);
    }

    [Fact]
    public void A_fresh_alert_reads_published_not_pushed_not_acknowledged_and_open()
    {
        var page = _context.Render<NotificationsSettings>();

        page.Find(SendAlert).Click();

        page.WaitForAssertion(() => Assert.StartsWith("Published: ", Step(page, "published"), StringComparison.Ordinal));
        // A literal, not the constant: the reason is what a person reads, and it has to stay
        // true now that the phone push is sent — it is the delivery receipt that is missing.
        Assert.Equal("Pushed to the phone: Not reported — the phone push carries no delivery receipt", Step(page, "pushed"));
        Assert.Equal("Acknowledged: Not yet", Step(page, "answered"));
        Assert.Equal("Closed: Open", Step(page, "closed"));
        Assert.Contains("setting__check-step--passed", page.Find("[data-testid='notifications-test-step-published']").ClassName, StringComparison.Ordinal);
        Assert.Contains("setting__check-step--skipped", page.Find("[data-testid='notifications-test-step-pushed']").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Acknowledging_the_test_alert_in_the_centre_moves_the_status_while_the_page_is_open()
    {
        var page = _context.Render<NotificationsSettings>();
        page.Find(SendAlert).Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find(Clear)));

        await _centre.AcknowledgeAsync((await OnlyTestAsync()).Id, Ct);

        page.WaitForAssertion(() => Assert.StartsWith("Acknowledged: At ", Step(page, "answered"), StringComparison.Ordinal));
        Assert.True(page.Find(Clear).HasAttribute("disabled"));
    }

    // --- Send test question ----------------------------------------------------------------

    [Fact]
    public async Task Send_test_question_asks_it_works_or_not_yet_with_an_open_answer_about_a_test_subject()
    {
        var page = _context.Render<NotificationsSettings>();

        page.Find(SendQuestion).Click();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='notifications-test-status']")));
        var question = await OnlyTestAsync();
        Assert.Equal(NotificationKind.Question, question.Kind);
        Assert.Equal(
            [(TestNotifications.ItWorksChoiceId, "It works"), (TestNotifications.NotYetChoiceId, "Not yet")],
            question.AnswerSpec!.Choices.Select(choice => (choice.Id, choice.Label)));
        Assert.NotNull(question.AnswerSpec.Open);
        Assert.False(question.AnswerSpec.AllowMultiple);

        var subject = question.Subject!;
        Assert.Equal(("notifications", "test"), (subject.Context, subject.Type));
        Assert.Matches("^[0-9a-f]{8}$", subject.Id);
        Assert.Equal(
            $"notifications / test / {subject.Id}",
            page.Find("[data-testid='notifications-test-which'] code").TextContent);
        Assert.Equal("Answered: Waiting for an answer", Step(page, "answered"));
    }

    [Fact]
    public async Task Each_test_question_is_about_a_subject_of_its_own()
    {
        var page = _context.Render<NotificationsSettings>();

        page.Find(SendQuestion).Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find(Clear)));
        page.Find(Clear).Click();
        page.WaitForAssertion(() => Assert.True(page.Find(Clear).HasAttribute("disabled")));
        page.Find(SendQuestion).Click();

        page.WaitForAssertion(() => Assert.Equal(
            2, _centre.ListAsync(Ct).Result.Count(view => TestNotifications.IsTest(view.Source))));
        var subjects = (await _centre.ListAsync(Ct)).Select(view => view.Subject!.Id).Distinct();
        Assert.Equal(2, subjects.Count());
    }

    [Fact]
    public async Task An_answer_from_the_phone_shows_the_answer_and_the_device_while_the_page_is_open()
    {
        var page = _context.Render<NotificationsSettings>();
        page.Find(SendQuestion).Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find(Clear)));
        var question = await OnlyTestAsync();

        var accepted = await _owner.AcceptReplyAsync(
            new NotificationReply(Guid.NewGuid(), question.Id, Owner, Phone, [TestNotifications.ItWorksChoiceId], null, Now),
            Ct);
        Assert.True(accepted.IsSuccess);

        page.WaitForAssertion(() => Assert.StartsWith(
            "Answered: It works — from another device",
            Step(page, "answered"),
            StringComparison.Ordinal));
        Assert.Equal("Closed: No — settled by the answer", Step(page, "closed"));
        Assert.True(page.Find(Clear).HasAttribute("disabled"));
        Assert.Equal(
            "The test question was answered from another device.",
            NotificationsSettings.Headline((await OnlyTestAsync()), Owner));
    }

    [Fact]
    public async Task An_open_answer_from_the_phone_is_shown_as_written()
    {
        var page = _context.Render<NotificationsSettings>();
        page.Find(SendQuestion).Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find(Clear)));
        var question = await OnlyTestAsync();

        var accepted = await _owner.AcceptReplyAsync(
            new NotificationReply(Guid.NewGuid(), question.Id, Owner, Phone, [], "Buzzed on the lock screen", Now),
            Ct);
        Assert.True(accepted.IsSuccess);

        page.WaitForAssertion(() => Assert.StartsWith(
            "Answered: “Buzzed on the lock screen” — from another device",
            Step(page, "answered"),
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_answer_given_on_this_desktop_says_so()
    {
        var page = _context.Render<NotificationsSettings>();
        page.Find(SendQuestion).Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find(Clear)));

        await _centre.AnswerAsync((await OnlyTestAsync()).Id, [TestNotifications.NotYetChoiceId], null, cancellationToken: Ct);

        page.WaitForAssertion(() => Assert.StartsWith(
            "Answered: Not yet — from this desktop", Step(page, "answered"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sending_a_new_test_withdraws_the_one_still_open()
    {
        var page = _context.Render<NotificationsSettings>();
        page.Find(SendQuestion).Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find(Clear)));
        var first = await OnlyTestAsync();

        page.Find(SendAlert).Click();

        page.WaitForAssertion(() => Assert.Equal("Acknowledged: Not yet", Step(page, "answered")));
        var tests = (await _centre.ListAsync(Ct)).Where(view => TestNotifications.IsTest(view.Source)).ToList();
        Assert.Equal(2, tests.Count);
        var withdrawn = Assert.Single(tests, view => view.Id == first.Id);
        Assert.Equal(NotificationState.Closed, withdrawn.State);
        Assert.Equal(TestNotifications.ReplacedReason, withdrawn.CloseReason);
        Assert.True(Assert.Single(tests, view => view.Id != first.Id).IsOpen);
    }

    // --- Clear test ------------------------------------------------------------------------

    [Fact]
    public async Task Clear_test_closes_the_open_test_and_the_status_says_it_closed()
    {
        var page = _context.Render<NotificationsSettings>();
        page.Find(SendQuestion).Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find(Clear)));

        page.Find(Clear).Click();

        page.WaitForAssertion(() => Assert.StartsWith($"Closed: {TestNotifications.ClearedReason}", Step(page, "closed"), StringComparison.Ordinal));
        var closed = await OnlyTestAsync();
        Assert.Equal(NotificationState.Closed, closed.State);
        Assert.Equal(TestNotifications.ClearedReason, closed.CloseReason);
        Assert.Equal("Answered: Not answered", Step(page, "answered"));
        Assert.True(page.Find(Clear).HasAttribute("disabled"));
    }

    // --- Coming back -----------------------------------------------------------------------

    [Fact]
    public async Task The_page_opens_on_the_last_test_the_centre_holds_and_ignores_other_sources()
    {
        await _owner.PublishAsync(TestNotifications.Alert(TestNotifications.NewSubject()), Ct);
        _time.Advance(TimeSpan.FromMinutes(1));
        await _owner.PublishAsync(TestNotifications.Question(TestNotifications.NewSubject()), Ct);
        _time.Advance(TimeSpan.FromMinutes(1));
        await _owner.PublishAsync(Question(), Ct);

        var page = _context.Render<NotificationsSettings>();

        page.WaitForAssertion(() => Assert.Equal("Answered: Waiting for an answer", Step(page, "answered")));
    }

    [Fact]
    public async Task A_publish_the_module_refuses_is_said_on_the_card()
    {
        _context.Services.AddSingleton<INotifications>(new RefusingNotifications());
        var page = _context.Render<NotificationsSettings>();

        page.Find(SendAlert).Click();

        page.WaitForAssertion(() => Assert.Contains(
            "switched off", page.Find("[data-testid='notifications-test-error']").TextContent, StringComparison.Ordinal));
        Assert.Empty(await _centre.ListAsync(Ct));
        Assert.Null(page.Find(SendAlert).GetAttribute("disabled"));
    }

    /// <summary>A port that refuses every publish the way a desktop with notifications off does.</summary>
    private sealed class RefusingNotifications : INotifications
    {
        public Task<Backlog.SharedKernel.Results.Result<Guid>> PublishAsync(NotificationDraft draft, CancellationToken cancellationToken = default) =>
            Task.FromResult(Backlog.SharedKernel.Results.Result.Failure<Guid>(NotificationsErrors.NotAvailable()));

        public Task<Backlog.SharedKernel.Results.Result> CloseAsync(Guid notificationId, string? reason = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Backlog.SharedKernel.Results.Result<int>> CloseForSubjectAsync(NotificationSubject subject, string reason, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Backlog.SharedKernel.Results.Result<NotificationAnswer?>> GetAnswerAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
