using System.Diagnostics;
using System.Globalization;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// How the Lobes atlas's camera moves, run rather than read
/// (<c>.devbook/domain/devbook/features.md#lobes-atlas</c>).
/// <para>
/// The renderer eases the camera toward where it is heading, lets a fling slow
/// down, ramps the idle turn in, and eases a new layout home on the motion
/// tokens' curve. Those steps are pure functions beside the renderer in
/// <c>components.js</c>; these cut them out of the script as it ships and run
/// them in Node, so the feel the person judged is the one the tests hold.
/// </para>
/// </summary>
public sealed class LobesCameraScriptTests
{
    [Fact]
    public void An_easing_closes_on_its_target_and_then_stops_there()
    {
        var output = Run("""
            let zoom = 1;
            let frames = 0;
            while (zoom !== 1.08 && frames < 200) {
                zoom = backlogLobesEase(zoom, 1.08, BACKLOG_LOBES_MOTION.frameMs, BACKLOG_LOBES_MOTION.zoomLerp, BACKLOG_LOBES_MOTION.zoomEpsilon);
                frames++;
            }
            console.log(frames);
            """);

        var frames = int.Parse(output, CultureInfo.InvariantCulture);

        // A wheel notch lands in well under 400 ms (24 frames at 60 Hz), so the
        // next notch never queues behind an unfinished one.
        Assert.InRange(frames, 2, 24);
    }

    [Fact]
    public void One_long_frame_moves_the_camera_as_far_as_two_short_ones()
    {
        var output = Run("""
            const m = BACKLOG_LOBES_MOTION;
            const one = backlogLobesEase(0, 1, 2 * m.frameMs, m.turnLerp, 0);
            const two = backlogLobesEase(backlogLobesEase(0, 1, m.frameMs, m.turnLerp, 0), 1, m.frameMs, m.turnLerp, 0);
            console.log(one.toFixed(9) + ' ' + two.toFixed(9));
            """);

        var values = Numbers(output);

        Assert.Equal(values[0], values[1], 8);
    }

    [Fact]
    public void A_fling_slows_and_comes_to_rest_within_a_second_and_a_half()
    {
        var output = Run("""
            const m = BACKLOG_LOBES_MOTION;
            let speed = m.flingMaxSpeed;
            let ms = 0;
            let slowing = true;
            let previous = speed;
            while (speed !== 0 && ms < 5000) {
                speed = backlogLobesDamp(speed, m.frameMs);
                if (Math.abs(speed) > Math.abs(previous)) slowing = false;
                previous = speed;
                ms += m.frameMs;
            }
            console.log(ms.toFixed(1) + ' ' + (slowing ? 1 : 0));
            """);

        var values = Numbers(output);

        // It keeps turning for a while after the release, and then stops.
        Assert.InRange(values[0], 150, 1500);
        Assert.Equal(1, values[1]);
    }

    [Fact]
    public void The_idle_turn_waits_its_delay_then_ramps_in_over_transition_slow()
    {
        var output = Run("""
            const m = BACKLOG_LOBES_MOTION;
            console.log([0, m.idleDelayMs - 1, m.idleDelayMs, m.idleDelayMs + m.idleRampMs / 2, m.idleDelayMs + m.idleRampMs, m.idleDelayMs + 60000]
                .map((ms) => backlogLobesIdleRamp(ms).toFixed(6)).join(' '));
            """);

        var shares = Numbers(output);

        Assert.Equal(0, shares[0]);
        Assert.Equal(0, shares[1]);
        Assert.Equal(0, shares[2]);
        Assert.InRange(shares[3], 0.01, 0.99);
        Assert.Equal(1, shares[4]);
        Assert.Equal(1, shares[5]);
    }

    [Fact]
    public void The_idle_turn_is_slow_enough_to_take_about_a_hundred_seconds_a_turn()
    {
        var output = Run("console.log((Math.PI * 2 / BACKLOG_LOBES_MOTION.idleSpeed).toFixed(3));");

        Assert.InRange(Numbers(output)[0], 80, 130);
    }

    [Fact]
    public void The_scope_curve_starts_at_rest_ends_at_rest_and_never_turns_back()
    {
        var output = Run("""
            const values = [];
            for (let i = 0; i <= 100; i++) values.push(backlogLobesEaseInOut(i / 100));
            let monotonic = true;
            for (let i = 1; i < values.length; i++) if (values[i] < values[i - 1] - 1e-9) monotonic = false;
            console.log([values[0], values[100], values[50], monotonic ? 1 : 0, values[1], values[99]].map((v) => v.toFixed(6)).join(' '));
            """);

        var values = Numbers(output);

        Assert.Equal(0, values[0]);
        Assert.Equal(1, values[1]);
        Assert.InRange(values[2], 0.01, 0.99);
        Assert.Equal(1, values[3]);
        // Slow at both ends: the first and last hundredth move far less than a
        // hundredth.
        Assert.InRange(values[4], 0, 0.005);
        Assert.InRange(1 - values[5], 0, 0.005);
    }

    [Fact]
    public void Easing_home_takes_the_short_way_round_however_far_the_idle_turn_wound()
    {
        var output = Run("""
            console.log([backlogLobesNearestTurn(-0.7 + 40 * Math.PI + 0.3, -0.7), backlogLobesNearestTurn(-0.7 - 7 * Math.PI, -0.7)]
                .map((v) => v.toFixed(6)).join(' '));
            """);

        var values = Numbers(output);

        Assert.Equal(-0.4, values[0], 5);
        Assert.InRange(Math.Abs(values[1] + 0.7), 0, Math.PI + 1e-6);
    }

    [Theory]
    [InlineData(1.08)]
    [InlineData(1.5)]
    [InlineData(0.8)]
    public void A_zoom_keeps_the_point_under_the_cursor_where_it_is(double ratio)
    {
        // The point under a cursor 200 px right of centre, with a 30 px pan,
        // sits 170 px from the scene centre; after the zoom it sits 170 * ratio
        // from it, and the new pan puts it back under the cursor.
        var output = Run($$"""
            const pan = backlogLobesAnchorPan(30, 200, {{ratio.ToString(CultureInfo.InvariantCulture)}});
            console.log((pan + (200 - 30) * {{ratio.ToString(CultureInfo.InvariantCulture)}}).toFixed(6));
            """);

        Assert.Equal(200, Numbers(output)[0], 5);
    }

    [Fact]
    public void The_pan_is_held_at_the_centre_until_zoomed_in_and_never_past_the_edge()
    {
        var output = Run("""
            console.log([0.5, 1, 1.5, 2, 2.4].map((zoom) => backlogLobesPanLimit(400, zoom).toFixed(3)).join(' '));
            """);

        Assert.Equal(new double[] { 0, 0, 200, 400, 400 }, Numbers(output));
    }

    private static double[] Numbers(string output) =>
        [.. output.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(value => double.Parse(value, CultureInfo.InvariantCulture))];

    private static string Run(string body) => RunNode($"{MotionFunctions()}\n\n{body}");

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

    /// <summary>The camera's motion constants and step functions, from
    /// <c>BACKLOG_LOBES_MOTION</c> up to the camera report constant after them,
    /// cut out of the script as it ships.</summary>
    private static string MotionFunctions()
    {
        var script = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        const string from = "    const BACKLOG_LOBES_MOTION = ";
        const string to = "    // The shortest gap between two camera reports";

        var begin = script.IndexOf(from, StringComparison.Ordinal);
        Assert.True(begin >= 0, "components.js no longer declares BACKLOG_LOBES_MOTION.");

        var end = script.IndexOf(to, begin, StringComparison.Ordinal);
        Assert.True(end > begin, "The camera report constant no longer follows the Lobes motion functions in components.js.");

        return script[begin..end];
    }
}
