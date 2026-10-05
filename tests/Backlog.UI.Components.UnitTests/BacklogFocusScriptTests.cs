using System.Diagnostics;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <c>window.backlogFocus</c>, run rather than read.
/// <para>
/// A component asks for the caret from its <c>OnAfterRenderAsync</c>, naming an
/// element the render it has just finished created. Blazor Server applies that
/// render before the call arrives, so the element is always there — which is why
/// the web harness and every bUnit test passed. The desktop head's WebView does not
/// promise that order: "+ New entry" opened the title field and the call to focus
/// it found nothing, so the caret stayed on the button. These run the script's own
/// function in Node against a document whose element turns up a frame late.
/// </para>
/// </summary>
public sealed class BacklogFocusScriptTests
{
    [Fact]
    public void An_element_that_is_already_there_takes_the_caret_at_once()
    {
        var result = Run(appearsOnFrame: 0);

        Assert.Equal("focused on frame 0, selected", result);
    }

    /// <summary>The bug: the field is created by a render the browser has not
    /// applied yet when the focus call reaches it.</summary>
    [Fact]
    public void An_element_that_arrives_a_frame_later_still_takes_the_caret()
    {
        var result = Run(appearsOnFrame: 2);

        Assert.Equal("focused on frame 2, selected", result);
    }

    /// <summary>Waiting is bounded: an element that never comes — the field was
    /// closed again before it arrived — leaves no frame loop running.</summary>
    [Fact]
    public void An_element_that_never_arrives_stops_being_waited_for()
    {
        var result = Run(appearsOnFrame: null);

        Assert.Equal("never focused, frames left: 0", result);
    }

    private static string Run(int? appearsOnFrame)
    {
        var harness = $$"""
            let frame = 0;
            let queue = [];
            let focusedOn = null;
            let selected = false;
            const element = {
                focus() { focusedOn = frame; },
                select() { selected = true; }
            };
            const appearsOn = {{(appearsOnFrame is { } n ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}};
            const window = {};
            const document = {
                getElementById: id => id === 'field' && appearsOn !== null && frame >= appearsOn ? element : null
            };
            const requestAnimationFrame = callback => { queue.push(callback); };

            {{FocusFunction()}}

            window.backlogFocus('field', true);
            while (queue.length > 0 && frame < 100) {
                frame++;
                const due = queue;
                queue = [];
                due.forEach(callback => callback());
            }

            console.log(focusedOn === null
                ? `never focused, frames left: ${queue.length}`
                : `focused on frame ${focusedOn}${selected ? ', selected' : ''}`);
            """;

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

            return output.Result.Trim();
        }
    }

    /// <summary>The assignment of <c>window.backlogFocus</c> and the frame budget
    /// above it, cut out of the script as it ships, so the function under test is
    /// the one the app loads.</summary>
    private static string FocusFunction()
    {
        var script = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        const string from = "    const focusFrames = ";
        const string to = "    // Tab inside a quick edit belongs to the list";

        var begin = script.IndexOf(from, StringComparison.Ordinal);
        Assert.True(begin >= 0, "components.js no longer declares the frames window.backlogFocus waits.");

        var end = script.IndexOf(to, begin, StringComparison.Ordinal);
        Assert.True(end > begin, "The Tab guard no longer follows window.backlogFocus in components.js.");

        return script[begin..end];
    }
}
