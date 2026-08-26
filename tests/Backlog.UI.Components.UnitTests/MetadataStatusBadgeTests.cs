namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The read-only status badge, and the one thing that must stay true of it: it is
/// the same element <c>KnowledgeStatusPill</c> has always rendered.
///
/// <para>The pill was the only shape of this, and it carried the folder lookup
/// inside it — which is what kept <c>MetadataView</c>'s headline tied to
/// <c>KnowledgeFolder</c>, because the headline needs the badge, the modifier and
/// the flag on a word nobody recognises, and all three lived behind a folder. So
/// the drawing moved here and the pill became the folder lookup over it: one
/// implementation, and no second opinion about what a status looks like.</para>
///
/// <para>Which makes the equality below the load-bearing test in this file. If
/// these ever diverge the stylesheet is painting one of them and not the other,
/// and the record's headline and a status drawn beside a file name stop reading as
/// one thing.</para>
/// </summary>
public sealed class MetadataStatusBadgeTests
{
    [Fact]
    public void It_renders_exactly_what_the_folder_shaped_pill_renders()
    {
        using var context = new BunitContext();

        foreach (var folder in Enum.GetValues<KnowledgeFolder>())
        {
            // Every word of the folder, plus one that is not — the flagged badge
            // is the most elaborate thing either component emits, so it is the
            // case worth pinning even though the record's headline would draw a
            // select for the others.
            foreach (var status in KnowledgeStatus.Values(folder).Append("shipped"))
            {
                var pill = context.Render<KnowledgeStatusPill>(parameters => parameters
                    .Add(p => p.Status, status)
                    .Add(p => p.Folder, folder));

                var badge = context.Render<MetadataStatusBadge>(parameters => parameters
                    .Add(b => b.Status, status)
                    .Add(b => b.Vocabulary, KnowledgeStatus.Vocabulary(folder)));

                Assert.Equal(pill.Markup, badge.Markup);
            }
        }
    }

    /// <summary>Nothing in, nothing out: a block that states no status should not
    /// leave an empty badge on the line. The guard is here rather than on the
    /// callers, so no caller can forget it.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_status_draws_no_element(string? status)
    {
        using var context = new BunitContext();

        var badge = context.Render<MetadataStatusBadge>(parameters => parameters
            .Add(b => b.Status, status)
            .Add(b => b.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech)));

        Assert.Empty(badge.Markup.Trim());
    }

    /// <summary>The word is printed as it was authored and never tidied up. The
    /// flag exists to make a typo visible, and a badge that normalised the word
    /// would hide the thing it is flagging.</summary>
    [Fact]
    public void The_word_is_drawn_as_it_was_written()
    {
        using var context = new BunitContext();

        var badge = context.Render<MetadataStatusBadge>(parameters => parameters
            .Add(b => b.Status, " Adopted ")
            .Add(b => b.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech)));

        var span = badge.Find("span.badge");

        Assert.Equal("Adopted", span.TextContent.Trim());

        // Recognised, so no flag — and the modifier the tone gives it.
        Assert.DoesNotContain("knowledge-status--unrecognised", span.ClassList);
        Assert.Contains("badge--status-active", span.ClassList);
    }

    /// <summary>A caller's own vocabulary reaches every part of the drawing: the
    /// modifier from its resolver, the flag from its list, and the expectation
    /// naming its words. This is the case the folder-shaped pill could never
    /// express.</summary>
    [Fact]
    public void A_caller_that_is_not_a_knowledge_folder_gets_the_whole_treatment()
    {
        var shipping = new MetadataStatusVocabulary(
            ["staged", "shipped"],
            status => status == "shipped" ? "done" : "ready");

        using var context = new BunitContext();

        var known = context.Render<MetadataStatusBadge>(parameters => parameters
            .Add(b => b.Status, "shipped")
            .Add(b => b.Vocabulary, shipping));

        Assert.Contains("badge--status-done", known.Find("span.badge").ClassList);
        Assert.False(known.Find("span.badge").HasAttribute("title"));

        var typo = context.Render<MetadataStatusBadge>(parameters => parameters
            .Add(b => b.Status, "shiped")
            .Add(b => b.Vocabulary, shipping));

        var flagged = typo.Find("span.badge");

        Assert.Contains("knowledge-status--unrecognised", flagged.ClassList);
        Assert.Contains("badge--status-archived", flagged.ClassList);
        Assert.Equal("Unexpected status. Expected one of: staged, shipped.", flagged.GetAttribute("title"));
    }

    /// <summary>The caller's own class goes last, after the flag, so nothing a
    /// caller passes displaces a class the stylesheet or a test matches on by exact
    /// position. Carried over from the pill, which is where the rule was written.</summary>
    [Fact]
    public void The_flag_comes_before_a_callers_own_class()
    {
        using var context = new BunitContext();

        var badge = context.Render<MetadataStatusBadge>(parameters => parameters
            .Add(b => b.Status, "shipped")
            .Add(b => b.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech))
            .Add(b => b.CssClass, "host-own")
            .Add(b => b.TestId, "status"));

        var span = badge.Find("span.badge");

        Assert.EndsWith("knowledge-status--unrecognised host-own", span.ClassName, StringComparison.Ordinal);
        Assert.Equal("status", span.GetAttribute("data-testid"));
    }
}
