using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.UI;
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
    public void The_card_carries_the_origin_whole()
    {
        var card = NotificationPresentation.ToCard(View(SpecManager), Now, Owner);

        Assert.Equal(
            new NotificationCardOrigin("spec-manager", "Billing · #7 Rounding in invoices", "Ada Lovelace", "Agent session", "https://spec-manager.example/q/7"),
            card.Origin);
    }

    [Fact]
    public void A_card_without_an_origin_has_none_and_names_its_source()
    {
        var card = NotificationPresentation.ToCard(View(), Now, Owner);

        Assert.Null(card.Origin);
        Assert.Equal("Sessions", card.Source);
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
}
