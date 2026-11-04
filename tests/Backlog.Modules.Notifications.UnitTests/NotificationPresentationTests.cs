using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.UI;
using Backlog.SharedKernel;
using Backlog.UI.Components.Notifications;

using static Backlog.Modules.Notifications.UnitTests.Drafts;

namespace Backlog.Modules.Notifications.UnitTests;

/// <summary>
/// How the centre puts a notification's origin into words: the origin replaces the source label
/// where one is set, the card carries it whole, and the "From" filter picks by its system.
/// </summary>
public sealed class NotificationPresentationTests
{
    private static readonly NotificationOrigin SpecManager =
        new("spec-manager", "Billing · #7 Rounding in invoices", "Ada Lovelace", "Agent session", "https://spec-manager.example/q/7");

    private static NotificationView View(NotificationOrigin? origin = null, NotificationSource? source = null) => new(
        Guid.NewGuid(), Owner, source ?? Sessions, Session, NotificationKind.Question, NotificationSeverity.Info,
        "Which rounding rule applies to invoice totals?", "#7", string.Empty, YesNo(),
        null, false, Now, NotificationState.Open, null, null, null, origin);

    [Fact]
    public void The_source_label_is_the_origins_system_and_place_where_one_is_set()
    {
        Assert.Equal("spec-manager · Billing · #7 Rounding in invoices", NotificationPresentation.SourceLabel(View(SpecManager)));
    }

    [Fact]
    public void Without_an_origin_the_source_label_is_the_source_as_before()
    {
        Assert.Equal("Sessions", NotificationPresentation.SourceLabel(View()));
        Assert.Equal("MCP", NotificationPresentation.SourceLabel(View(source: new NotificationSource("mcp", "k"))));
    }

    [Fact]
    public void The_row_line_leads_with_the_origin()
    {
        Assert.Equal(
            "spec-manager · Billing · #7 Rounding in invoices · 4 min ago",
            NotificationPresentation.RowLine(View(SpecManager), Now.AddMinutes(4), Owner));
    }

    [Fact]
    public void The_card_carries_the_origin_with_the_asker_kind_and_the_agents_session_alone_as_how()
    {
        var card = NotificationPresentation.ToCard(View(SpecManager with { Via = "Agent session · Fix rounding" }), Now, Owner);

        Assert.Equal(
            new NotificationCardOrigin("spec-manager", "Billing · #7 Rounding in invoices", "Ada Lovelace", "Fix rounding", "https://spec-manager.example/q/7", NotificationCardAsker.Agent),
            card.Origin);
    }

    [Fact]
    public void A_persons_card_keeps_its_how_whole()
    {
        var card = NotificationPresentation.ToCard(View(SpecManager with { Via = "typed in spec-manager" }), Now, Owner);

        Assert.Equal(NotificationCardAsker.Person, card.Origin!.Asker);
        Assert.Equal("typed in spec-manager", card.Origin.Via);
    }

    [Fact]
    public void A_card_without_an_origin_has_none_and_names_its_source()
    {
        var card = NotificationPresentation.ToCard(View(), Now, Owner);

        Assert.Null(card.Origin);
        Assert.Equal("Sessions", card.Source);
    }

    [Fact]
    public void A_card_without_an_origin_still_carries_the_asker_kind_its_row_marks()
    {
        var sessions = NotificationPresentation.ToCard(View(), Now, Owner);
        var test = NotificationPresentation.ToCard(View(source: new NotificationSource("notifications", "test")), Now, Owner);

        Assert.Equal(NotificationCardAsker.Agent, sessions.Asker);
        Assert.Equal(NotificationCardAsker.System, test.Asker);
    }

    [Fact]
    public void A_card_with_an_origin_carries_the_same_asker_kind_on_itself()
    {
        var card = NotificationPresentation.ToCard(View(SpecManager with { Via = "typed in spec-manager" }), Now, Owner);

        Assert.Equal(NotificationCardAsker.Person, card.Asker);
    }

    [Fact]
    public void From_is_the_origins_system_or_the_source_without_one()
    {
        Assert.Equal("spec-manager", NotificationPresentation.From(View(SpecManager)));
        Assert.Equal("Sessions", NotificationPresentation.From(View()));
    }

    [Fact]
    public void The_from_options_name_each_system_once_in_alphabetical_order()
    {
        var views = new[] { View(SpecManager), View(new NotificationOrigin("Backlog", "Pairing")), View(SpecManager), View() };

        Assert.Equal(["Backlog", "Sessions", "spec-manager"], NotificationPresentation.Froms(views));
    }

    [Fact]
    public void From_only_keeps_the_notifications_from_one_system_and_none_narrows_nothing()
    {
        var fromSpecManager = View(SpecManager);
        var views = new[] { fromSpecManager, View(new NotificationOrigin("Backlog")), View() };

        Assert.Equal([fromSpecManager], NotificationPresentation.FromOnly(views, "spec-manager"));
        Assert.Equal(3, NotificationPresentation.FromOnly(views, null).Count);
        Assert.Equal(3, NotificationPresentation.FromOnly(views, string.Empty).Count);
    }

    // --- Asker kind -----------------------------------------------------------------------

    [Theory]
    [InlineData("Agent session")]
    [InlineData("Agent session · Fix rounding")]
    [InlineData("MCP ask")]
    [InlineData("MCP notify")]
    public void An_agent_asked_what_came_through_an_agent_session_or_the_mcp_tools(string via)
    {
        Assert.Equal(NotificationAskerKind.AiAgent, NotificationPresentation.AskerKind(View(new NotificationOrigin("Claude Code", Via: via))));
    }

    [Fact]
    public void A_sessions_notification_with_no_origin_is_an_agents()
    {
        Assert.Equal(NotificationAskerKind.AiAgent, NotificationPresentation.AskerKind(View()));
    }

    [Fact]
    public void A_question_typed_in_spec_manager_is_a_persons()
    {
        Assert.Equal(NotificationAskerKind.Person, NotificationPresentation.AskerKind(View(SpecManager with { Via = "typed in spec-manager" })));
    }

    [Fact]
    public void A_remark_its_author_left_is_a_persons()
    {
        var remark = View(new NotificationOrigin("Backlog", "arc42/05.md", "Job"), new NotificationSource("devbook", "r-1"));

        Assert.Equal(NotificationAskerKind.Person, NotificationPresentation.AskerKind(remark));
    }

    [Fact]
    public void Pairing_and_the_settings_test_are_backlogs_own()
    {
        Assert.Equal(NotificationAskerKind.Backlog, NotificationPresentation.AskerKind(View(new NotificationOrigin("Backlog", "Pairing"), new NotificationSource("sync", "p"))));
        Assert.Equal(NotificationAskerKind.Backlog, NotificationPresentation.AskerKind(View(source: new NotificationSource("notifications", "test"))));
    }

    // --- Filters and scope ----------------------------------------------------------------

    [Fact]
    public void Asked_by_keeps_one_kind_of_asker_and_none_narrows_nothing()
    {
        var agent = View();
        var person = View(SpecManager with { Via = "typed in spec-manager" });

        Assert.Equal([person], NotificationPresentation.AskedByOnly([agent, person], NotificationAskerKind.Person));
        Assert.Equal(2, NotificationPresentation.AskedByOnly([agent, person], null).Count);
    }

    [Fact]
    public void Kind_keeps_questions_alerts_or_whatever_is_still_open()
    {
        var question = View();
        var alert = View() with { Kind = NotificationKind.Alert, AnswerSpec = null };
        var answered = View() with { State = NotificationState.Answered };

        Assert.Equal([question, answered], NotificationPresentation.KindOnly([question, alert, answered], NotificationKindFilter.Questions));
        Assert.Equal([alert], NotificationPresentation.KindOnly([question, alert, answered], NotificationKindFilter.Alerts));
        Assert.Equal([question, alert], NotificationPresentation.KindOnly([question, alert, answered], NotificationKindFilter.Open));
    }

    [Fact]
    public void Nothing_scoped_keeps_every_notification()
    {
        Assert.True(NotificationPresentation.InScope(View(), [], new Dictionary<Guid, string?>()));
    }

    [Fact]
    public void A_scoped_repository_keeps_what_resolves_to_it_and_hides_the_brains()
    {
        var backlog = View();
        var brains = View();
        var fincent = View();
        var repositories = new Dictionary<Guid, string?> { [backlog.Id] = "backlog", [brains.Id] = null, [fincent.Id] = "fincent" };

        Assert.Equal([backlog], NotificationPresentation.ScopeOnly([backlog, brains, fincent], ["backlog"], repositories));
        Assert.Equal([backlog, fincent], NotificationPresentation.ScopeOnly([backlog, brains, fincent], ["backlog", "fincent"], repositories));
    }

    [Fact]
    public void The_brain_scoped_keeps_what_resolves_to_no_repository()
    {
        var backlog = View();
        var brains = View();
        var repositories = new Dictionary<Guid, string?> { [backlog.Id] = "backlog", [brains.Id] = null };

        Assert.Equal([brains], NotificationPresentation.ScopeOnly([backlog, brains], [BrainScope.Value], repositories));
    }

    [Fact]
    public void A_notification_whose_repository_could_not_be_read_is_kept_under_every_scope()
    {
        var unread = View();

        Assert.True(NotificationPresentation.InScope(unread, ["backlog"], new Dictionary<Guid, string?>()));
        Assert.True(NotificationPresentation.InScope(unread, [BrainScope.Value], new Dictionary<Guid, string?>()));
    }

    // --- Grouping -------------------------------------------------------------------------

    [Fact]
    public void The_default_grouping_is_by_repository_unless_one_scope_member_is_pressed()
    {
        Assert.Equal(NotificationGrouping.Repository, NotificationPresentation.DefaultGrouping([]));
        Assert.Equal(NotificationGrouping.Repository, NotificationPresentation.DefaultGrouping(["backlog", "fincent"]));
        Assert.Equal(NotificationGrouping.System, NotificationPresentation.DefaultGrouping(["backlog"]));
        Assert.Equal(NotificationGrouping.System, NotificationPresentation.DefaultGrouping([BrainScope.Value]));
    }

    [Fact]
    public void Grouped_by_repository_the_brains_sit_under_brain()
    {
        var backlog = View();
        var brains = View() with { CreatedAt = Now.AddMinutes(-5) };
        var repositories = new Dictionary<Guid, string?> { [backlog.Id] = "backlog", [brains.Id] = null };

        var groups = NotificationPresentation.Group([backlog, brains], NotificationGrouping.Repository, repositories);

        Assert.Equal(["backlog", "Brain"], groups.Select(group => group.Label));
    }

    [Fact]
    public void Questions_on_no_linked_task_share_one_group_when_grouped_by_who_asked()
    {
        static NotificationSubject Question() => new("spec-manager", "question", Guid.NewGuid().ToString("D"));
        var views = Enumerable.Range(0, 3).Select(_ => View(SpecManager) with { Subject = Question() }).ToList();

        var groups = NotificationPresentation.Group(views, NotificationGrouping.AskedBy, new Dictionary<Guid, string?>());

        Assert.Equal("AI agent", Assert.Single(groups).Label);
    }

    [Fact]
    public void Grouped_by_system_each_system_is_one_group()
    {
        var views = new[] { View(SpecManager), View(), View(SpecManager) };

        var groups = NotificationPresentation.Group(views, NotificationGrouping.System, new Dictionary<Guid, string?>());

        Assert.Equal(["Sessions", "spec-manager"], groups.Select(group => group.Label).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_subject_named_by_a_guid_is_headed_by_its_place_never_the_guid()
    {
        var view = View(SpecManager) with { Subject = new NotificationSubject("spec-manager", "question", "01a187e5-3f00-4c1e-9b8a-6d2b1c0e7f11") };

        var group = Assert.Single(NotificationPresentation.Group([view], NotificationGrouping.Subject, new Dictionary<Guid, string?>()));

        Assert.Equal("Billing · #7 Rounding in invoices", group.Label);
        Assert.DoesNotContain("01a187e5", group.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_subject_named_by_a_guid_with_no_place_is_headed_by_its_title()
    {
        var view = View() with { Subject = new NotificationSubject("spec-manager", "question", Guid.NewGuid().ToString("D")) };

        var group = Assert.Single(NotificationPresentation.Group([view], NotificationGrouping.Subject, new Dictionary<Guid, string?>()));

        Assert.Equal("Which rounding rule applies to invoice totals?", group.Label);
    }
}
