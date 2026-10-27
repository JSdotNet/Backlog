using System.Net;

using Backlog.Infrastructure.SpecManager.Questions;
using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// A pull of the person's other spec-manager notifications: which are published as alerts,
/// which close because they were read in spec-manager, and which acknowledged here are marked
/// read there.
/// </summary>
public sealed class SpecManagerNotificationSyncTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_unread_notifications_since_the_first_sign_in_are_published_and_the_question_and_older_ones_are_not()
    {
        using var scenario = new NotificationScenario();

        Assert.True(await scenario.Sync.PullAsync(Token));

        Assert.Equal(
            new[] { scenario.IdOf(NotificationScenario.Mention), scenario.IdOf(NotificationScenario.Reply) }.Order(),
            scenario.Ledger.Published.Select(entry => entry.Id).Order());
    }

    [Fact]
    public async Task It_asks_for_the_two_hundred_newest_unread()
    {
        using var scenario = new NotificationScenario();

        await scenario.Sync.PullAsync(Token);

        Assert.Equal("?unread=true&count=200", Assert.Single(scenario.Connector.Server.To(NotificationScenario.NotificationsPath)).Uri.Query);
    }

    [Fact]
    public async Task An_alert_is_placed_in_its_product_by_name_and_one_about_no_product_in_none()
    {
        using var scenario = new NotificationScenario();

        await scenario.Sync.PullAsync(Token);

        Assert.Equal("Billing", scenario.Ledger.Draft(scenario.IdOf(NotificationScenario.Mention)).Origin?.Place);
        Assert.Null(scenario.Ledger.Draft(scenario.IdOf(NotificationScenario.Reply)).Origin?.Place);
    }

    /// <summary>An alert acknowledged here, and still unread in spec-manager, is not published
    /// again: the owner keeps the one it holds.</summary>
    [Fact]
    public async Task A_second_pull_publishes_nothing_again()
    {
        using var scenario = new NotificationScenario();
        await scenario.Sync.PullAsync(Token);
        scenario.Ledger.Acknowledge(scenario.IdOf(NotificationScenario.Mention));

        await scenario.Sync.PullAsync(Token);

        Assert.Equal(2, scenario.Ledger.Published.Count);
        Assert.Equal(NotificationState.Acknowledged, scenario.Ledger.StateOf(scenario.IdOf(NotificationScenario.Mention)));
    }

    [Fact]
    public async Task The_products_are_read_once_and_again_only_when_a_product_appears_they_do_not_name()
    {
        using var scenario = new NotificationScenario();
        scenario.Connector.Server.Route(HttpMethod.Get, NotificationScenario.ProductsPath, (_, _) => StubSpecManager.Respond(
            HttpStatusCode.OK,
            """[{ "id": "10000000-0000-0000-0000-000000000001", "slug": "billing", "name": "Billing" }]"""));

        await scenario.Sync.PullAsync(Token);
        await scenario.Sync.PullAsync(Token);
        Assert.Single(scenario.Connector.Server.To(NotificationScenario.ProductsPath));

        var onDemo = NotificationScenario.Fixture(NotificationScenario.Reply) with { Id = Guid.NewGuid(), ProductId = NotificationScenario.Demo };
        scenario.Unread(list => [.. list, onDemo]);
        scenario.Connector.Server.Route(HttpMethod.Get, NotificationScenario.ProductsPath, (_, _) => StubSpecManager.Respond(
            HttpStatusCode.OK,
            Fixtures.Read("products.json")));
        await scenario.Sync.PullAsync(Token);

        Assert.Equal(2, scenario.Connector.Server.To(NotificationScenario.ProductsPath).Count);
        Assert.Equal("backlog-demo", scenario.Ledger.Draft(scenario.IdOf(onDemo.Id)).Origin?.Place);
    }

    /// <summary>A product the products do not name — archived, or MCP switched off — is asked
    /// after once per sign-in, not on every pull that brings a notification about it.</summary>
    [Fact]
    public async Task A_product_the_products_do_not_name_is_looked_up_once()
    {
        using var scenario = new NotificationScenario();
        var gone = Guid.NewGuid();
        var first = NotificationScenario.Fixture(NotificationScenario.Reply) with { Id = Guid.NewGuid(), ProductId = gone };
        var second = first with { Id = Guid.NewGuid() };

        scenario.Unread(_ => [first]);
        await scenario.Sync.PullAsync(Token);
        scenario.Unread(_ => [first, second]);
        await scenario.Sync.PullAsync(Token);

        Assert.Single(scenario.Connector.Server.To(NotificationScenario.ProductsPath));
        Assert.Null(scenario.Ledger.Draft(scenario.IdOf(second.Id)).Origin?.Place);
    }

    /// <summary>An alert already held is not published again, so its product is not looked
    /// up again either.</summary>
    [Fact]
    public async Task Held_alerts_look_up_no_product()
    {
        using var scenario = new NotificationScenario();
        await HoldAsync(scenario, NotificationScenario.Mention);
        scenario.Unread(list => list.Where(notification => notification.Id == NotificationScenario.Mention));

        await scenario.Sync.PullAsync(Token);

        Assert.Empty(scenario.Connector.Server.To(NotificationScenario.ProductsPath));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task An_answer_without_a_list_is_no_answer_and_closes_nothing(string body)
    {
        using var scenario = new NotificationScenario();
        var held = await HoldAsync(scenario, NotificationScenario.ReadElsewhere);
        scenario.Connector.Server.Route(HttpMethod.Get, NotificationScenario.NotificationsPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.OK, body));

        Assert.False(await scenario.Sync.PullAsync(Token));

        Assert.Equal(NotificationState.Open, scenario.Ledger.StateOf(held));
        Assert.Single(scenario.Ledger.Published);
    }

    [Fact]
    public async Task Products_that_cannot_be_read_leave_the_alerts_without_a_place()
    {
        using var scenario = new NotificationScenario();
        scenario.Connector.Server.Route(HttpMethod.Get, NotificationScenario.ProductsPath, (_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await scenario.Sync.PullAsync(Token);

        Assert.Null(scenario.Ledger.Draft(scenario.IdOf(NotificationScenario.Mention)).Origin?.Place);
    }

    [Fact]
    public async Task Nobody_signed_in_pulls_nothing()
    {
        using var scenario = new NotificationScenario();
        await scenario.Connector.SignIn.SignOutAsync(Token);

        Assert.False(await scenario.Sync.PullAsync(Token));

        Assert.Empty(scenario.Connector.Server.To(NotificationScenario.NotificationsPath));
        Assert.Empty(scenario.Ledger.Published);
    }

    [Fact]
    public async Task An_installation_that_cannot_be_reached_is_left_for_the_next_pull()
    {
        using var scenario = new NotificationScenario();
        scenario.Connector.Server.Route(HttpMethod.Get, NotificationScenario.NotificationsPath, (_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        Assert.False(await scenario.Sync.PullAsync(Token));

        Assert.Empty(scenario.Ledger.Published);
    }

    [Fact]
    public async Task An_alert_read_in_spec_manager_closes_when_the_list_is_complete()
    {
        using var scenario = new NotificationScenario();
        var read = await HoldAsync(scenario, NotificationScenario.ReadElsewhere);

        await scenario.Sync.PullAsync(Token);

        Assert.Equal(NotificationState.Closed, scenario.Ledger.StateOf(read));
        Assert.Equal("Read in spec-manager", scenario.Ledger.ReasonOf(read));
    }

    [Fact]
    public async Task A_list_cut_short_closes_nothing()
    {
        using var scenario = new NotificationScenario();
        var read = await HoldAsync(scenario, NotificationScenario.ReadElsewhere);
        scenario.Unread(list => list, unread: 250);

        await scenario.Sync.PullAsync(Token);

        Assert.Equal(NotificationState.Open, scenario.Ledger.StateOf(read));
    }

    [Fact]
    public async Task Questions_refusal_alerts_other_sources_and_other_installations_are_never_closed_as_read()
    {
        using var scenario = new NotificationScenario();
        var stranger = Guid.NewGuid();
        var question = await PublishAsync(scenario, Draft(scenario, NotificationScenario.ReadElsewhere) with
        {
            Kind = NotificationKind.Question,
            Subject = new NotificationSubject("spec-manager", "question", "q-1"),
            Answer = new AnswerSpec([new AnswerChoice("1", "Yes")]),
            Id = SpecManagerQuestionIds.For(scenario.Root, "notification/" + stranger),
        });
        var refusal = await PublishAsync(scenario, Draft(scenario, stranger) with
        {
            Id = SpecManagerQuestionIds.RefusalFor(scenario.Root, stranger.ToString()),
        });
        var otherSource = await PublishAsync(scenario, Draft(scenario, stranger) with
        {
            Source = new NotificationSource("sessions", $"notification/{stranger:D}"),
            Id = SpecManagerQuestionIds.ForNotification(scenario.Root, stranger),
        });
        var otherInstallation = await PublishAsync(scenario, Draft(scenario, Guid.NewGuid()) with
        {
            Id = SpecManagerQuestionIds.ForNotification("https://other.test", stranger),
        });

        await scenario.Sync.PullAsync(Token);

        Assert.All(
            new[] { question, refusal, otherSource, otherInstallation },
            id => Assert.Equal(NotificationState.Open, scenario.Ledger.StateOf(id)));
    }

    [Fact]
    public async Task An_alert_acknowledged_here_is_marked_read_in_spec_manager_on_the_next_pull()
    {
        using var scenario = new NotificationScenario();
        await scenario.Sync.PullAsync(Token);
        scenario.ReadAnswers(NotificationScenario.Mention, HttpStatusCode.NoContent);
        scenario.Ledger.Acknowledge(scenario.IdOf(NotificationScenario.Mention));

        await scenario.Sync.PullAsync(Token);

        var read = Assert.Single(scenario.ReadsOf(NotificationScenario.Mention));
        Assert.Equal("sma_current", read.Bearer);
        Assert.Empty(scenario.ReadsOf(NotificationScenario.Reply));
    }

    [Fact]
    public async Task A_read_spec_manager_no_longer_knows_counts_as_done()
    {
        using var scenario = new NotificationScenario();
        await scenario.Sync.PullAsync(Token);
        scenario.ReadAnswers(NotificationScenario.Mention, HttpStatusCode.NotFound);
        scenario.Ledger.Acknowledge(scenario.IdOf(NotificationScenario.Mention));

        Assert.True(await scenario.Sync.PullAsync(Token));

        Assert.Single(scenario.ReadsOf(NotificationScenario.Mention));
    }

    [Fact]
    public async Task A_read_that_fails_is_sent_again_on_the_next_pull_and_holds_up_nothing_else()
    {
        using var scenario = new NotificationScenario();
        await scenario.Sync.PullAsync(Token);
        scenario.ReadAnswers(NotificationScenario.Mention, HttpStatusCode.InternalServerError);
        scenario.Ledger.Acknowledge(scenario.IdOf(NotificationScenario.Mention));
        var read = await HoldAsync(scenario, NotificationScenario.ReadElsewhere);

        await scenario.Sync.PullAsync(Token);
        await scenario.Sync.PullAsync(Token);

        Assert.Equal(2, scenario.ReadsOf(NotificationScenario.Mention).Count);
        Assert.Equal(NotificationState.Closed, scenario.Ledger.StateOf(read));
    }

    /// <summary>Holds the alert of <paramref name="notificationId"/> open, as an earlier pull
    /// left it.</summary>
    private static Task<Guid> HoldAsync(NotificationScenario scenario, Guid notificationId) =>
        PublishAsync(scenario, Draft(scenario, notificationId));

    private static NotificationDraft Draft(NotificationScenario scenario, Guid notificationId) =>
        SpecManagerNotificationMapper.ToDraft(
            NotificationScenario.Fixture(NotificationScenario.Reply) with { Id = notificationId },
            scenario.Root,
            defaultInstallation: false,
            productName: null);

    private static async Task<Guid> PublishAsync(NotificationScenario scenario, NotificationDraft draft)
    {
        var published = await scenario.Ledger.PublishAsync(draft, Token);
        return published.Value;
    }
}
