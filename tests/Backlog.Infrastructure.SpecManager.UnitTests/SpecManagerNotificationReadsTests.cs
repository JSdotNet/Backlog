using System.Net;

using Backlog.Infrastructure.SpecManager.Questions;
using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// An alert of a spec-manager notification acknowledged in Backlog — on the desktop, or on
/// the phone through the sync exchange — marked read in spec-manager as the owner stores the
/// acknowledgement, without holding up whoever stored it.
/// </summary>
public sealed class SpecManagerNotificationReadsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_acknowledged_alert_is_marked_read_in_spec_manager_once()
    {
        using var scenario = new NotificationScenario();
        var centre = new FakeNotificationCentre();
        using var reads = Start(scenario, centre);
        scenario.ReadAnswers(NotificationScenario.Mention, HttpStatusCode.NoContent);

        centre.Raise(View(scenario, NotificationScenario.Mention, NotificationState.Acknowledged));
        await reads.Sends.WaitAsync(Token);

        Assert.Single(scenario.ReadsOf(NotificationScenario.Mention));
    }

    [Theory]
    [InlineData(NotificationState.Open)]
    [InlineData(NotificationState.Closed)]
    public async Task An_alert_published_or_closed_sends_nothing(NotificationState state)
    {
        using var scenario = new NotificationScenario();
        var centre = new FakeNotificationCentre();
        using var reads = Start(scenario, centre);

        centre.Raise(View(scenario, NotificationScenario.Mention, state));
        await reads.Sends.WaitAsync(Token);

        Assert.Empty(scenario.ReadsOf(NotificationScenario.Mention));
    }

    [Fact]
    public async Task Another_source_a_question_or_another_installation_sends_nothing()
    {
        using var scenario = new NotificationScenario();
        var centre = new FakeNotificationCentre();
        using var reads = Start(scenario, centre);
        var mention = View(scenario, NotificationScenario.Mention, NotificationState.Acknowledged);

        centre.Raise(mention with { Source = new NotificationSource("sessions", mention.Source.Key) });
        centre.Raise(mention with { Kind = NotificationKind.Question });
        centre.Raise(mention with { Id = SpecManagerQuestionIds.ForNotification("https://other.test", NotificationScenario.Mention) });
        await reads.Sends.WaitAsync(Token);

        Assert.Empty(scenario.ReadsOf(NotificationScenario.Mention));
    }

    /// <summary>Signed out, nothing is sent: the next pull after the sign-in finds the alert
    /// acknowledged and the notification unread, and sends it then.</summary>
    [Fact]
    public async Task Signed_out_it_sends_nothing()
    {
        using var scenario = new NotificationScenario();
        var centre = new FakeNotificationCentre();
        using var reads = Start(scenario, centre);
        await scenario.Connector.SignIn.SignOutAsync(Token);

        centre.Raise(View(scenario, NotificationScenario.Mention, NotificationState.Acknowledged));
        await reads.Sends.WaitAsync(Token);

        Assert.Empty(scenario.ReadsOf(NotificationScenario.Mention));
    }

    [Fact]
    public async Task Whoever_stored_the_acknowledgement_does_not_wait_for_spec_manager()
    {
        using var scenario = new NotificationScenario();
        var centre = new FakeNotificationCentre();
        using var reads = Start(scenario, centre);
        using var answer = new ManualResetEventSlim();
        scenario.Connector.Server.Route(HttpMethod.Post, NotificationScenario.ReadPath(NotificationScenario.Mention), (_, _) =>
        {
            answer.Wait(TimeSpan.FromSeconds(10));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        centre.Raise(View(scenario, NotificationScenario.Mention, NotificationState.Acknowledged));

        Assert.False(reads.Sends.IsCompleted);
        answer.Set();
        await reads.Sends.WaitAsync(Token);
        Assert.Single(scenario.ReadsOf(NotificationScenario.Mention));
    }

    [Fact]
    public async Task A_read_that_fails_is_left_for_the_next_pull_without_throwing()
    {
        using var scenario = new NotificationScenario();
        var centre = new FakeNotificationCentre();
        using var reads = Start(scenario, centre);
        scenario.ReadAnswers(NotificationScenario.Mention, HttpStatusCode.InternalServerError);

        centre.Raise(View(scenario, NotificationScenario.Mention, NotificationState.Acknowledged));
        await reads.Sends.WaitAsync(Token);

        Assert.Single(scenario.ReadsOf(NotificationScenario.Mention));
    }

    [Fact]
    public async Task Once_disposed_it_listens_no_more()
    {
        using var scenario = new NotificationScenario();
        var centre = new FakeNotificationCentre();
        var reads = Start(scenario, centre);

        reads.Dispose();
        centre.Raise(View(scenario, NotificationScenario.Mention, NotificationState.Acknowledged));
        await reads.Sends.WaitAsync(Token);

        Assert.Empty(scenario.ReadsOf(NotificationScenario.Mention));
    }

    private static SpecManagerNotificationReads Start(NotificationScenario scenario, FakeNotificationCentre centre)
    {
        var reads = new SpecManagerNotificationReads(scenario.Connector.Client, scenario.Connector.SignIn, centre);
        reads.Start();
        return reads;
    }

    private static NotificationView View(NotificationScenario scenario, Guid notificationId, NotificationState state) =>
        ViewOf(scenario.Root, notificationId, state);

    /// <summary>The alert of the fixture's <paramref name="notificationId"/> on the
    /// installation at <paramref name="root"/>, as the centre reports it in
    /// <paramref name="state"/>.</summary>
    internal static NotificationView ViewOf(string root, Guid notificationId, NotificationState state)
    {
        var draft = SpecManagerNotificationMapper.ToDraft(NotificationScenario.Fixture(notificationId), root, false, null);
        return new NotificationView(
            draft.Id!.Value,
            Guid.Empty,
            draft.Source,
            draft.Subject,
            draft.Kind,
            draft.Severity,
            draft.Title,
            draft.Summary,
            draft.BodyMarkdown,
            draft.Answer,
            draft.DeepLink,
            draft.IsPrivate,
            DateTimeOffset.UnixEpoch,
            state,
            null,
            null,
            null,
            draft.Origin);
    }
}
