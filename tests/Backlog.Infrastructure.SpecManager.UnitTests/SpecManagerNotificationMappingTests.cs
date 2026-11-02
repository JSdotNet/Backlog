using System.Text;

using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Infrastructure.SpecManager.Questions;
using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// One of the person's other spec-manager notifications — a mention, a reply, a change
/// proposal — as the alert that says it in Backlog: its id, what it says, where it opens and
/// where it came from; and which of them come in at all.
/// </summary>
public sealed class SpecManagerNotificationMappingTests
{
    private const string Root = "https://spec.test";

    private static readonly DateTimeOffset FirstSignIn = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly SpecManagerNotificationDto Mention = new(
        new Guid("50000000-0000-0000-0000-00000000000A"),
        "Mention",
        "Kees mentioned you on #7 Rounding in invoices",
        "@Jip can you check the\nrounding rule before Friday?",
        "/producten/billing/backlog?item=7",
        new Guid("10000000-0000-0000-0000-000000000001"),
        new DateTimeOffset(2026, 10, 5, 11, 0, 0, TimeSpan.Zero),
        IsRead: false);

    [Fact]
    public void The_id_is_derived_from_the_installation_and_the_notification_and_never_a_question_id()
    {
        var id = SpecManagerQuestionIds.ForNotification(Root, Mention.Id);

        Assert.Equal(id, SpecManagerQuestionIds.ForNotification(Root + "/", Mention.Id));
        Assert.NotEqual(id, SpecManagerQuestionIds.ForNotification("https://other.test", Mention.Id));
        Assert.NotEqual(id, SpecManagerQuestionIds.For(Root, Mention.Id.ToString()));
        Assert.NotEqual(id, SpecManagerQuestionIds.RefusalFor(Root, Mention.Id.ToString()));
    }

    /// <summary>The scheme is pinned: a change would publish every unread one a second time
    /// beside the first.</summary>
    [Fact]
    public void The_id_scheme_is_pinned()
    {
        var installation = SpecManagerQuestionIds.Version5(new Guid("6ba7b811-9dad-11d1-80b4-00c04fd430c8"), Root);

        Assert.Equal(
            SpecManagerQuestionIds.Version5(installation, "notification:50000000-0000-0000-0000-00000000000a"),
            SpecManagerQuestionIds.ForNotification(Root, Mention.Id));
    }

    [Fact]
    public void A_mention_is_a_warning_alert_named_by_its_notification()
    {
        var draft = SpecManagerNotificationMapper.ToDraft(Mention, Root, defaultInstallation: false, "Billing");

        Assert.Equal(NotificationKind.Alert, draft.Kind);
        Assert.Equal(NotificationSeverity.Warning, draft.Severity);
        Assert.Equal(new NotificationSource("spec-manager", "notification/50000000-0000-0000-0000-00000000000a"), draft.Source);
        Assert.Null(draft.Subject);
        Assert.Null(draft.Answer);
        Assert.Equal(SpecManagerQuestionIds.ForNotification(Root, Mention.Id), draft.Id);
    }

    [Theory]
    [InlineData("Reply")]
    [InlineData("ChangeProposal")]
    [InlineData("Input")]
    [InlineData("ReviewWork")]
    [InlineData("Administration")]
    public void Every_other_category_is_an_info_alert(string category)
    {
        var draft = SpecManagerNotificationMapper.ToDraft(Mention with { Category = category }, Root, false, "Billing");

        Assert.Equal(NotificationSeverity.Info, draft.Severity);
    }

    [Fact]
    public void The_subject_is_the_title_and_the_text_the_summary_and_the_body()
    {
        var draft = SpecManagerNotificationMapper.ToDraft(Mention, Root, false, "Billing");

        Assert.Equal("Kees mentioned you on #7 Rounding in invoices", draft.Title);
        Assert.Equal("@Jip can you check the rounding rule before Friday?", draft.Summary);
        Assert.Equal("@Jip can you check the\nrounding rule before Friday?", draft.BodyMarkdown);
    }

    [Fact]
    public void The_title_summary_and_body_are_cut_to_their_bounds()
    {
        var draft = SpecManagerNotificationMapper.ToDraft(
            Mention with { Subject = new string('s', 200), Text = new string('é', 40_000) },
            Root,
            false,
            "Billing");

        Assert.Equal(80, draft.Title.Length);
        Assert.EndsWith("…", draft.Title, StringComparison.Ordinal);
        Assert.Equal(180, draft.Summary.Length);
        Assert.True(Encoding.UTF8.GetByteCount(draft.BodyMarkdown) <= 32 * 1024);
        Assert.EndsWith("…", draft.BodyMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_notification_without_a_subject_is_titled_by_its_category()
    {
        var draft = SpecManagerNotificationMapper.ToDraft(Mention with { Category = "ChangeProposal", Subject = "  " }, Root, false, null);

        Assert.Equal("Change proposal", draft.Title);
    }

    [Theory]
    [InlineData("/producten/billing/backlog?item=7", "https://spec.test/producten/billing/backlog?item=7")]
    [InlineData("producten/billing", "https://spec.test/producten/billing")]
    [InlineData("https://elsewhere.test/page", "https://elsewhere.test/page")]
    public void The_link_opens_in_spec_manager_without_a_double_slash(string link, string expected)
    {
        var draft = SpecManagerNotificationMapper.ToDraft(Mention with { Link = link }, Root + "/", false, "Billing");

        Assert.Equal(expected, draft.DeepLink);
        Assert.Equal(expected, draft.Origin?.OpenLink);
    }

    [Fact]
    public void A_notification_without_a_link_opens_nothing()
    {
        var draft = SpecManagerNotificationMapper.ToDraft(Mention with { Link = " " }, Root, false, "Billing");

        Assert.Null(draft.DeepLink);
    }

    [Fact]
    public void The_origin_names_the_installation_the_product_and_the_category()
    {
        var draft = SpecManagerNotificationMapper.ToDraft(Mention, Root, defaultInstallation: false, "Billing");

        Assert.Equal(new NotificationOrigin("spec-manager (spec.test)", "Billing", null, "Mention", "https://spec.test/producten/billing/backlog?item=7"), draft.Origin);
    }

    [Fact]
    public void The_installation_that_ships_is_named_spec_manager_alone()
    {
        var draft = SpecManagerNotificationMapper.ToDraft(Mention, Root, defaultInstallation: true, "Billing");

        Assert.Equal("spec-manager", draft.Origin?.System);
    }

    [Fact]
    public void A_notification_whose_product_is_unknown_names_no_place()
    {
        var draft = SpecManagerNotificationMapper.ToDraft(Mention, Root, false, productName: null);

        Assert.Null(draft.Origin?.Place);
    }

    [Fact]
    public void A_product_name_longer_than_the_place_holds_is_cut_to_fit()
    {
        var draft = SpecManagerNotificationMapper.ToDraft(Mention, Root, false, new string('p', 300));

        Assert.Equal(NotificationOrigin.MaxPlaceLength, draft.Origin?.Place?.Length);
    }

    [Theory]
    [InlineData("Mention", "Mention")]
    [InlineData("Reply", "Reply")]
    [InlineData("ChangeProposal", "Change proposal")]
    [InlineData("Input", "Input")]
    [InlineData("ReviewWork", "Review work")]
    [InlineData("Administration", "Administration")]
    [InlineData("SecurityReviewRequest", "Security review request")]
    public void The_category_is_said_in_words(string category, string words) =>
        Assert.Equal(words, SpecManagerNotificationMapper.ViaOf(category));

    [Fact]
    public void An_unread_notification_from_after_the_first_sign_in_comes_in() =>
        Assert.True(SpecManagerNotificationMapper.Admits(Mention, FirstSignIn));

    [Fact]
    public void One_created_at_the_first_sign_in_comes_in() =>
        Assert.True(SpecManagerNotificationMapper.Admits(Mention with { CreatedAt = FirstSignIn }, FirstSignIn));

    [Fact]
    public void One_from_before_the_first_sign_in_stays_out() =>
        Assert.False(SpecManagerNotificationMapper.Admits(Mention with { CreatedAt = FirstSignIn.AddTicks(-1) }, FirstSignIn));

    [Fact]
    public void A_read_one_stays_out() =>
        Assert.False(SpecManagerNotificationMapper.Admits(Mention with { IsRead = true }, FirstSignIn));

    [Fact]
    public void A_question_stays_out_since_it_comes_in_as_a_question() =>
        Assert.False(SpecManagerNotificationMapper.Admits(Mention with { Category = "BacklogQuestion" }, FirstSignIn));

    [Fact]
    public void A_category_this_build_does_not_know_comes_in_as_an_info_alert()
    {
        var unknown = Mention with { Category = "SecurityReviewRequest" };

        Assert.True(SpecManagerNotificationMapper.Admits(unknown, FirstSignIn));
        Assert.Equal(NotificationSeverity.Info, SpecManagerNotificationMapper.ToDraft(unknown, Root, false, null).Severity);
    }
}
