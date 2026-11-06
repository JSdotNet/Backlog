using System.Diagnostics;
using System.Globalization;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Where the Lobes atlas seats its hubs, run rather than read
/// (<c>.devbook/domain/brain/features.md#lobes-atlas</c>).
/// <para>
/// The design draws two or three hubs in a brain stem, and the renderer stepped
/// each next hub twelve world units further down. The live QA pass met real data:
/// a Brain has a hub per area — nine in the test Brain — and a devbook one per
/// convention and reading-order root — twenty-two in this repository's — so the
/// stem ran out of the brain and off the bottom of the canvas at the default view.
/// These run the script's own anatomy function in Node, the one the app loads.
/// </para>
/// </summary>
public sealed class LobesAnatomyScriptTests
{
    /// <summary>The stem's centre in world units, as <c>BACKLOG_LOBES_CENTRES.stem</c> sets it.</summary>
    private const double StemY = 300;

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    [InlineData(22)]
    [InlineData(60)]
    public void However_many_hubs_a_stem_holds_they_stay_close_under_the_brain(int hubs)
    {
        var seats = Run(hubs);

        Assert.Equal(hubs, seats.Count);
        Assert.All(seats, seat =>
        {
            // The seats are printed to three decimals, so the caps carry a hair of slack.
            Assert.InRange(seat.Y, StemY, StemY + 80.01);
            Assert.InRange(Math.Sqrt(Math.Pow(seat.X - 30, 2) + Math.Pow(seat.Z, 2)), 0, 200.01);
        });
    }

    /// <summary>A handful keeps the design's short staircase: each next hub a
    /// little lower than the one before it.</summary>
    [Fact]
    public void A_few_hubs_still_step_down_one_after_another()
    {
        var seats = Run(3);

        Assert.True(seats[0].Y < seats[1].Y && seats[1].Y < seats[2].Y, string.Join(", ", seats.Select(seat => seat.Y)));
    }

    // ----- A ghost's dashed outline --------------------------------------------------

    /// <summary>The shell's radii in world units, as <c>BACKLOG_LOBES_SHELL</c> sets them.</summary>
    private const double ShellX = 340;
    private const double ShellY = 290;
    private const double ShellZ = 430;

    /// <summary>A ghost scope draws no dust; its brain is one dashed ellipse, the
    /// shell's silhouette. Facing the camera square on, that is the shell's own
    /// width and height.</summary>
    [Fact]
    public void Square_on_a_ghosts_outline_is_the_shells_width_and_height()
    {
        var outline = Outline(0, 0);

        Assert.Equal(ShellX, outline.Rx, 6);
        Assert.Equal(ShellY, outline.Ry, 6);
        Assert.Equal(0, outline.Rotation, 6);
    }

    [Fact]
    public void A_quarter_turn_shows_the_shells_depth_as_the_outlines_width()
    {
        var outline = Outline(Math.PI / 2, 0);

        Assert.Equal(ShellZ, outline.Rx, 6);
        Assert.Equal(ShellY, outline.Ry, 6);
    }

    [Fact]
    public void A_quarter_tilt_shows_the_shells_depth_as_the_outlines_height()
    {
        var outline = Outline(0, Math.PI / 2);

        Assert.Equal(ShellX, outline.Rx, 6);
        Assert.Equal(ShellZ, outline.Ry, 6);
    }

    /// <summary>At any angle the outline is the edge of the shell as drawn: every
    /// point of the shell, turned the way <c>project()</c> turns it, falls inside
    /// the ellipse, and the farthest of them reaches its edge.</summary>
    [Theory]
    [InlineData(-0.7, -0.2)]
    [InlineData(0.4, 1.1)]
    [InlineData(2.5, -1.3)]
    [InlineData(-3.9, 0.6)]
    public void At_any_angle_the_outline_hugs_the_turned_shell(double yaw, double pitch)
    {
        var outline = Outline(yaw, pitch);

        Assert.InRange(outline.Rx, ShellY - 1e-6, ShellZ + 1e-6);
        Assert.InRange(outline.Ry, ShellY - 1e-6, ShellZ + 1e-6);

        var reach = Numbers(RunNode($$"""
            {{AnatomyFunctions()}}

            const yaw = {{Invariant(yaw)}};
            const pitch = {{Invariant(pitch)}};
            const outline = backlogLobesOutline(yaw, pitch);
            const cy = Math.cos(yaw), sy = Math.sin(yaw), cp = Math.cos(pitch), sp = Math.sin(pitch);
            const cr = Math.cos(outline.rotation), sr = Math.sin(outline.rotation);
            const S = BACKLOG_LOBES_SHELL;
            let most = 0;
            for (let i = 0; i <= 240; i++) {
                for (let j = 0; j <= 240; j++) {
                    const u = (i / 240) * Math.PI * 2;
                    const v = (j / 240) * Math.PI;
                    const wx = Math.sin(v) * Math.cos(u) * S.x;
                    const wy = Math.cos(v) * S.y;
                    const wz = Math.sin(v) * Math.sin(u) * S.z;
                    const x1 = wx * cy + wz * sy;
                    const z1 = -wx * sy + wz * cy;
                    const y2 = wy * cp - z1 * sp;
                    const a = (x1 * cr + y2 * sr) / outline.rx;
                    const b = (-x1 * sr + y2 * cr) / outline.ry;
                    most = Math.max(most, a * a + b * b);
                }
            }
            console.log(most.toFixed(6));
            """))[0];

        Assert.InRange(reach, 0.99, 1 + 1e-6);
    }

    private sealed record Ellipse(double Rx, double Ry, double Rotation);

    private static Ellipse Outline(double yaw, double pitch)
    {
        var values = Numbers(RunNode($$"""
            {{AnatomyFunctions()}}

            const outline = backlogLobesOutline({{Invariant(yaw)}}, {{Invariant(pitch)}});
            console.log([outline.rx, outline.ry, outline.rotation].map((v) => v.toFixed(9)).join(' '));
            """));

        return new Ellipse(values[0], values[1], values[2]);
    }

    private static string Invariant(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static double[] Numbers(string output) =>
        [.. output.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(value => double.Parse(value, CultureInfo.InvariantCulture))];

    /// <summary>The renderer makes no dust for a ghost scope and strokes its
    /// outline dashed in the ghost's muted ink, then puts the dash back, so
    /// nothing drawn after it inherits the pattern. bUnit has no canvas, so this
    /// reads the shape of the drawing code.</summary>
    [Fact]
    public void A_ghost_scope_makes_no_dust_and_its_outline_is_stroked_dashed_in_the_ghost_ink()
    {
        var making = ComponentsScript.Region("        const dust = [];", "        const scopeSatellite = ");
        Assert.Contains("kind === 'ghost'", making, StringComparison.Ordinal);
        Assert.Contains("outlines.push(", making, StringComparison.Ordinal);

        var drawing = ComponentsScript.Region("        function drawOutlines() {", "        function emphasis(markId) {");
        Assert.Contains("backlogLobesOutline(view.yaw, view.pitch)", drawing, StringComparison.Ordinal);
        Assert.Contains("context.ellipse(", drawing, StringComparison.Ordinal);
        Assert.Contains("palette.ghost.accent", drawing, StringComparison.Ordinal);
        Assert.Contains("context.setLineDash([5 * dash, 5 * dash]);", drawing, StringComparison.Ordinal);
        Assert.Contains("context.setLineDash([]);", drawing, StringComparison.Ordinal);
        Assert.DoesNotMatch("#[0-9a-fA-F]{3,6}\\b|rgba?\\(", drawing);

        var draw = ComponentsScript.Region("            drawDust();", "            drawLinks(satellitePoint);");
        Assert.Contains("drawOutlines();", draw, StringComparison.Ordinal);
    }

    private sealed record Seat(double X, double Y, double Z);

    private static List<Seat> Run(int hubs)
    {
        var harness = $$"""
            {{GoldenAngle()}}

            {{AnatomyFunctions()}}

            const marks = [];
            for (let i = 0; i < {{hubs.ToString(CultureInfo.InvariantCulture)}}; i++) {
                marks.push({ id: 'hub-' + String(i).padStart(3, '0'), scope: 'brain', lobe: 'stem', kind: 'hub' });
            }

            const points = backlogLobesAnatomy({ marks, links: [] });
            console.log(points.map((point) => [point.x, point.y, point.z].map((v) => v.toFixed(3)).join(' ')).join('\n'));
            """;

        var output = RunNode(harness);

        return [.. output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' ').Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray())
            .Select(v => new Seat(v[0], v[1], v[2]))];
    }

    private static string RunNode(string harness)
    {
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("node did not start");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Assert.Fail($"This test runs components.js and needs Node on the PATH: {exception.Message}");
            return string.Empty;
        }

        using (process)
        {
            process.StandardInput.Write(harness);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();

            Assert.True(process.WaitForExit(TimeSpan.FromSeconds(30)), "node did not finish in thirty seconds.");
            Assert.True(process.ExitCode == 0, $"node failed ({process.ExitCode}):\n{error.Result}");

            return output.Result.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        }
    }

    /// <summary>The golden angle the hubs spiral by, cut out of the script too.</summary>
    private static string GoldenAngle()
    {
        var line = Script().Split('\n')
            .SingleOrDefault(candidate => candidate.TrimStart().StartsWith("const BACKLOG_ATLAS_GOLDEN = ", StringComparison.Ordinal));
        Assert.True(line is not null, "components.js no longer declares BACKLOG_ATLAS_GOLDEN.");
        return line ?? string.Empty;
    }

    private static string Script() =>
        File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>The Lobes atlas's constants and helpers up to and including
    /// <c>backlogLobesAnatomy</c>, cut out of the script as it ships.</summary>
    private static string AnatomyFunctions()
    {
        var script = Script();

        const string from = "    const BACKLOG_LOBES_CENTRES = ";
        const string to = "    function backlogLobesSeats(";

        var begin = script.IndexOf(from, StringComparison.Ordinal);
        Assert.True(begin >= 0, "components.js no longer declares BACKLOG_LOBES_CENTRES.");

        var end = script.IndexOf(to, begin, StringComparison.Ordinal);
        Assert.True(end > begin, "backlogLobesSeats no longer follows backlogLobesAnatomy in components.js.");

        // The comment block above backlogLobesSeats opens before it; cut at its start.
        var comment = script.LastIndexOf("    /*", end, StringComparison.Ordinal);
        return script[begin..(comment > begin ? comment : end)];
    }
}
