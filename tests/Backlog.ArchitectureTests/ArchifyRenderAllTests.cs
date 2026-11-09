using System.Diagnostics;
using System.Text.Json;

namespace Backlog.ArchitectureTests;

/// <summary>
/// What <c>render --all</c> in <c>tools/diagrams/archify-artifacts.mjs</c> does once
/// a chapter's fences have moved.
///
/// <para>It used to build each specification's name from the index entry that last
/// held the ordinal, so a diagram whose specification had been renamed to follow its
/// fence came back as ENOENT or as a type mismatch, naming a file nobody had. The
/// answer is what is on disk: render it, refuse it, or skip it and say why.</para>
///
/// <para>Proved on a throwaway repository holding the script and a stub of the
/// vendored generator, because the devbook's own state changes with every chapter
/// edit and the picture is not what is under test. Skipped where Node is absent.</para>
/// </summary>
public sealed class ArchifyRenderAllTests : IDisposable
{
    private const string Sequence = "sequenceDiagram\n  A->>B: hello";

    private const string Flow = "flowchart LR\n  A --> B";

    private const string EditedFlow = "flowchart LR\n  A --> C";

    private const string OtherFlow = "flowchart LR\n  X --> Y";

    /// <summary>Stands in for <c>archify.mjs deliver</c>: writes the artifact and
    /// answers with a clean receipt.</summary>
    private const string GeneratorStub = """
        import { writeFileSync } from 'node:fs';
        writeFileSync(process.argv[5], '');
        console.log(JSON.stringify({ ok: true, artifact: { bytes: 0 }, validation: { errors: 0, warnings: 0, checksPassed: 9, checkCount: 9 } }));
        """;

    private readonly string _root;
    private readonly string _script;
    private readonly string _chapters;
    private readonly string _artifacts;

    public ArchifyRenderAllTests()
    {
        _root = Directory.CreateTempSubdirectory("archify-all-").FullName;
        _script = Path.Combine(_root, "tools", "diagrams", "archify-artifacts.mjs");
        Directory.CreateDirectory(Path.GetDirectoryName(_script)!);
        File.Copy(RepositoryRoot.File("tools", "diagrams", "archify-artifacts.mjs"), _script);

        var generator = Path.Combine(_root, "tools", "archify", "bin", "archify.mjs");
        Directory.CreateDirectory(Path.GetDirectoryName(generator)!);
        File.WriteAllText(generator, GeneratorStub);

        _chapters = Path.Combine(_root, ".devbook", "arc42");
        _artifacts = Path.Combine(_chapters, "_archify");
        Directory.CreateDirectory(_artifacts);
    }

    [Fact]
    public void A_specification_renamed_to_its_new_ordinal_is_found_on_disk_rather_than_named_from_the_old_entry()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        // Diagram 2 is a sequence, then the sequence moves to 3 and a re-authored
        // flowchart takes its place. The old entry at 2 says `sequence`, and
        // chapter.2.sequence.json is exactly the file that no longer exists.
        WriteChapter(OtherFlow, Sequence);
        WriteSpec("chapter.1.workflow.json");
        WriteSpec("chapter.2.sequence.json");
        Assert.Equal(0, RenderAll().Exit);

        WriteChapter(OtherFlow, Flow, Sequence);
        File.Move(Spec("chapter.2.sequence.json"), Spec("chapter.3.sequence.json"));
        WriteSpec("chapter.2.workflow.json", "authored from the new fence");

        var (exit, results) = RenderAll();

        Assert.True(exit == 0, results);
        Assert.Contains("chapter.2.workflow.json", results, StringComparison.Ordinal);
        Assert.Contains("chapter.3.sequence.json", results, StringComparison.Ordinal);
        Assert.DoesNotContain("ENOENT", results, StringComparison.Ordinal);
        Assert.Equal(["chapter.1.workflow.json", "chapter.2.workflow.json", "chapter.3.sequence.json"], IndexedSpecs());
        Assert.Equal(0, Verify().Exit);
    }

    [Fact]
    public void A_specification_of_a_type_the_fence_cannot_have_is_skipped_with_the_reason()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        WriteChapter(Sequence);
        WriteSpec("chapter.1.sequence.json");
        Assert.Equal(0, RenderAll().Exit);
        var before = Index();

        // The fence at this ordinal is now a flowchart, and the specification still
        // there is the old sequence: nothing to render, and nothing failed.
        WriteChapter(Flow);

        var (exit, results) = RenderAll();

        Assert.True(exit == 0, results);
        var skip = SkippedResult(results, ordinal: 1);
        Assert.Contains("chapter.1.sequence.json", skip, StringComparison.Ordinal);
        Assert.Contains("flowchart", skip, StringComparison.Ordinal);
        Assert.Equal(before, Index());

        // A skip leaves the work outstanding, and the gate still says so.
        Assert.NotEqual(0, Verify().Exit);
    }

    [Fact]
    public void A_fence_with_no_specification_is_skipped_and_one_recorded_elsewhere_names_the_file_to_rename()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        WriteChapter(Flow);
        WriteSpec("chapter.1.workflow.json");
        Assert.Equal(0, RenderAll().Exit);

        // A new diagram above it, and the specification not renamed yet. The new
        // fence takes the old specification's ordinal, so the guard refuses that
        // one; the moved fence has nothing at its ordinal and is told what to do.
        WriteChapter(OtherFlow, Flow);

        var (exit, results) = RenderAll();

        Assert.True(exit != 0, results);
        Assert.Contains("chapter.1.workflow.json", SkippedResult(results, ordinal: 2), StringComparison.Ordinal);
        Assert.Equal(["chapter.1.workflow.json"], IndexedSpecs());
    }

    [Fact]
    public void A_renamed_specification_left_unrendered_is_picked_up_and_its_entry_refiled()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        WriteChapter(Flow, Sequence);
        WriteSpec("chapter.1.workflow.json");
        WriteSpec("chapter.2.sequence.json");
        Assert.Equal(0, RenderAll().Exit);

        // The first diagram is gone, so the sequence is now diagram 1. Its entry
        // still names the old file, which is not the file the app would draw from.
        WriteChapter(Sequence);
        File.Delete(Spec("chapter.1.workflow.json"));
        File.Move(Spec("chapter.2.sequence.json"), Spec("chapter.1.sequence.json"));

        Assert.Contains("\"unrendered\"", Verify().Output, StringComparison.Ordinal);

        var (exit, results) = RenderAll();

        Assert.True(exit == 0, results);
        Assert.Equal(["chapter.1.sequence.json"], IndexedSpecs());
        Assert.Equal(0, Verify().Exit);
    }

    [Fact]
    public void A_neighbours_specification_left_at_a_moved_fences_ordinal_reads_as_stale_and_is_refused()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        WriteChapter(Flow, OtherFlow);
        WriteSpec("chapter.1.workflow.json");
        WriteSpec("chapter.2.workflow.json");
        Assert.Equal(0, RenderAll().Exit);
        var before = Index();

        // The two swap, and neither specification follows. Each fence has its entry
        // and a file at its ordinal, which is the other diagram's picture.
        WriteChapter(OtherFlow, Flow);

        Assert.Contains("\"stale\"", Verify().Output, StringComparison.Ordinal);

        var (exit, results) = RenderAll();

        Assert.True(exit != 0, results);
        Assert.Contains("The fences have moved", results, StringComparison.Ordinal);
        Assert.Equal(before, Index());
    }

    [Fact]
    public void A_neighbours_specification_renamed_onto_an_unmoved_fence_is_refused()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        WriteChapter(Flow, OtherFlow);
        WriteSpec("chapter.1.workflow.json");
        WriteSpec("chapter.2.lifecycle.json");
        Assert.Equal(0, RenderAll().Exit);
        var before = Index();

        // Neither fence changed: the file authored from the second now claims the first.
        File.Delete(Spec("chapter.1.workflow.json"));
        File.Move(Spec("chapter.2.lifecycle.json"), Spec("chapter.1.lifecycle.json"));

        var (exit, results) = RenderAll();

        Assert.True(exit != 0, results);
        Assert.Contains("is not the fence", results, StringComparison.Ordinal);
        Assert.Equal(before, Index());
        Assert.NotEqual(0, Verify().Exit);
    }

    [Fact]
    public void A_fence_edited_and_moved_with_its_specification_renamed_unchanged_stays_refused_on_every_run()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        WriteChapter(Flow, OtherFlow);
        WriteSpec("chapter.1.workflow.json");
        WriteSpec("chapter.2.workflow.json");
        Assert.Equal(0, RenderAll().Exit);

        // The first diagram is removed, and the second is edited and moves up.
        WriteChapter(EditedFlow);
        File.Delete(Spec("chapter.1.workflow.json"));
        File.Move(Spec("chapter.2.workflow.json"), Spec("chapter.1.workflow.json"));

        // Twice: the first run must not clear what the second run's refusal rests on.
        foreach (var run in new[] { 1, 2 })
        {
            var (exit, results) = RenderAll();
            Assert.True(exit != 0, $"run {run}:\n{results}");
            Assert.Contains("is not the fence", results, StringComparison.Ordinal);
        }

        Assert.NotEqual(0, Verify().Exit);

        // Re-authoring is the way out, and it leaves no leftover behind.
        WriteSpec("chapter.1.workflow.json", "re-authored");
        Assert.Equal(0, RenderAll().Exit);
        Assert.Equal(["chapter.1.workflow.json"], IndexedSpecs());
        Assert.Equal(0, Verify().Exit);
    }

    [Fact]
    public void Two_fences_swapped_with_one_edited_refuse_the_unchanged_specification_of_the_edited_one()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        WriteChapter(Flow, OtherFlow);
        WriteSpec("chapter.1.workflow.json");
        WriteSpec("chapter.2.lifecycle.json");
        Assert.Equal(0, RenderAll().Exit);

        WriteChapter(OtherFlow, EditedFlow);
        File.Move(Spec("chapter.1.workflow.json"), Spec("chapter.2.workflow.json"));
        File.Move(Spec("chapter.2.lifecycle.json"), Spec("chapter.1.lifecycle.json"));

        var (exit, results) = RenderAll();

        Assert.True(exit != 0, results);
        Assert.Contains("chapter.2.workflow.json", results, StringComparison.Ordinal);
        Assert.Contains("is not the fence", results, StringComparison.Ordinal);
        Assert.NotEqual(0, Verify().Exit);

        // Rendering by path, one at a time, must not lose the same record either.
        var (byPath, output) = Node([_script, "render", Spec("chapter.2.workflow.json"), "--json"]);
        Assert.True(byPath != 0, output);
    }

    [Fact]
    public void Identical_fences_authored_at_different_types_are_an_error_rather_than_rendered()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        WriteChapter(Flow, Flow);
        WriteSpec("chapter.1.workflow.json");
        Assert.Equal(0, RenderAll().Exit);

        WriteSpec("chapter.2.lifecycle.json");

        var (verifyExit, verify) = Verify();
        Assert.True(verifyExit != 0, verify);
        Assert.Contains("\"error\"", verify, StringComparison.Ordinal);
        Assert.Contains("contradicts", verify, StringComparison.Ordinal);

        var (byPath, output) = Node([_script, "render", Spec("chapter.2.lifecycle.json"), "--json"]);
        Assert.True(byPath != 0, output);
    }

    [Fact]
    public void A_fence_identical_to_one_in_a_neighbouring_chapter_keeps_the_entry_when_its_own_is_removed()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        // Sorted ahead of chapter.md, so chapter.md renders last and holds the entry.
        WriteMarkdown("abc.md", [Flow]);
        WriteChapter(Sequence, Flow);
        WriteSpec("abc.1.workflow.json");
        WriteSpec("chapter.1.sequence.json");
        WriteSpec("chapter.2.workflow.json");
        Assert.Equal(0, RenderAll().Exit);
        Assert.Equal(0, Verify().Exit);

        // The chapter holding the entry loses the fence; the neighbour still draws it.
        WriteChapter(Sequence);
        File.Delete(Spec("chapter.2.workflow.json"));

        var (exit, output) = Node([_script, "render", Spec("chapter.1.sequence.json"), "--json"]);

        Assert.True(exit == 0, output);
        Assert.Contains("chapter.2.workflow.json", IndexedSpecs());
        Assert.Equal(0, Verify().Exit);
    }

    [Fact]
    public void A_dead_entry_whose_specification_is_still_on_disk_is_reported_until_the_file_goes()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        WriteChapter(Flow, Sequence);
        WriteSpec("chapter.1.workflow.json");
        WriteSpec("chapter.2.sequence.json");
        Assert.Equal(0, RenderAll().Exit);

        WriteChapter(Flow);

        var (exit, results) = RenderAll();

        Assert.True(exit == 0, results);
        Assert.Contains("chapter.2.sequence.json", SkippedResult(results, ordinal: 2), StringComparison.Ordinal);
        Assert.Contains("chapter.2.sequence.json", IndexedSpecs());

        File.Delete(Spec("chapter.2.sequence.json"));

        var (cleared, afterDelete) = RenderAll();

        Assert.True(cleared == 0, afterDelete);
        Assert.Contains("\"cleared\"", afterDelete, StringComparison.Ordinal);
        Assert.Equal(["chapter.1.workflow.json"], IndexedSpecs());
    }

    [Fact]
    public void An_unedited_duplicate_fence_stays_rendered_at_both_ordinals_rather_than_being_refiled_forever()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        // One hash, so one entry, and it can only name one of the two.
        WriteChapter(Flow, Flow);
        WriteSpec("chapter.1.workflow.json");
        WriteSpec("chapter.2.workflow.json");
        Assert.Equal(0, RenderAll().Exit);

        Assert.Equal(0, Verify().Exit);
        Assert.Equal("[]", Compact(RenderAll().Results));
    }

    [Fact]
    public void A_dead_entry_for_an_ordinal_no_longer_in_the_chapter_is_cleared_even_with_nothing_to_render()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        WriteChapter(Flow, Sequence);
        WriteSpec("chapter.1.workflow.json");
        WriteSpec("chapter.2.sequence.json");
        WriteOtherChapter(OtherFlow);
        WriteSpec("other.1.workflow.json");
        Assert.Equal(0, RenderAll().Exit);

        // Diagram 2 is removed. Nothing renders at its ordinal again, so nothing
        // that only clears the ordinal it writes will ever reach this entry.
        WriteChapter(Flow);
        File.Delete(Spec("chapter.2.sequence.json"));

        var (exit, results) = RenderAll();

        Assert.True(exit == 0, results);
        Assert.Equal(["chapter.1.workflow.json", "other.1.workflow.json"], IndexedSpecs());
    }

    [Fact]
    public void Rendering_one_specification_by_path_clears_a_dead_entry_too()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        WriteChapter(Flow, Sequence);
        WriteSpec("chapter.1.workflow.json");
        WriteSpec("chapter.2.sequence.json");
        Assert.Equal(0, RenderAll().Exit);

        WriteChapter(Flow);
        File.Delete(Spec("chapter.2.sequence.json"));

        var (exit, output) = Node([_script, "render", Spec("chapter.1.workflow.json"), "--json"]);

        Assert.True(exit == 0, output);
        Assert.Equal(["chapter.1.workflow.json"], IndexedSpecs());
    }

    [Fact]
    public void An_entry_that_still_marks_a_stale_diagram_survives_the_sweep()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        WriteChapter(Flow);
        WriteSpec("chapter.1.workflow.json");
        Assert.Equal(0, RenderAll().Exit);

        // Its hash is gone from the chapter, but it is the only record that this
        // diagram's artifact is out of date. The guard refuses the unchanged
        // specification, and the diagram has to go on reading as stale.
        WriteChapter(EditedFlow);

        var (exit, results) = RenderAll();

        Assert.True(exit != 0, results);
        Assert.Contains("has changed since", results, StringComparison.Ordinal);
        Assert.Equal(["chapter.1.workflow.json"], IndexedSpecs());

        var (_, verify) = Verify();
        Assert.Contains("\"stale\"", verify, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder must not replace the assertion that failed.
        }
    }

    private void WriteChapter(params string[] fences) => WriteMarkdown("chapter.md", fences);

    /// <summary>A second chapter in the same folder, sharing the one index.</summary>
    private void WriteOtherChapter(params string[] fences) => WriteMarkdown("other.md", fences);

    private void WriteMarkdown(string name, string[] fences) =>
        File.WriteAllText(
            Path.Combine(_chapters, name),
            "# Chapter\n\n" + string.Concat(fences.Select(fence => $"```mermaid\n{fence}\n```\n\n")));

    private void WriteSpec(string name, string body = "first draft") =>
        File.WriteAllText(Spec(name), $"{{\"body\":\"{body} of {name}\"}}\n");

    private string Spec(string name) => Path.Combine(_artifacts, name);

    private string Index() => File.ReadAllText(Spec("index.json"));

    private List<string> IndexedSpecs()
    {
        using var index = JsonDocument.Parse(Index());

        return [.. index.RootElement.GetProperty("entries").EnumerateObject()
            .Select(entry => entry.Value.GetProperty("spec").GetString()!)
            .Order(StringComparer.Ordinal)];
    }

    private (int Exit, string Results) RenderAll() => Node([_script, "render", "--all", "--json"]);

    private (int Exit, string Output) Verify() => Node([_script, "verify", "--json"]);

    /// <summary>The skip <c>render --all --json</c> reported for this chapter's
    /// diagram, as its reason.</summary>
    private static string SkippedResult(string results, int ordinal)
    {
        using var parsed = JsonDocument.Parse(results);

        var skip = parsed.RootElement.EnumerateArray().FirstOrDefault(result =>
            result.TryGetProperty("skipped", out var skipped) && skipped.GetBoolean()
            && result.GetProperty("ordinal").GetInt32() == ordinal);

        Assert.True(skip.ValueKind == JsonValueKind.Object, $"diagram {ordinal} was not skipped:\n{results}");

        return skip.GetProperty("reason").GetString()!;
    }

    private static string Compact(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(parsed.RootElement);
    }

    private (int Exit, string Output) Node(string[] arguments)
    {
        var startInfo = new ProcessStartInfo("node")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;

        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var stderr = error.GetAwaiter().GetResult();

        return (process.ExitCode, output.Length > 0 ? output : stderr);
    }

    private static bool NodeInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("node", "--version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });

            return process is not null && process.WaitForExit(10_000) && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
