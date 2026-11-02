namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A decision or debt record's state, drawn where the status is. It is not in the
/// record's block — the tool keeps a subject's workflow state, not git — so a host
/// hands it in already in words, and what is worth pinning is how it reads, that
/// nothing is drawn for nothing, that it goes with the status, that its select
/// answers to the state's own options and a listening host rather than to whether
/// the file may be written, and that the state is one status badge on the tone
/// scale whether or not it can be changed.
/// </summary>
public sealed class MetadataSubjectStateTests
{
    private static readonly MetadataSubjectState Planned = new("debt", "planned", "You", "2026-10-30");

    private static readonly IReadOnlyList<string> DebtStates = ["identified", "planned", "in-progress", "resolved"];

    /// <summary>The line says who set the state and when; the word itself is the
    /// status badge's, so it is not said twice.</summary>
    [Fact]
    public void The_line_states_the_kind_who_and_when()
    {
        using var context = new BunitContext();

        var line = context.Render<MetadataStateLine>(parameters => parameters
            .Add(l => l.Subject, Planned));

        Assert.Equal("debt state set by You on 2026-10-30", line.Find("[data-testid='devbook-state-subject']").TextContent.Trim());
    }

    /// <summary>The subject's sign-off leads: it is where the record stands, and
    /// the block's own approval qualifies it.</summary>
    [Fact]
    public void The_subjects_state_comes_before_the_blocks_own()
    {
        using var context = new BunitContext();

        var line = context.Render<MetadataStateLine>(parameters => parameters
            .Add(l => l.State, new MetadataState { ApprovedBy = "ana" })
            .Add(l => l.Subject, Planned));

        Assert.Equal(
            ["devbook-state-subject", "devbook-state-approval"],
            line.FindAll(".devbook-record__state > *").Select(element => element.GetAttribute("data-testid")));
    }

    [Fact]
    public void A_subject_nobody_has_set_a_state_on_draws_nothing()
    {
        using var context = new BunitContext();

        var line = context.Render<MetadataStateLine>(parameters => parameters
            .Add(l => l.Subject, new MetadataSubjectState("debt", null)));

        Assert.Equal(string.Empty, line.Markup.Trim());
    }

    /// <summary>A state with nobody and no day to name has nothing for the line
    /// to say; the badge alone carries it.</summary>
    [Fact]
    public void A_state_with_no_author_and_no_day_leaves_the_line_empty()
    {
        using var context = new BunitContext();

        var line = context.Render<MetadataStateLine>(parameters => parameters
            .Add(l => l.Subject, new MetadataSubjectState("debt", "planned")));

        Assert.Equal(string.Empty, line.Markup.Trim());
    }

    /// <summary>The value is drawn once — as the status badge beside the block's,
    /// toned on the same scale — and the line beside it names who and when.</summary>
    [Fact]
    public void A_record_with_a_subject_state_draws_it_once_as_a_status_badge()
    {
        using var context = new BunitContext();

        var view = context.Render<MetadataView>(parameters => parameters
            .Add(v => v.Metadata, MetadataRecord.Empty)
            .Add(v => v.Subject, Planned));

        var badge = view.Find(".devbook-record__headline [data-testid='devbook-subject-state']");
        Assert.Equal("planned", badge.TextContent.Trim());
        Assert.Contains("badge--status-active", badge.ClassList);
        Assert.Equal("debt state set by You on 2026-10-30", view.Find("[data-testid='devbook-state-subject']").TextContent.Trim());

        // "planned" appears once in the whole headline: the badge says it, the
        // line does not.
        Assert.Equal(2, view.Find(".devbook-record__headline").TextContent.Split("planned").Length);
    }

    /// <summary>The status display switched off takes the subject's state with
    /// it, select and all.</summary>
    [Fact]
    public void With_the_status_hidden_the_subject_state_is_not_drawn()
    {
        using var context = new BunitContext();

        var view = context.Render<MetadataView>(parameters => parameters
            .Add(v => v.Metadata, MetadataRecord.Empty)
            .Add(v => v.ShowStatus, false)
            .Add(v => v.Subject, Planned with { Options = ["identified", "planned"] })
            .Add(v => v.OnSubjectStateChanged, (string _) => { }));

        Assert.Empty(view.FindAll("[data-testid='devbook-state-subject']"));
        Assert.Empty(view.FindAll("[data-testid='devbook-subject-state']"));
        Assert.Empty(view.FindAll("[data-testid='devbook-subject-state-select']"));
    }

    /// <summary>
    /// Offered by its options, not by <see cref="MetadataView.CanEditStatus"/>:
    /// a state is no file write, so a surface that may not write the file — a
    /// branch snapshot — still offers it. A pick is raised for the host to write.
    /// Unset, the select reads "No state" in the quiet tone.
    /// </summary>
    [Fact]
    public void A_subject_with_options_is_offered_where_the_status_cannot_be_edited()
    {
        using var context = new BunitContext();
        string? picked = null;

        var view = context.Render<MetadataView>(parameters => parameters
            .Add(v => v.Metadata, MetadataReader.Parse("status: draft"))
            .Add(v => v.CanEditStatus, false)
            .Add(v => v.Subject, new MetadataSubjectState("debt", null) { Options = DebtStates })
            .Add(v => v.OnSubjectStateChanged, (string state) => picked = state));

        var select = view.Find("[data-testid='devbook-subject-state-select']");
        Assert.Contains("badge--status-draft", select.ClassList);
        Assert.Equal("No state", select.QuerySelector("option[value='']")!.TextContent);

        view.Find("[data-testid='devbook-subject-state-select'] select").Change("planned");

        Assert.Equal("planned", picked);
        // The file's own status stays the badge: the one select is the state's.
        Assert.Single(view.FindAll("select"));
    }

    /// <summary>A state with no options is shown and not offered — a decision
    /// record's, whose key needs the pull request it is decided in.</summary>
    [Fact]
    public void A_subject_with_no_options_is_shown_and_not_offered()
    {
        using var context = new BunitContext();

        var view = context.Render<MetadataView>(parameters => parameters
            .Add(v => v.Metadata, MetadataRecord.Empty)
            .Add(v => v.Subject, new MetadataSubjectState("decision", "decided", "You", "2026-10-30"))
            .Add(v => v.OnSubjectStateChanged, (string _) => { }));

        Assert.Equal("decided", view.Find("[data-testid='devbook-subject-state']").TextContent.Trim());
        Assert.Equal("decision state set by You on 2026-10-30", view.Find("[data-testid='devbook-state-subject']").TextContent.Trim());
        Assert.Empty(view.FindAll("[data-testid='devbook-subject-state-select']"));
    }

    /// <summary>
    /// A state that can be changed is drawn as the same badge as one that cannot
    /// (<c>color-scheme.md</c>): the select and the read-only badge wear the same
    /// modifier, and it is the tone scale's — identified quiet, planned and in
    /// progress live, resolved and decided settled, declined out of force.
    /// </summary>
    [Theory]
    [InlineData("debt", "identified", "draft")]
    [InlineData("debt", "planned", "active")]
    [InlineData("debt", "in-progress", "active")]
    [InlineData("debt", "resolved", "done")]
    [InlineData("decision", "proposed", "draft")]
    [InlineData("decision", "decided", "done")]
    [InlineData("decision", "declined", "archived")]
    public void An_editable_and_a_read_only_state_wear_the_same_toned_badge(string kind, string state, string slug)
    {
        using var context = new BunitContext();
        var subject = new MetadataSubjectState(kind, state, "You", "2026-10-30");

        var editable = context.Render<MetadataView>(parameters => parameters
            .Add(v => v.Metadata, MetadataRecord.Empty)
            .Add(v => v.Subject, subject with { Options = [state] })
            .Add(v => v.OnSubjectStateChanged, (string _) => { }));
        var readOnly = context.Render<MetadataView>(parameters => parameters
            .Add(v => v.Metadata, MetadataRecord.Empty)
            .Add(v => v.Subject, subject));

        Assert.Contains($"badge--status-{slug}", editable.Find("[data-testid='devbook-subject-state-select']").ClassList);
        Assert.Contains($"badge--status-{slug}", readOnly.Find("[data-testid='devbook-subject-state']").ClassList);
        Assert.Empty(editable.FindAll("[data-testid='devbook-subject-state']"));
    }

    /// <summary>A file whose block states nothing still names itself and its
    /// state: the header's record is drawn for the subject alone.</summary>
    [Fact]
    public void A_file_with_an_empty_block_and_a_subject_state_gets_its_headline()
    {
        using var context = new BunitContext();

        var view = context.Render<MetadataFileView>(parameters => parameters
            .Add(v => v.Metadata, MetadataRecord.Empty)
            .Add(v => v.Subject, Planned)
            .Add(v => v.Heading, (RenderFragment)(builder => builder.AddMarkupContent(0, "<h2>TDR 0001</h2>"))));

        Assert.Single(view.FindAll(".devbook-record__headline h2"));
        Assert.Single(view.FindAll("[data-testid='devbook-subject-state']"));
    }

    /// <summary>
    /// Options with nobody listening are no control, and with no state there is
    /// nothing else to draw: an empty block stands down rather than leaving a
    /// bordered, labelled group around an empty line.
    /// </summary>
    [Fact]
    public void Options_with_no_listener_and_no_state_draw_no_record()
    {
        using var context = new BunitContext();
        var unset = new MetadataSubjectState("debt", null) { Options = DebtStates };

        var view = context.Render<MetadataView>(parameters => parameters
            .Add(v => v.Metadata, MetadataRecord.Empty)
            .Add(v => v.Subject, unset));

        Assert.Equal(string.Empty, view.Markup.Trim());
    }

    /// <summary>The file header asks the same question before it hands an empty
    /// block down, so it draws the heading alone.</summary>
    [Fact]
    public void A_file_with_options_no_listener_and_no_state_draws_its_heading_alone()
    {
        using var context = new BunitContext();
        var unset = new MetadataSubjectState("debt", null) { Options = DebtStates };

        var view = context.Render<MetadataFileView>(parameters => parameters
            .Add(v => v.Metadata, MetadataRecord.Empty)
            .Add(v => v.Subject, unset)
            .Add(v => v.Heading, (RenderFragment)(builder => builder.AddMarkupContent(0, "<h2>TDR 0001</h2>")))
            .Add(v => v.HeadingAlone, (RenderFragment)(builder => builder.AddMarkupContent(0, "<h2>TDR 0001</h2>"))));

        Assert.Empty(view.FindAll(".devbook-record"));
        Assert.Single(view.FindAll("h2"));
    }
}
