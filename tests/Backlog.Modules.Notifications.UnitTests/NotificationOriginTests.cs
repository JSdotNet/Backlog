using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.DomainModels;

using static Backlog.Modules.Notifications.UnitTests.Drafts;

namespace Backlog.Modules.Notifications.UnitTests;

/// <summary>
/// Where a notification came from, in words (<c>.devbook/domain/notifications/domain.md#origin</c>):
/// the value object's bounds — System 40, Place 120, AskedBy 80, Via 40 characters, one line each,
/// and an http or https link — as the owner holds a draft to them, and as <see cref="NotificationOrigin.Fit"/>
/// cuts a source's words to them.
/// </summary>
public sealed class NotificationOriginTests
{
    private static readonly NotificationOrigin SpecManager =
        new("spec-manager", "Billing · #7 Rounding in invoices", "Ada Lovelace", "Agent session", "https://spec-manager.example/q/7");

    [Fact]
    public void The_bounds_are_forty_one_twenty_eighty_and_forty_characters()
    {
        Assert.Equal(40, NotificationOrigin.MaxSystemLength);
        Assert.Equal(120, NotificationOrigin.MaxPlaceLength);
        Assert.Equal(80, NotificationOrigin.MaxAskedByLength);
        Assert.Equal(40, NotificationOrigin.MaxViaLength);
    }

    [Fact]
    public void A_notification_keeps_the_origin_it_was_published_with()
    {
        var notification = Published(Question() with { Origin = SpecManager });

        Assert.Equal(SpecManager, notification.Origin);
    }

    [Fact]
    public void A_notification_without_an_origin_has_none()
    {
        Assert.Null(Published().Origin);
    }

    [Fact]
    public void An_origin_is_trimmed_and_its_blank_parts_dropped()
    {
        var notification = Published(Question() with { Origin = new NotificationOrigin("  Backlog ", " Pairing ", "  ", "", null) });

        Assert.Equal(new NotificationOrigin("Backlog", "Pairing"), notification.Origin);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_origin_without_a_system_is_refused(string system)
    {
        var result = Notification.Publish(Guid.NewGuid(), Owner, Question() with { Origin = new NotificationOrigin(system, "Pairing") }, Now);

        Assert.Equal("notification.origin_invalid", result.Error.Code);
    }

    [Theory]
    [InlineData(41, 0, 0, 0)]
    [InlineData(0, 121, 0, 0)]
    [InlineData(0, 0, 81, 0)]
    [InlineData(0, 0, 0, 41)]
    public void A_part_past_its_bound_is_refused(int system, int place, int askedBy, int via)
    {
        var origin = new NotificationOrigin(
            system == 0 ? "Backlog" : new string('s', system),
            place == 0 ? null : new string('p', place),
            askedBy == 0 ? null : new string('a', askedBy),
            via == 0 ? null : new string('v', via));

        var result = Notification.Publish(Guid.NewGuid(), Owner, Question() with { Origin = origin }, Now);

        Assert.Equal("notification.content_too_long", result.Error.Code);
    }

    [Fact]
    public void Every_part_at_its_bound_is_accepted()
    {
        var origin = new NotificationOrigin(new string('s', 40), new string('p', 120), new string('a', 80), new string('v', 40));

        var result = Notification.Publish(Guid.NewGuid(), Owner, Question() with { Origin = origin }, Now);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void A_part_that_runs_over_a_line_is_refused()
    {
        var result = Notification.Publish(Guid.NewGuid(), Owner, Question() with { Origin = new NotificationOrigin("Backlog", "Pairing\nnow") }, Now);

        Assert.Equal("notification.origin_invalid", result.Error.Code);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("backlog://devbook/_/arc42.md")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.test/q")]
    public void A_link_that_is_not_http_or_https_is_refused(string link)
    {
        var result = Notification.Publish(Guid.NewGuid(), Owner, Question() with { Origin = new NotificationOrigin("spec-manager", OpenLink: link) }, Now);

        Assert.Equal("notification.origin_invalid", result.Error.Code);
    }

    [Fact]
    public void The_line_is_system_and_place_or_the_system_alone()
    {
        Assert.Equal("spec-manager · Billing · #7 Rounding in invoices", SpecManager.Line);
        Assert.Equal("Backlog", new NotificationOrigin("Backlog").Line);
    }

    [Fact]
    public void The_line_cut_to_a_length_ends_in_an_ellipsis()
    {
        var cut = SpecManager.LineWithin(20);

        Assert.Equal(20, cut.Length);
        Assert.Equal("spec-manager · Bill…", cut);
        Assert.Equal("Backlog", new NotificationOrigin("Backlog").LineWithin(20));
    }

    [Fact]
    public void Fit_cuts_every_part_to_its_bound_and_makes_it_one_line()
    {
        var origin = NotificationOrigin.Fit(
            new string('s', 50),
            "JSdotNet/Backlog ·\n" + new string('p', 200),
            new string('a', 90),
            new string('v', 50))!;

        Assert.Equal(40, origin.System.Length);
        Assert.EndsWith("…", origin.System, StringComparison.Ordinal);
        Assert.Equal(120, origin.Place!.Length);
        Assert.DoesNotContain('\n', origin.Place);
        Assert.Equal(80, origin.AskedBy!.Length);
        Assert.Equal(40, origin.Via!.Length);
        Assert.True(Notification.Publish(Guid.NewGuid(), Owner, Question() with { Origin = origin }, Now).IsSuccess);
    }

    [Fact]
    public void Fit_drops_blank_parts_and_a_link_it_would_refuse()
    {
        var origin = NotificationOrigin.Fit("Backlog", " ", null, "", "backlog://devbook/_/arc42.md");

        Assert.Equal(new NotificationOrigin("Backlog"), origin);
        Assert.Equal("https://example.test/q", NotificationOrigin.Fit("x", openLink: " https://example.test/q ")!.OpenLink);
    }

    [Fact]
    public void Fit_without_a_system_is_no_origin()
    {
        Assert.Null(NotificationOrigin.Fit("  ", "Pairing"));
        Assert.Null(NotificationOrigin.Fit(null));
    }
}
