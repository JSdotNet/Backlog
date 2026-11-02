using System.Diagnostics;
using System.Globalization;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Where the Lobes atlas seats its hubs, run rather than read
/// (<c>.devbook/domain/devbook/features.md#lobes-atlas</c>).
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
