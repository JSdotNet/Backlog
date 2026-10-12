using Backlog.Modules.Inbox.Abstractions;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// The tokens an inbox item is stored and travels as, pinned member by member,
/// and the one place the map differs from Tasks' on purpose: an unknown kind is
/// read as <see cref="ContentKind.Text"/> rather than thrown on. The tokens are
/// persisted values, so every one is listed by hand.
/// </summary>
public sealed class InboxEnumMapTests
{
    public static TheoryData<InboxStatus, string> Statuses => new()
    {
        { InboxStatus.Unprocessed, "unprocessed" },
        { InboxStatus.Triaged, "triaged" },
        { InboxStatus.Deferred, "deferred" },
        { InboxStatus.Archived, "archived" }
    };

    public static TheoryData<ContentKind, string> Kinds => new()
    {
        { ContentKind.Text, "text" },
        { ContentKind.Article, "article" },
        { ContentKind.Link, "link" },
        { ContentKind.YouTube, "youtube" },
        { ContentKind.Image, "image" },
        { ContentKind.Document, "document" },
        { ContentKind.Email, "email" },
        { ContentKind.Code, "code" },
        { ContentKind.Voice, "voice" },
        { ContentKind.ClaudeArtifact, "claude-artifact" },
        { ContentKind.Note, "note" }
    };

    public static TheoryData<RoutingDomain, string> Domains => new()
    {
        { RoutingDomain.Tasks, "tasks" },
        { RoutingDomain.Devbook, "devbook" },
        { RoutingDomain.Archive, "archive" }
    };

    [Theory]
    [MemberData(nameof(Statuses))]
    public void A_status_round_trips_through_its_token(InboxStatus member, string token)
    {
        Assert.Equal(token, InboxEnumMap.ToWire(member));
        Assert.Equal(member, InboxEnumMap.ParseStatus(token));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_kind_round_trips_through_its_token(ContentKind member, string token)
    {
        Assert.Equal(token, InboxEnumMap.ToWire(member));
        Assert.Equal(member, InboxEnumMap.ParseKind(token));
    }

    [Theory]
    [MemberData(nameof(Domains))]
    public void A_routing_domain_round_trips_through_its_token(RoutingDomain member, string token)
    {
        Assert.Equal(token, InboxEnumMap.ToWire(member));
        Assert.Equal(member, InboxEnumMap.ParseRoutingDomain(token));
    }

    [Fact]
    public void Every_member_of_every_enum_has_a_token()
    {
        Assert.Equal(Enum.GetValues<InboxStatus>().Length, Statuses.Count);
        Assert.Equal(Enum.GetValues<ContentKind>().Length, Kinds.Count);
        Assert.Equal(Enum.GetValues<RoutingDomain>().Length, Domains.Count);
    }

    /// <summary>The token the Devbook domain carried before the context was
    /// renamed still reads, in any spelling.</summary>
    [Theory]
    [InlineData("secondbrain")]
    [InlineData("second_brain")]
    [InlineData("Second-Brain")]
    public void The_legacy_second_brain_token_still_reads_as_devbook(string token) =>
        Assert.Equal(RoutingDomain.Devbook, InboxEnumMap.ParseRoutingDomain(token));

    [Theory]
    [InlineData("claude_artifact")]
    [InlineData("ClaudeArtifact")]
    [InlineData(" CLAUDE-ARTIFACT ")]
    public void A_kind_is_read_whatever_its_spelling(string token) =>
        Assert.Equal(ContentKind.ClaudeArtifact, InboxEnumMap.ParseKind(token));

    /// <summary>A kind this build does not know is a reading of the capture, not
    /// a decision about it, so it is shown as text rather than breaking the
    /// queue.</summary>
    [Theory]
    [InlineData("hologram")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unknown_kind_is_read_as_text(string? token) =>
        Assert.Equal(ContentKind.Text, InboxEnumMap.ParseKind(token));

    [Fact]
    public void An_unknown_status_or_routing_domain_throws()
    {
        Assert.Equal("Unknown inbox status 'pending'.", Assert.Throws<FormatException>(() => InboxEnumMap.ParseStatus("pending")).Message);
        Assert.Equal("Unknown routing domain 'notes'.", Assert.Throws<FormatException>(() => InboxEnumMap.ParseRoutingDomain("notes")).Message);
    }

    [Fact]
    public void A_member_outside_the_enum_cannot_be_written()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InboxEnumMap.ToWire((InboxStatus)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => InboxEnumMap.ToWire((ContentKind)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => InboxEnumMap.ToWire((RoutingDomain)99));
    }
}
