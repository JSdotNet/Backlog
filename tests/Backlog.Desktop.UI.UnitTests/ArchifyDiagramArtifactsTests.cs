using System.Text;
using System.Text.Json;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Diagrams;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The adapter turns "what exists for this diagram?" into a question about files
/// in whichever clone the chapters were read out of. Everything worth pinning is
/// a decision about disk: the flag, the fence scan, the hash lookup, and the one
/// answer that has to be said out loud — an artifact authored from a fence that
/// has since been edited.
/// <para>
/// The fixture is a clone rather than a mock of one, because the parts that can
/// be got wrong are all path shaped: where <c>_archify/</c> sits, how a
/// specification is named from the chapter and the ordinal, and whether a fence
/// written with CRLF still hashes to the key the generator filed it under.
/// </para>
/// </summary>
public sealed class ArchifyDiagramArtifactsTests
{
    private const string Flowchart = "flowchart TD\n    A[Start] --> B[Stop]";

    private const string EditedFlowchart = "flowchart TD\n    A[Start] --> B[Stop]\n    B --> C[Archive]";

    private const string ClassDiagram = "classDiagram\n    class Order {\n        +OrderId Id\n    }";

    // Two arc42 chapters that live in one folder and therefore share one index.
    // Both have a diagram 1, and the two are of different kinds, so a lookup that
    // attributed one chapter's entry to the other would answer with the wrong
    // Archify type as well as the wrong verdict.
    private const string RuntimeChapter = "06-runtime-view.md";

    private const string DeploymentChapter = "07-deployment-view.md";

    private const string RuntimeFlow = "flowchart TD\n    Client --> Api\n    Api --> Store";

    private const string EditedRuntimeFlow = "flowchart TD\n    Client --> Api\n    Api --> Store\n    Store --> Archive";

    private const string DeploymentSequence = "sequenceDiagram\n    Desktop ->> Cloud: sync";

    /// <summary>When the fixture's chapters were last written. Stated rather than
    /// taken from the clock, because the signature that decides whether to rebuild
    /// compares last-write-times, and two files written in the same millisecond
    /// would make a test about the rule into a test about filesystem timestamp
    /// granularity.</summary>
    private static readonly DateTime Written = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The flag is the whole feature's off switch, so it is asked before anything
    /// on disk is believed. An artifact that is sitting right there and a chapter
    /// that matches it are not enough.
    /// </summary>
    [Fact]
    public void With_the_feature_switched_off_an_artifact_on_disk_is_not_found()
    {
        using var workspace = ArchifyWorkspace.Create(archifyDiagrams: false);
        workspace.WriteChapter("flow.md", Flowchart);
        workspace.WriteArtifact("flow.md", Flowchart, "workflow");

        using var artifacts = workspace.Artifacts();

        Assert.Null(artifacts.Find(Flowchart, "mermaid"));
    }

    [Fact]
    public void An_artifact_the_index_files_under_this_fences_hash_is_the_one_that_is_shown()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart);
        workspace.WriteArtifact("flow.md", Flowchart, "workflow");

        using var artifacts = workspace.Artifacts();
        var found = artifacts.Find(Flowchart, "mermaid");

        Assert.NotNull(found);
        Assert.Equal(ArchifyWorkspace.ArtifactDocument, found.Html);
        Assert.Equal(workspace.ArtifactFile("flow.1.workflow.html"), found.ArtifactPath);
        Assert.Equal(workspace.ArtifactFile("flow.1.workflow.json"), found.SpecPath);
        Assert.Equal("workflow", found.ArchifyType);
        Assert.False(found.IsOutOfDate);
        Assert.True(found.CanRender);
    }

    /// <summary>
    /// The drift case the design exists for. An Archify artifact is a
    /// re-authoring rather than a rendering, so nothing in it points back at the
    /// mermaid it came from; only the recorded hash can notice that the fence has
    /// moved on. When it has, the picture must not be shown — and the reader must
    /// be told why they are looking at mermaid instead.
    /// </summary>
    [Fact]
    public void An_artifact_authored_before_the_fence_was_edited_is_withheld_and_reported_as_out_of_date()
    {
        using var workspace = ArchifyWorkspace.Create();

        // The index was written when the fence still said what Flowchart says.
        workspace.WriteArtifact("flow.md", Flowchart, "workflow");

        // Then somebody edited the diagram. Same chapter, same ordinal, new text.
        workspace.WriteChapter("flow.md", EditedFlowchart);

        using var artifacts = workspace.Artifacts();
        var found = artifacts.Find(EditedFlowchart, "mermaid");

        Assert.NotNull(found);
        Assert.Null(found.Html);
        Assert.Null(found.ArtifactPath);
        Assert.False(found.CanRender);
        Assert.True(found.IsOutOfDate);

        // The specification is still the right one to re-render from, so the
        // reader is offered the mechanical half rather than sent to an agent.
        Assert.Equal(workspace.ArtifactFile("flow.1.workflow.json"), found.SpecPath);
        Assert.Equal("workflow", found.ArchifyType);
    }

    [Fact]
    public void A_specification_nobody_has_rendered_yet_is_found_without_an_index_entry()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart);
        workspace.WriteSpecification("flow.1.workflow.json");

        using var artifacts = workspace.Artifacts();
        var found = artifacts.Find(Flowchart, "mermaid");

        Assert.NotNull(found);
        Assert.Equal(workspace.ArtifactFile("flow.1.workflow.json"), found.SpecPath);
        Assert.Null(found.Html);
        Assert.Null(found.ArtifactPath);
        Assert.False(found.IsOutOfDate);
        Assert.Equal("workflow", found.ArchifyType);
    }

    /// <summary>
    /// The specification is found by looking, not by predicting its name. A
    /// flowchart defaults to <c>workflow</c>, but a flowchart that really
    /// describes a structure is authored as <c>architecture</c> — and computing
    /// the filename from the default made that specification invisible, so the
    /// render button never appeared for a diagram that was sitting there ready to
    /// render. The type on disk is also the type reported, because the author who
    /// named the file is the one who decided.
    /// </summary>
    [Fact]
    public void A_specification_authored_at_a_type_that_is_not_the_default_for_the_kind_is_still_found()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart);
        workspace.WriteSpecification("flow.1.architecture.json");

        using var artifacts = workspace.Artifacts();
        var found = artifacts.Find(Flowchart, "mermaid");

        Assert.NotNull(found);
        Assert.Equal(workspace.ArtifactFile("flow.1.architecture.json"), found.SpecPath);
        Assert.Equal("architecture", found.ArchifyType);
        Assert.Equal("showcase", found.Quality);
        Assert.Null(found.Html);
        Assert.False(found.IsOutOfDate);
    }

    /// <summary>
    /// The quality opt-out, which is a filename segment rather than a field
    /// inside the specification or a list beside it — everything the render step
    /// needs stays in the name, so nothing can disagree with where the file sits.
    /// Two of this repository's diagrams are non-planar and therefore cannot ever
    /// pass the showcase profile, so they say <c>standard</c> in their name and
    /// this side has to read the name they actually have.
    /// </summary>
    [Fact]
    public void A_specification_that_opts_out_of_showcase_is_found_under_its_standard_name()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart);
        workspace.WriteSpecification("flow.1.workflow.standard.json");

        using var artifacts = workspace.Artifacts();
        var found = artifacts.Find(Flowchart, "mermaid");

        Assert.NotNull(found);
        Assert.Equal(workspace.ArtifactFile("flow.1.workflow.standard.json"), found.SpecPath);
        Assert.Equal("workflow", found.ArchifyType);
        Assert.Equal("standard", found.Quality);
    }

    /// <summary>
    /// And a rendered one keeps its picture. The artifact of a standard-quality
    /// specification is <c>.standard.html</c>, so a reader looking for
    /// <c>.html</c> would find nothing and report a rendered diagram missing.
    /// </summary>
    [Fact]
    public void A_rendered_standard_quality_artifact_is_shown_like_any_other()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart);
        workspace.WriteArtifact("flow.md", Flowchart, "workflow", quality: "standard");

        using var artifacts = workspace.Artifacts();
        var found = artifacts.Find(Flowchart, "mermaid");

        Assert.NotNull(found);
        Assert.Equal(ArchifyWorkspace.ArtifactDocument, found.Html);
        Assert.Equal(workspace.ArtifactFile("flow.1.workflow.standard.html"), found.ArtifactPath);
        Assert.Equal(workspace.ArtifactFile("flow.1.workflow.standard.json"), found.SpecPath);
        Assert.Equal("standard", found.Quality);
        Assert.True(found.CanRender);
    }

    /// <summary>
    /// Showcase is what a filename that says nothing means, and that is the answer
    /// given rather than null: a diagram with a type has a profile it renders
    /// under, whether or not anybody wrote it down. Null is reserved for the case
    /// where no Archify type fits at all, because then there is nothing to render
    /// under any profile.
    /// </summary>
    [Fact]
    public void A_specification_that_names_no_quality_is_showcase_and_a_diagram_with_no_type_has_none()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart);
        workspace.WriteChapter("model.md", ClassDiagram);
        workspace.WriteSpecification("flow.1.workflow.json");

        using var artifacts = workspace.Artifacts();

        Assert.Equal("showcase", artifacts.Find(Flowchart, "mermaid")?.Quality);
        Assert.Null(artifacts.Find(ClassDiagram, "mermaid")?.Quality);
    }

    /// <summary>
    /// Two specifications for one diagram is an authoring mistake, and the
    /// generator says so and refuses. Here it must not: a knowledge pane that
    /// threw would fail to draw the whole chapter because of a stray file beside
    /// it. The alphabetically first wins, so the wrong answer is at least the
    /// same wrong answer on every machine and in every process.
    /// </summary>
    [Fact]
    public void Two_specifications_for_one_diagram_pick_the_first_by_name_rather_than_throwing()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart);
        workspace.WriteSpecification("flow.1.workflow.json");
        workspace.WriteSpecification("flow.1.architecture.json");

        using var artifacts = workspace.Artifacts();
        var found = artifacts.Find(Flowchart, "mermaid");

        Assert.NotNull(found);
        Assert.Equal("architecture", found.ArchifyType);
        Assert.Equal(workspace.ArtifactFile("flow.1.architecture.json"), found.SpecPath);
    }

    /// <summary>
    /// A_specification_authored_at_a_type_that_is_not_the_default is about what is
    /// on disk; this is about what the index says. An entry naming the type it was
    /// rendered as outranks whatever specification happens to be lying beside it,
    /// because the entry records what actually happened.
    /// </summary>
    [Fact]
    public void The_index_entrys_type_outranks_a_specification_that_disagrees_with_it()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart);
        workspace.WriteArtifact("flow.md", Flowchart, "workflow");

        // A leftover from an abandoned re-authoring, sorting ahead of the real one.
        workspace.WriteSpecification("flow.1.architecture.json");

        using var artifacts = workspace.Artifacts();
        var found = artifacts.Find(Flowchart, "mermaid");

        Assert.NotNull(found);
        Assert.Equal("workflow", found.ArchifyType);
        Assert.Equal(ArchifyWorkspace.ArtifactDocument, found.Html);
        Assert.Equal(workspace.ArtifactFile("flow.1.workflow.json"), found.SpecPath);
    }

    /// <summary>
    /// A diagram that belongs to no chapter has nowhere to put a specification, so
    /// answering anything but null would put an offer under a storybook sample
    /// that could never be honoured.
    /// </summary>
    [Fact]
    public void A_diagram_that_is_in_no_chapter_is_not_this_ports_business()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart);

        using var artifacts = workspace.Artifacts();

        Assert.Null(artifacts.Find("flowchart LR\n    Sample --> Storybook", "mermaid"));
    }

    /// <summary>
    /// A class diagram is a real chapter fence with a real place to put a
    /// specification, and still there is nothing to generate: none of Archify's
    /// five types can say "aggregate root" or "0..*". The type comes back null so
    /// the view offers nothing.
    /// </summary>
    [Fact]
    public void A_class_diagram_chapter_fence_has_no_Archify_type_to_offer()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("model.md", ClassDiagram);

        using var artifacts = workspace.Artifacts();
        var found = artifacts.Find(ClassDiagram, "mermaid");

        Assert.NotNull(found);
        Assert.Null(found.ArchifyType);
        Assert.Null(found.Html);
        Assert.Null(found.SpecPath);
        Assert.False(found.IsOutOfDate);
    }

    /// <summary>
    /// The C# fence scan and the generator's read the same chapters, and a Windows
    /// checkout hands them CRLF. If the two disagree about line endings the app
    /// silently shows mermaid everywhere, which is the one failure this design
    /// cannot detect from the inside.
    /// </summary>
    [Fact]
    public void A_fence_written_with_CRLF_is_still_found_and_still_matches_its_artifact()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart, newline: "\r\n");
        workspace.WriteArtifact("flow.md", Flowchart, "workflow");

        using var artifacts = workspace.Artifacts();

        // The markdown parser hands the view LF, whatever the file on disk holds.
        var found = artifacts.Find(Flowchart, "mermaid");

        Assert.NotNull(found);
        Assert.Equal(ArchifyWorkspace.ArtifactDocument, found.Html);
        Assert.False(found.IsOutOfDate);
    }

    [Fact]
    public void A_language_nothing_draws_as_a_diagram_is_never_looked_up()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart);
        workspace.WriteArtifact("flow.md", Flowchart, "workflow");

        using var artifacts = workspace.Artifacts();

        Assert.Null(artifacts.Find(Flowchart, "csharp"));
        Assert.Null(artifacts.Find(Flowchart, null));
    }

    /// <summary>
    /// <c>_archify/</c> sits beside the chapter, so in <c>.arc42/</c> all four
    /// chapters share one index — and a rendered diagram must keep its picture with
    /// a neighbour's diagram 1 filed right next to it. The entries are hash-keyed
    /// and coexist perfectly well; it was the staleness test that was blind.
    /// </summary>
    [Fact]
    public void A_current_artifact_is_not_displaced_by_another_chapters_diagram_of_the_same_number()
    {
        using var workspace = ArchifyWorkspace.CreateArc42();
        workspace.WriteChapter(RuntimeChapter, RuntimeFlow);
        workspace.WriteChapter(DeploymentChapter, DeploymentSequence);
        workspace.WriteArtifact(RuntimeChapter, RuntimeFlow, "workflow");
        workspace.WriteArtifact(DeploymentChapter, DeploymentSequence, "sequence");

        using var artifacts = workspace.Artifacts();

        var runtime = artifacts.Find(RuntimeFlow, "mermaid");
        Assert.NotNull(runtime);
        Assert.Equal(ArchifyWorkspace.ArtifactDocument, runtime.Html);
        Assert.Equal("workflow", runtime.ArchifyType);
        Assert.Equal(workspace.ArtifactFile("06-runtime-view.1.workflow.json"), runtime.SpecPath);
        Assert.False(runtime.IsOutOfDate);

        var deployment = artifacts.Find(DeploymentSequence, "mermaid");
        Assert.NotNull(deployment);
        Assert.Equal(ArchifyWorkspace.ArtifactDocument, deployment.Html);
        Assert.Equal("sequence", deployment.ArchifyType);
        Assert.False(deployment.IsOutOfDate);
    }

    /// <summary>
    /// The regression itself. Matching an index entry on the ordinal alone made one
    /// chapter's diagram 1 answer for another's: the runtime view had never been
    /// rendered, and the deployment view's entry beside it in the shared index was
    /// enough to report the runtime diagram stale — and to offer to re-render it
    /// against the deployment view's specification, under the deployment view's
    /// Archify type. Both halves of that wrong answer are pinned here.
    /// </summary>
    [Fact]
    public void A_diagram_nobody_has_rendered_is_not_called_stale_because_a_neighbour_has_a_diagram_one()
    {
        using var workspace = ArchifyWorkspace.CreateArc42();
        workspace.WriteChapter(RuntimeChapter, RuntimeFlow);
        workspace.WriteChapter(DeploymentChapter, DeploymentSequence);

        // Only the deployment chapter has ever been rendered.
        workspace.WriteArtifact(DeploymentChapter, DeploymentSequence, "sequence");

        using var artifacts = workspace.Artifacts();
        var found = artifacts.Find(RuntimeFlow, "mermaid");

        Assert.NotNull(found);
        Assert.False(found.IsOutOfDate);
        Assert.Null(found.Html);
        Assert.Null(found.SpecPath);

        // The type is the one this diagram's own source implies, not the one the
        // neighbour's entry records.
        Assert.Equal("workflow", found.ArchifyType);
    }

    /// <summary>
    /// The other half of the fix: attributing entries to chapters must not amount
    /// to switching staleness detection off. A chapter whose own entry names it and
    /// holds an older hash is still reported out of date, and still against its own
    /// specification rather than the neighbour's.
    /// </summary>
    [Fact]
    public void A_chapters_own_edited_fence_is_still_out_of_date_beside_a_neighbours_entry()
    {
        using var workspace = ArchifyWorkspace.CreateArc42();
        workspace.WriteChapter(DeploymentChapter, DeploymentSequence);

        // The neighbour is filed first, so a lookup that matched on the ordinal
        // alone would reach it before the entry that really belongs here.
        workspace.WriteArtifact(DeploymentChapter, DeploymentSequence, "sequence");

        // The runtime chapter was rendered from the fence as it stood, and edited after.
        workspace.WriteArtifact(RuntimeChapter, RuntimeFlow, "workflow");
        workspace.WriteChapter(RuntimeChapter, EditedRuntimeFlow);

        using var artifacts = workspace.Artifacts();
        var found = artifacts.Find(EditedRuntimeFlow, "mermaid");

        Assert.NotNull(found);
        Assert.True(found.IsOutOfDate);
        Assert.Null(found.Html);
        Assert.Equal("workflow", found.ArchifyType);
        Assert.Equal(workspace.ArtifactFile("06-runtime-view.1.workflow.json"), found.SpecPath);
    }

    /// <summary>
    /// An index written before the <c>chapter</c> field existed still answers
    /// correctly, because the chapter is recoverable from the specification's
    /// filename. Without that fallback, upgrading would have made every entry
    /// already on disk either match nothing or match everything.
    /// </summary>
    [Fact]
    public void An_entry_written_before_the_chapter_field_is_attributed_by_its_specification_filename()
    {
        using var workspace = ArchifyWorkspace.CreateArc42();
        workspace.WriteChapter(RuntimeChapter, EditedRuntimeFlow);
        workspace.WriteChapter(DeploymentChapter, DeploymentSequence);

        workspace.AddIndexEntry(
            DiagramSourceHash.Of(RuntimeFlow),
            ordinal: 1,
            type: "workflow",
            spec: "06-runtime-view.1.workflow.json",
            artifact: "06-runtime-view.1.workflow.html");

        using var artifacts = workspace.Artifacts();

        var runtime = artifacts.Find(EditedRuntimeFlow, "mermaid");
        Assert.NotNull(runtime);
        Assert.True(runtime.IsOutOfDate);
        Assert.Equal("workflow", runtime.ArchifyType);

        // And the chapter it does not name is left alone by it.
        var deployment = artifacts.Find(DeploymentSequence, "mermaid");
        Assert.NotNull(deployment);
        Assert.False(deployment.IsOutOfDate);
        Assert.Equal("sequence", deployment.ArchifyType);
    }

    /// <summary>
    /// An entry that names no chapter and carries no specification to recover one
    /// from is taken as this chapter's. That is the old behaviour kept on purpose:
    /// right in every folder that holds a single chapter, and the best guess
    /// available in the ones that do not — which is why the second assertion here
    /// is the documented cost of the fallback rather than a bug.
    /// </summary>
    [Fact]
    public void An_entry_that_names_no_chapter_at_all_is_taken_as_this_chapters()
    {
        using var workspace = ArchifyWorkspace.CreateArc42();
        workspace.WriteChapter(RuntimeChapter, EditedRuntimeFlow);
        workspace.WriteChapter(DeploymentChapter, DeploymentSequence);

        workspace.AddIndexEntry(DiagramSourceHash.Of(RuntimeFlow), ordinal: 1, type: "workflow");

        using var artifacts = workspace.Artifacts();

        var runtime = artifacts.Find(EditedRuntimeFlow, "mermaid");
        var deployment = artifacts.Find(DeploymentSequence, "mermaid");

        Assert.NotNull(runtime);
        Assert.NotNull(deployment);
        Assert.True(runtime.IsOutOfDate);
        Assert.True(deployment.IsOutOfDate);
    }

    /// <summary>
    /// The chapter scan is cached, because it answers once per diagram per render.
    /// A moved folder, a new clone or a switched flag all change the answer, so
    /// the signal that any of them happened throws the whole scan away rather
    /// than working out which chapters were affected.
    /// </summary>
    [Fact]
    public void A_folder_change_throws_the_cached_chapter_scan_away()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart);

        using var artifacts = workspace.Artifacts();
        Assert.NotNull(artifacts.Find(Flowchart, "mermaid"));

        workspace.WriteChapter("flow.md", EditedFlowchart);
        Assert.NotNull(artifacts.Find(Flowchart, "mermaid"));

        workspace.MoveFolders();

        Assert.Null(artifacts.Find(Flowchart, "mermaid"));
        Assert.NotNull(artifacts.Find(EditedFlowchart, "mermaid"));
    }

    /// <summary>
    /// The regression QA found. Nothing raises an event when a chapter file is
    /// saved — <c>IKnowledgeFolderSource.Changed</c> is about a folder moving —
    /// so the cached fence map answered for a file that had since been edited:
    /// the new fence hashed to a key the map had never heard of, Find returned
    /// null, and the reader got plain mermaid with no drift alert and no offer.
    /// Silently worse than the designed answer, and exactly the case hashing the
    /// source exists to catch.
    /// </summary>
    [Fact]
    public void A_chapter_edited_while_the_app_is_open_is_noticed_once_the_recheck_interval_has_passed()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart, written: Written);
        workspace.WriteArtifact("flow.md", Flowchart, "workflow");

        using var artifacts = workspace.Artifacts();
        Assert.Equal(ArchifyWorkspace.ArtifactDocument, artifacts.Find(Flowchart, "mermaid")?.Html);

        // Somebody edits the chapter in another window while the pane is open.
        workspace.WriteChapter("flow.md", EditedFlowchart, written: Written.AddMinutes(1));

        // The symptom, pinned: inside the interval the edited fence is unknown.
        Assert.Null(artifacts.Find(EditedFlowchart, "mermaid"));

        workspace.Advance(ArchifyDiagramArtifacts.RecheckAfter + TimeSpan.FromSeconds(1));

        var found = artifacts.Find(EditedFlowchart, "mermaid");

        Assert.NotNull(found);
        Assert.True(found.IsOutOfDate);
        Assert.Null(found.Html);
        Assert.Null(found.ArtifactPath);

        // And the offer that goes with being told: the specification beside the
        // chapter is still the right one to re-render from.
        Assert.Equal(workspace.ArtifactFile("flow.1.workflow.json"), found.SpecPath);
        Assert.Equal("workflow", found.ArchifyType);
    }

    /// <summary>
    /// A chapter that did not exist when the map was built. The file count moves
    /// as well as the newest timestamp, so either half of the signature would
    /// catch this one — what it pins is that no event is needed for it.
    /// </summary>
    [Fact]
    public void A_chapter_added_while_the_app_is_open_is_found_without_any_Changed_event()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart, written: Written);

        using var artifacts = workspace.Artifacts();
        Assert.NotNull(artifacts.Find(Flowchart, "mermaid"));
        Assert.Null(artifacts.Find(RuntimeFlow, "mermaid"));

        workspace.WriteChapter("runtime.md", RuntimeFlow, written: Written.AddMinutes(1));
        workspace.Advance(ArchifyDiagramArtifacts.RecheckAfter + TimeSpan.FromSeconds(1));

        var found = artifacts.Find(RuntimeFlow, "mermaid");

        Assert.NotNull(found);
        Assert.Equal("workflow", found.ArchifyType);

        // The chapter that was there all along is still there: a rebuild, not a
        // replacement of one answer by another.
        Assert.NotNull(artifacts.Find(Flowchart, "mermaid"));
    }

    /// <summary>
    /// The half of the signature the timestamp cannot carry. The deleted chapter
    /// is the older of the two, so the newest last-write-time is exactly what it
    /// was before — only the file count moved, and only counting the chapters
    /// notices that a fence has gone.
    /// </summary>
    [Fact]
    public void A_deleted_chapter_takes_its_fences_with_it_even_though_the_newest_chapter_is_untouched()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart, written: Written);
        workspace.WriteChapter("runtime.md", RuntimeFlow, written: Written.AddHours(1));

        using var artifacts = workspace.Artifacts();
        Assert.NotNull(artifacts.Find(Flowchart, "mermaid"));

        workspace.DeleteChapter("flow.md");
        workspace.Advance(ArchifyDiagramArtifacts.RecheckAfter + TimeSpan.FromSeconds(1));

        Assert.Null(artifacts.Find(Flowchart, "mermaid"));
        Assert.NotNull(artifacts.Find(RuntimeFlow, "mermaid"));
    }

    /// <summary>
    /// The other side of the interval, which is the whole reason there is one: a
    /// pane full of diagrams asks once per diagram, and re-reading every chapter
    /// each time is what the cache exists to avoid. Asserted from outside — the
    /// fence that is no longer in the file is still answered and the one that now
    /// is is not, which is only possible if nothing was re-read.
    /// </summary>
    [Fact]
    public void A_lookup_inside_the_recheck_interval_answers_from_the_cache_rather_than_re_reading_the_chapters()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart, written: Written);

        using var artifacts = workspace.Artifacts();
        Assert.NotNull(artifacts.Find(Flowchart, "mermaid"));

        workspace.WriteChapter("flow.md", EditedFlowchart, written: Written.AddMinutes(1));

        // Exactly the interval and not a tick past it, so the boundary is pinned
        // too: the map is trusted for the whole of it.
        workspace.Advance(ArchifyDiagramArtifacts.RecheckAfter);

        Assert.NotNull(artifacts.Find(Flowchart, "mermaid"));
        Assert.Null(artifacts.Find(EditedFlowchart, "mermaid"));
    }

    /// <summary>
    /// The brief is all the agent gets, so it has to name the four things it
    /// cannot work out on its own — which fence, where the specification goes,
    /// which type, and which instructions to follow — and hand over the canonical
    /// text rather than a paraphrase of it.
    /// </summary>
    [Fact]
    public async Task Authoring_hands_the_agent_the_chapter_the_specification_path_and_the_fence()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("flow.md", Flowchart);

        using var artifacts = workspace.Artifacts();
        var error = await artifacts.AuthorAsync(Flowchart);

        Assert.Null(error);
        var request = Assert.Single(workspace.Launcher.Requests);
        Assert.Equal(workspace.RootPath, request.WorkingDirectory);
        Assert.Contains(".domain/orders/flow.md", request.Prompt, StringComparison.Ordinal);
        Assert.Contains(".domain/orders/_archify/flow.1.workflow.json", request.Prompt, StringComparison.Ordinal);
        Assert.Contains(Flowchart, request.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_diagram_with_no_Archify_type_is_never_sent_to_an_agent()
    {
        using var workspace = ArchifyWorkspace.Create();
        workspace.WriteChapter("model.md", ClassDiagram);

        using var artifacts = workspace.Artifacts();
        var error = await artifacts.AuthorAsync(ClassDiagram);

        Assert.Equal("No Archify diagram type fits this kind of diagram.", error);
        Assert.Empty(workspace.Launcher.Requests);
    }
}

/// <summary>
/// A repository clone with one knowledge folder in it, built the way the
/// generator would leave it: chapters holding mermaid fences, an <c>_archify/</c>
/// folder beside them, and an <c>index.json</c> filing each artifact under the
/// hash of the fence it was authored from.
/// </summary>
file sealed class ArchifyWorkspace : IDisposable
{
    internal const string ArtifactDocument = "<!doctype html><html><body>Archify</body></html>";

    private readonly string _root;
    private readonly FakeAppFeatureSettings _features;
    private readonly FakeKnowledgeFolderSource _folders;
    private readonly GitHubSettingsStore _repositories;

    /// <summary>The index as it is being built up, in insertion order, so a test
    /// can put two chapters' entries in one file the way one shared folder
    /// does.</summary>
    private readonly Dictionary<string, object> _entries = new(StringComparer.Ordinal);

    /// <summary>A clock that only moves when a test moves it. The adapter re-stats
    /// the chapters at most once per interval, so a frozen clock is what lets a
    /// test stand either side of that boundary without waiting out two real
    /// seconds — and what makes "inside the interval" mean it rather than mean
    /// "however long this machine took".</summary>
    private readonly MovableClock _clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    private ArchifyWorkspace(string root, string chapterFolder, bool archifyDiagrams, bool copilotCli)
    {
        _root = root;
        ChapterDirectory = Path.Combine(root, chapterFolder);
        ArtifactDirectory = Path.Combine(ChapterDirectory, "_archify");
        Directory.CreateDirectory(ArtifactDirectory);

        _features = new FakeAppFeatureSettings(archifyDiagrams, copilotCli);
        _folders = new FakeKnowledgeFolderSource(root);

        // A store pointed at an empty temp file, so the only scope the adapter can
        // resolve through is the unscoped one the folder source answers.
        _repositories = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        Launcher = new RecordingCopilotCliLauncher();
    }

    internal string RootPath => _root;

    internal string ChapterDirectory { get; }

    internal string ArtifactDirectory { get; }

    internal RecordingCopilotCliLauncher Launcher { get; }

    /// <summary>The <c>.domain</c> shape: a folder per bounded context, so the
    /// <c>_archify/</c> beside it holds one chapter's entries and nothing
    /// else.</summary>
    internal static ArchifyWorkspace Create(bool archifyDiagrams = true, bool copilotCli = true) =>
        new(NewRoot(), Path.Combine(".domain", "orders"), archifyDiagrams, copilotCli);

    /// <summary>The <c>.arc42</c> shape, which is where matching on the ordinal
    /// alone came unstuck: every chapter sits directly in the folder, so all four
    /// share one <c>_archify/index.json</c> and one chapter's diagram 1 sits beside
    /// another chapter's diagram 1.</summary>
    internal static ArchifyWorkspace CreateArc42(bool archifyDiagrams = true, bool copilotCli = true) =>
        new(NewRoot(), ".arc42", archifyDiagrams, copilotCli);

    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "backlog-archify-artifact-tests", Guid.NewGuid().ToString("n"));

    internal ArchifyDiagramArtifacts Artifacts() =>
        new(_features, _folders, _repositories, Launcher, _clock);

    internal string ArtifactFile(string name) => Path.Combine(ArtifactDirectory, name);

    /// <summary>Moves the clock the adapter reads, which is the only thing that
    /// lets it re-stat the chapters again.</summary>
    internal void Advance(TimeSpan elapsed) => _clock.Advance(elapsed);

    /// <summary>What a repointed clone or a moved knowledge folder raises.</summary>
    internal void MoveFolders() => _folders.Move();

    /// <summary>Writes a chapter whose mermaid fences are the sources given, in
    /// order, with the line ending a checkout would have left behind. A test that
    /// cares when the chapter was written says so, because the rebuild signature
    /// reads that off disk.</summary>
    internal void WriteChapter(string name, string source, string newline = "\n", DateTime? written = null)
    {
        var text = new StringBuilder()
            .Append("# Orders\n\nHow an order moves.\n\n")
            .Append("```mermaid\n")
            .Append(source)
            .Append("\n```\n")
            .ToString();

        var file = Path.Combine(ChapterDirectory, name);
        File.WriteAllText(file, text.Replace("\n", newline));

        if (written is { } stamp) File.SetLastWriteTimeUtc(file, stamp);
    }

    internal void DeleteChapter(string name) => File.Delete(Path.Combine(ChapterDirectory, name));

    /// <summary>The state the generator leaves behind for one rendered diagram: a
    /// specification, a document, and an index entry filed under the hash of the
    /// fence it was authored from and naming the chapter it belongs to. The stem is
    /// built the way the adapter builds it, so the fixture cannot disagree with
    /// production about what a specification beside a chapter is called.</summary>
    internal void WriteArtifact(string chapterFile, string authoredFrom, string type, int ordinal = 1, string quality = "showcase")
    {
        var stem = Stem(chapterFile, type, ordinal, quality);

        WriteSpecification($"{stem}.json");
        File.WriteAllText(ArtifactFile($"{stem}.html"), ArtifactDocument);
        AddIndexEntry(DiagramSourceHash.Of(authoredFrom), ordinal, type, chapterFile, $"{stem}.json", $"{stem}.html", quality);
    }

    internal void WriteSpecification(string name) =>
        File.WriteAllText(ArtifactFile(name), """{"type":"workflow","title":"Orders"}""");

    /// <summary>The stem the generator builds a specification and its artifact
    /// from. <c>showcase</c> is the default and is never written into a name, so
    /// there is exactly one spelling of the ordinary case.</summary>
    internal static string Stem(string chapterFile, string type, int ordinal = 1, string quality = "showcase") =>
        $"{Path.GetFileNameWithoutExtension(chapterFile)}.{ordinal}.{type}{(quality == "standard" ? ".standard" : string.Empty)}";

    /// <summary>
    /// One index entry, with each optional field written only when it is given.
    /// <para>
    /// Absent rather than null on purpose: an index written before the
    /// <c>chapter</c> field existed simply has no such property, and a fixture that
    /// wrote an explicit null would be pinning a file the generator has never
    /// produced.
    /// </para>
    /// </summary>
    internal void AddIndexEntry(
        string hash,
        int ordinal,
        string type,
        string? chapter = null,
        string? spec = null,
        string? artifact = null,
        string? quality = null)
    {
        var entry = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["ordinal"] = ordinal,
            ["type"] = type,
            ["kind"] = "flowchart"
        };

        if (quality is not null) entry["quality"] = quality;
        if (chapter is not null) entry["chapter"] = chapter;
        if (spec is not null) entry["spec"] = spec;
        if (artifact is not null) entry["artifact"] = artifact;

        _entries[hash] = entry;

        File.WriteAllText(
            Path.Combine(ArtifactDirectory, "index.json"),
            JsonSerializer.Serialize(new { schemaVersion = 1, entries = _entries }));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>The two switches the adapter asks about, and nothing else.</summary>
file sealed class FakeAppFeatureSettings(bool archifyDiagrams, bool copilotCli) : IAppFeatureSettings
{
    private readonly Dictionary<string, bool> _switches = new(StringComparer.OrdinalIgnoreCase)
    {
        [KnowledgeFeatures.ArchifyDiagrams] = archifyDiagrams,
        [AppFeatureKeys.CopilotCli] = copilotCli
    };

    public event Action? Changed;

    public AppFeatureSettings Current => new()
    {
        EnabledFeatures = new HashSet<string>(
            _switches.Where(entry => entry.Value).Select(entry => entry.Key),
            StringComparer.OrdinalIgnoreCase)
    };

    public string SettingsPath => "(not persisted)";

    public bool IsEnabled(string key) => _switches.TryGetValue(key, out var enabled) && enabled;

    public string? SetEnabled(string key, bool enabled)
    {
        _switches[key] = enabled;
        Changed?.Invoke();
        return null;
    }
}

/// <summary>
/// A clone with one knowledge folder in it. The adapter asks for four —
/// <c>.domain</c>, <c>.arc42</c>, <c>.tech</c> and <c>.design</c> — and a folder
/// that is not in this fixture answers the way a folder nobody configured does.
/// </summary>
file sealed class FakeKnowledgeFolderSource(string root) : IKnowledgeFolderSource
{
    public event Action? Changed;

    public string StorageDirectory => root;

    public IReadOnlyList<KnowledgeFolderSetting> Folders(string? repositoryAlias) => [];

    public KnowledgeFolderLocation Resolve(string key, string? repositoryAlias = null)
    {
        var full = Path.Combine(root, key);

        return Directory.Exists(full)
            ? new KnowledgeFolderLocation(key, true, null, null, null, full, root)
            : KnowledgeFolderLocation.Unavailable(key, $"{key} is not configured here.");
    }

    public void NotifyContentChanged() => Changed?.Invoke();

    /// <summary>What a moved folder or a repointed clone raises. The adapter
    /// subscribes to it to throw its chapter scan away.</summary>
    internal void Move() => Changed?.Invoke();
}

/// <summary>A clock that stands still until a test moves it, so the interval the
/// fence map is trusted for has a boundary a test can stand either side of.</summary>
file sealed class MovableClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    internal void Advance(TimeSpan elapsed) => _now += elapsed;
}

/// <summary>An agent CLI that records the brief instead of starting
/// anything.</summary>
file sealed class RecordingCopilotCliLauncher : ICopilotCliLauncher
{
    private readonly List<CopilotCliRequest> _requests = [];

    internal IReadOnlyList<CopilotCliRequest> Requests => _requests;

    public Task LaunchAsync(CopilotCliRequest request, CancellationToken cancellationToken = default)
    {
        _requests.Add(request);
        return Task.CompletedTask;
    }
}
