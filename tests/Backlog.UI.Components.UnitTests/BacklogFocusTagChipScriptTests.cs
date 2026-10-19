using System.Diagnostics;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <c>window.backlogFocusTagChip</c>, run rather than read.
/// <para>
/// The tag strip is one row cut by overflow. A plain <c>focus()</c> on a chip that
/// sorts past that row scrolls the strip to it, and the first row is gone with
/// nothing to scroll it back — which is what handing the focus back to a released
/// plan did. These run the script's own function in Node against a strip whose
/// chips are laid out by hand, so the visible, clipped, hidden and scrolled cases
/// are each one rectangle.
/// </para>
/// </summary>
public sealed class BacklogFocusTagChipScriptTests
{
    [Fact]
    public void A_chip_on_the_strips_row_takes_the_focus_without_scrolling()
    {
        Assert.Equal("chip, preventScroll", Run(chipTop: 0, chipDrawn: true, moreDrawn: false));
    }

    /// <summary>The bug: a chip past the row would have scrolled the strip. The More
    /// toggle is the way to it, so the focus goes there.</summary>
    [Fact]
    public void A_chip_clipped_past_the_row_hands_the_focus_to_more()
    {
        Assert.Equal("more, preventScroll", Run(chipTop: 40, chipDrawn: true, moreDrawn: true));
    }

    /// <summary>Hidden with the rest of the unpicked strip at a narrow width, where More
    /// is not drawn either: the focus is left where it was.</summary>
    [Fact]
    public void A_hidden_chip_with_no_more_toggle_takes_nothing()
    {
        Assert.Equal("nothing", Run(chipTop: 0, chipDrawn: false, moreDrawn: false));
    }

    /// <summary>Tab onto a clipped chip had scrolled the strip before the press, so the
    /// lead pair the press moved the plan into read as clipped and the focus went to
    /// More. The strip is put back on its first row first, and the lead takes it.</summary>
    [Fact]
    public void A_strip_scrolled_by_tabbing_is_put_back_before_the_lead_is_measured()
    {
        Assert.Equal(
            "chip, preventScroll, scrollTop 0",
            Run(chipTop: 0, chipDrawn: true, moreDrawn: true, scrolledBy: 35));
    }

    private static string Run(int chipTop, bool chipDrawn, bool moreDrawn, int scrolledBy = 0)
    {
        // The chip's box is where layout put it less how far the strip is scrolled,
        // the way a browser reports it.
        var harness = $$"""
            let queue = [];
            let focused = 'nothing';
            const rect = (top, height) => ({ top, bottom: top + height });
            const strip = { id: 'tag-filter-strip', scrollTop: {{scrolledBy}}, getBoundingClientRect: () => rect(0, 30) };
            const chip = {
                getClientRects: () => {{(chipDrawn ? "[rect(0, 30)]" : "[]")}},
                getBoundingClientRect: () => rect({{chipTop}} - strip.scrollTop, 30),
                closest: selector => selector === '.filter-group--tags' ? strip : null,
                focus: options => { focused = 'chip' + (options && options.preventScroll ? ', preventScroll' : ''); }
            };
            const more = {
                getClientRects: () => {{(moreDrawn ? "[rect(0, 30)]" : "[]")}},
                focus: options => { focused = 'more' + (options && options.preventScroll ? ', preventScroll' : ''); }
            };
            const window = {};
            const document = {
                getElementById: id => id === 'chip' ? chip : null,
                querySelector: selector => selector === '[aria-controls="tag-filter-strip"]' ? more : null
            };
            const requestAnimationFrame = callback => { queue.push(callback); };
            const focusFrames = 10;
            const TAG_STRIP_SELECTOR = '.filter-group--tags';

            {{Cut("    window.backlogFocusTagChip = ", "    const tagStripResize = ")}}

            window.backlogFocusTagChip('chip');
            while (queue.length > 0) {
                const due = queue;
                queue = [];
                due.forEach(callback => callback());
            }

            console.log(focused + ({{scrolledBy}} > 0 ? `, scrollTop ${strip.scrollTop}` : ''));
            """;

        return Node(harness);
    }

    private static string Node(string harness)
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

            return output.Result.Trim();
        }
    }

    /// <summary>One declaration cut out of the script as it ships, from its own first
    /// line to the next one's, so the function under test is the one the app loads.</summary>
    private static string Cut(string from, string to)
    {
        var script = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        var begin = script.IndexOf(from, StringComparison.Ordinal);
        Assert.True(begin >= 0, $"components.js no longer declares '{from.Trim()}'.");

        var end = script.IndexOf(to, begin, StringComparison.Ordinal);
        Assert.True(end > begin, $"'{to.Trim()}' no longer follows '{from.Trim()}' in components.js.");

        return script[begin..end];
    }
}
