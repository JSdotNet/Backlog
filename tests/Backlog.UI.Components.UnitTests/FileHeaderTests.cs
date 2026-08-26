namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The half of a file pane that stays put, on its own.
///
/// <para>What it draws inside FileView is <c>FileViewTests</c>' and
/// <c>FileViewHeaderLayoutTests</c>' — those go through the pane and are the
/// reason this component's markup is what it is. What is here is the standalone
/// bargain: that every optional part draws nothing when it is absent, that the
/// flags the pane hands down are taken at face value, and that the callbacks ask
/// rather than tell.</para>
///
/// <para>Which matters because the storybook renders this without a pane around
/// it, and because a host that is not FileView may one day want the same header
/// over something that is not a file on disk.</para>
/// </summary>
public sealed class FileHeaderTests
{
    private const string Block = """
        status: adopted
        kind: runtime
        version: "10.0"
        """;

    private static readonly FileCompareBaseline[] Baselines =
    [
        new("opened", "As opened", "before"),
        new("commit", "Last commit", "older")
    ];

    private static IRenderedComponent<FileHeader> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<FileHeader>> extra) =>
        context.Render<FileHeader>(parameters =>
        {
            parameters.Add(header => header.Name, "shared.md").Add(header => header.TestId, "fh");
            extra(parameters);
        });

    [Fact]
    public void A_name_is_all_it_needs()
    {
        // The storybook's first story, and the reason it can be the first story: a
        // header handed nothing else draws the name and no empty scaffolding
        // around it.
        using var context = new BunitContext();
        var cut = Render(context, _ => { });

        Assert.Equal("shared.md", cut.Find(".file-view__name").TextContent);
        Assert.Empty(cut.FindAll(".file-view__meta"));
        Assert.Empty(cut.FindAll(".file-view__path"));
        Assert.Empty(cut.FindAll(".file-view__actions"));
        Assert.Empty(cut.FindAll(".file-view__baselines"));
    }

    [Fact]
    public void The_detail_line_is_source_then_kind_then_size()
    {
        // The order a reader asks for it in, and the separator the rest of the
        // product uses for a line of facts.
        using var context = new BunitContext();
        var cut = Render(context, parameters => parameters
            .Add(header => header.Source, "Repository")
            .Add(header => header.Kind, "Technology stack")
            .Add(header => header.SizeInBytes, 2048));

        Assert.Equal(
            $"Repository · Technology stack · {FileHeader.FormatSize(2048)}",
            cut.Find(".file-view__meta").TextContent);
    }

    [Fact]
    public void An_absent_fact_leaves_no_gap_in_the_detail_line()
    {
        using var context = new BunitContext();
        var cut = Render(context, parameters => parameters.Add(header => header.Kind, "Notes"));

        Assert.Equal("Notes", cut.Find(".file-view__meta").TextContent);
    }

    /// <summary>The modifier says which shape the identity column is in: holding a
    /// record, whose headline is the row the name's line is made of, or holding that
    /// line directly — in which case the column is the row and the stylesheet makes
    /// it one. It used to mean "and therefore takes the header's free space"; the
    /// column takes that either way now, because with no record it is the path
    /// rather than the status holding the far edge of the line.</summary>
    [Fact]
    public void A_record_marks_the_identity_column_as_the_one_holding_it()
    {
        using var context = new BunitContext();
        var cut = Render(context, parameters => parameters
            .Add(header => header.Metadata, MetadataReader.Parse(Block))
            .Add(header => header.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech)));

        Assert.Contains("file-view__identity--record", cut.Find(".file-view__identity").ClassName);
        Assert.NotNull(cut.Find(".knowledge-record"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_record_that_states_nothing_is_not_a_record(bool empty)
    {
        // The pane can only hand down null, because it drops an empty reading
        // before it gets here. A standalone caller can hand down
        // MetadataRecord.Empty, and the column has to answer the same way the
        // content does — otherwise the modifier claims a record that draws no
        // element at all.
        using var context = new BunitContext();
        var cut = Render(context, parameters => parameters
            .Add(header => header.Metadata, empty ? MetadataRecord.Empty : null));

        Assert.DoesNotContain("file-view__identity--record", cut.Find(".file-view__identity").ClassName);
        Assert.Empty(cut.FindAll(".knowledge-record"));
        Assert.Equal("shared.md", cut.Find(".file-view__name").TextContent);
    }

    [Fact]
    public void The_action_group_is_named_after_the_file()
    {
        // "Edit" and "Compare" on their own say nothing about which of several
        // panes on a page they belong to.
        using var context = new BunitContext();
        var cut = Render(context, parameters => parameters
            .Add(header => header.AllowCopy, true)
            .Add(header => header.Body, "text"));

        Assert.Equal("shared.md actions", cut.Find(".file-view__actions").GetAttribute("aria-label"));
    }

    [Fact]
    public void The_edit_control_says_which_of_its_two_meanings_it_has()
    {
        using var context = new BunitContext();

        var reading = Render(context, parameters => parameters.Add(header => header.OffersEdit, true));
        Assert.Equal("Edit", reading.Find("[data-testid='fh-edit']").GetAttribute("aria-label"));
        Assert.Equal("Edit", reading.Find("[data-testid='fh-edit']").GetAttribute("title"));

        var writing = Render(context, parameters => parameters
            .Add(header => header.OffersEdit, true)
            .Add(header => header.Editing, true));
        Assert.Equal("Done", writing.Find("[data-testid='fh-done']").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task The_controls_ask_rather_than_tell()
    {
        // Nothing here flips a mode. The modes are exclusive and the pane enforces
        // that, so both controls report and stop.
        using var context = new BunitContext();
        var edits = 0;
        bool? compared = null;

        var cut = Render(context, parameters => parameters
            .Add(header => header.OffersEdit, true)
            .Add(header => header.OffersCompare, true)
            .Add(header => header.OnToggleEditing, EventCallback.Factory.Create(this, () => edits++))
            .Add(header => header.OnComparingChanged,
                EventCallback.Factory.Create<bool>(this, asked => compared = asked)));

        await cut.Find("[data-testid='fh-edit']").ClickAsync(new());
        await cut.Find("[data-testid='fh-compare']").ClickAsync(new());

        Assert.Equal(1, edits);
        Assert.True(compared);
        Assert.Equal("Edit", cut.Find("[data-testid='fh-edit']").GetAttribute("aria-label"));
    }

    [Fact]
    public void The_baseline_picker_needs_a_comparison_and_a_choice()
    {
        using var context = new BunitContext();

        var notComparing = Render(context, parameters => parameters
            .Add(header => header.OffersCompare, true)
            .Add(header => header.CompareBaselines, Baselines));
        Assert.Empty(notComparing.FindAll(".file-view__baselines"));

        var oneBaseline = Render(context, parameters => parameters
            .Add(header => header.OffersCompare, true)
            .Add(header => header.Comparing, true)
            .Add(header => header.CompareBaselines, Baselines[..1]));
        Assert.Empty(oneBaseline.FindAll(".file-view__baselines"));

        var choice = Render(context, parameters => parameters
            .Add(header => header.OffersCompare, true)
            .Add(header => header.Comparing, true)
            .Add(header => header.CompareBaselines, Baselines));
        Assert.Equal(2, choice.FindAll(".file-view__baseline").Count);
    }

    [Fact]
    public void The_picker_highlights_the_baseline_it_is_told_about()
    {
        // Told, and not worked out: "the one asked for, or the first offered" is a
        // rule about a list the pane owns, so an id nothing on the list matches
        // highlights nothing rather than falling back to the first.
        using var context = new BunitContext();

        var named = Render(context, parameters => parameters
            .Add(header => header.OffersCompare, true)
            .Add(header => header.Comparing, true)
            .Add(header => header.CompareBaselines, Baselines)
            .Add(header => header.SelectedBaselineId, "commit"));
        Assert.Equal("true", named.Find("[data-testid='fh-baseline-commit']").GetAttribute("aria-pressed"));
        Assert.Equal("false", named.Find("[data-testid='fh-baseline-opened']").GetAttribute("aria-pressed"));

        var none = Render(context, parameters => parameters
            .Add(header => header.OffersCompare, true)
            .Add(header => header.Comparing, true)
            .Add(header => header.CompareBaselines, Baselines));
        Assert.Equal("false", none.Find("[data-testid='fh-baseline-opened']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task Picking_a_baseline_hands_the_baseline_back()
    {
        using var context = new BunitContext();
        FileCompareBaseline? picked = null;

        var cut = Render(context, parameters => parameters
            .Add(header => header.OffersCompare, true)
            .Add(header => header.Comparing, true)
            .Add(header => header.CompareBaselines, Baselines)
            .Add(header => header.SelectedBaselineId, "opened")
            .Add(header => header.OnSelectBaseline,
                EventCallback.Factory.Create<FileCompareBaseline>(this, baseline => picked = baseline)));

        await cut.Find("[data-testid='fh-baseline-commit']").ClickAsync(new());

        Assert.Equal("commit", picked?.Id);
    }

    [Fact]
    public void No_test_id_leaves_every_derived_one_off()
    {
        using var context = new BunitContext();
        var cut = context.Render<FileHeader>(parameters => parameters
            .Add(header => header.Name, "shared.md")
            .Add(header => header.AllowCopy, true)
            .Add(header => header.Body, "text")
            .Add(header => header.OffersEdit, true));

        Assert.Empty(cut.FindAll("[data-testid]"));
    }
}
