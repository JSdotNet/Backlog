using System.Diagnostics;
using System.Text.Json;
using Backlog.Modules.Sessions.UI.Adapters;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A delivery run's stages as the Archify specification the fold's artifact is
/// generated from, and the generator run over it. The specification is pinned for its
/// shape — one line of stages, the tone as the colour, the legend saying what the
/// colours mean — and the generator is run for real where Node is installed, because
/// Archify validates layout, and a specification that is well-formed JSON can still be
/// refused for a label wider than its box.
/// </summary>
public sealed class DeliveryRunDiagramSpecTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_stage_is_a_component_on_one_line_in_run_order()
    {
        using var spec = JsonDocument.Parse(DeliveryRunDiagramSpec.Archify(Eleven()));
        var root = spec.RootElement;

        Assert.Equal("architecture", root.GetProperty("diagram_type").GetString());
        Assert.Equal("trace", root.GetProperty("meta").GetProperty("animation").GetString());

        var components = root.GetProperty("components").EnumerateArray().ToList();
        Assert.Equal(11, components.Count);
        Assert.Equal("Scope Discovery", components[0].GetProperty("label").GetString());
        Assert.Equal("Summary", components[^1].GetProperty("label").GetString());

        // One row: the same y everywhere, x strictly rising.
        var positions = components.Select(component => component.GetProperty("pos")).ToList();
        Assert.Single(positions.Select(pos => pos[1].GetInt32()).Distinct());
        Assert.True(positions.Zip(positions.Skip(1)).All(pair => pair.Second[0].GetInt32() > pair.First[0].GetInt32()));

        // And the viewBox is wide enough to hold the last of them.
        var last = components[^1];
        Assert.True(root.GetProperty("meta").GetProperty("viewBox")[0].GetInt32()
            >= last.GetProperty("pos")[0].GetInt32() + last.GetProperty("size")[0].GetInt32());

        // Stages chained left to right.
        var connections = root.GetProperty("connections").EnumerateArray().ToList();
        Assert.Equal(10, connections.Count);
        Assert.Equal(("s0", "s1"), (connections[0].GetProperty("from").GetString(), connections[0].GetProperty("to").GetString()));
    }

    [Fact]
    public void The_tone_is_the_colour_and_the_word_and_the_legend_names_it()
    {
        using var spec = JsonDocument.Parse(DeliveryRunDiagramSpec.Archify(Eleven()));
        var components = spec.RootElement.GetProperty("components").EnumerateArray().ToList();

        Assert.Equal("backend", components[0].GetProperty("type").GetString());
        Assert.Equal("frontend", components[6].GetProperty("type").GetString());
        Assert.Equal("security", components[7].GetProperty("type").GetString());
        Assert.Equal("external", components[8].GetProperty("type").GetString());

        // Never colour alone: the word is in the box.
        Assert.Equal("Done · main session", components[0].GetProperty("sublabel").GetString());
        Assert.Equal("Done · qa:qa +1", components[5].GetProperty("sublabel").GetString());
        Assert.Equal("In progress · main session", components[6].GetProperty("sublabel").GetString());

        // A stage nobody reached names nobody.
        Assert.Equal("Pending", components[8].GetProperty("sublabel").GetString());

        var legend = spec.RootElement.GetProperty("meta").GetProperty("legend").GetProperty("entries");
        Assert.Equal("Done", legend.GetProperty("backend").GetProperty("label").GetString());
        Assert.Equal("In progress", legend.GetProperty("frontend").GetProperty("label").GetString());
    }

    [Fact]
    public void The_same_run_gives_the_same_specification_and_a_moved_stage_a_different_one()
    {
        var run = Eleven();

        Assert.Equal(DeliveryRunDiagramSpec.Archify(run), DeliveryRunDiagramSpec.Archify(run with { UpdatedAt = Noon.AddHours(1) }));

        var moved = run with { Stages = [.. run.Stages.Select((stage, index) => index == 6 ? stage with { Status = "done" } : stage)] };

        Assert.NotEqual(DeliveryRunDiagramSpec.Archify(run), DeliveryRunDiagramSpec.Archify(moved));
    }

    [Fact]
    public void The_mermaid_side_draws_the_same_stages_left_to_right()
    {
        var mermaid = DeliveryRunDiagramSpec.Mermaid(Eleven());

        Assert.StartsWith("flowchart LR\n    s0[\"Scope Discovery<br/>Done\"]", mermaid, StringComparison.Ordinal);
        Assert.Contains("s9 --> s10[\"Summary<br/>Pending\"]", mermaid, StringComparison.Ordinal);
    }

    /// <summary>
    /// The specification the fold sends is one Archify accepts, and what comes back
    /// is a whole artifact, cached by the specification's hash. Run against the
    /// vendored generator where Node is installed; skipped where it is not, because a
    /// machine without Node is the case the line's fallback exists for.
    /// </summary>
    [Fact]
    public async Task The_generator_accepts_the_specification_and_caches_the_artifact()
    {
        var generator = ArchifyDeliveryRunDiagrams.Locate(AppContext.BaseDirectory);
        Assert.SkipWhen(generator is null, "tools/archify is not reachable from the test output.");
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        var cache = Path.Combine(Path.GetTempPath(), "backlog-run-diagram-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var renderer = new ArchifyDeliveryRunDiagrams(generator, cache, TimeSpan.FromSeconds(60));
            var run = Eleven() with
            {
                Stages = [.. Eleven().Stages, new("A stage with an unusually long name for its box", "pending", null, 0)]
            };

            var diagram = await renderer.RenderAsync(DeliveryRunDiagramSpec.Archify(run), TestContext.Current.CancellationToken);

            Assert.Null(diagram.Unavailable);
            Assert.Contains("<html", diagram.Html!, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(diagram.ArtifactPath));

            var again = await renderer.RenderAsync(DeliveryRunDiagramSpec.Archify(run), TestContext.Current.CancellationToken);

            Assert.Same(diagram, again);
        }
        finally
        {
            try
            {
                Directory.Delete(cache, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task With_no_generator_the_renderer_says_so_instead_of_throwing()
    {
        var renderer = new ArchifyDeliveryRunDiagrams(null, Path.GetTempPath(), TimeSpan.FromSeconds(1));

        var diagram = await renderer.RenderAsync("{}", TestContext.Current.CancellationToken);

        Assert.Null(diagram.Html);
        Assert.Contains("not part of this installation", diagram.Unavailable, StringComparison.Ordinal);
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

            process?.WaitForExit(10_000);
            return process is { ExitCode: 0 };
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>The run in the screenshot the change was asked from: six done, one in
    /// progress, one blocked, three not reached.</summary>
    internal static DeliveryRun Eleven() =>
        SessionRowsTests.Run("run-mugw04px-sbxvga", "clever-wright-0be9f1", Noon.AddHours(-1), Noon, title: "Roadmap timeline sidebar/track row alignment") with
        {
            SkillId = "flow-code",
            Stages =
            [
                new("Scope Discovery", "done", 60_000, 1),
                new("Specification & Architecture Intake", "done", 60_000, 1),
                new("Reproduction & Root Cause", "done", 60_000, 1),
                new("Implementation", "done", 60_000, 1),
                new("Build & Test", "done", 60_000, 1),
                new("Validation", "done", 60_000, 1) { Agents = [new("qa:qa", "claude-sonnet-5", 1, 0), new("qa:qa-monitor", null, 0, 0)] },
                new("Personal Validation", "in_progress", null, 0),
                new("Create Pull Request", "blocked", null, 0),
                new("Verification", "pending", null, 0),
                new("Work Item Update", "pending", null, 0),
                new("Summary", "pending", null, 0)
            ]
        };
}
