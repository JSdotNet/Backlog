using System.Diagnostics;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <c>window.backlogPopoverOutside</c>, run rather than read.
/// <para>
/// An open PopoverButton closes on an outside click through a fixed backdrop, and a
/// fixed element is only window-sized while no ancestor is its containing block. The
/// Tasks bar's Filter menu sits in a query container, which is one, so its backdrop
/// covered the list column alone and a click in the entry beside it acted on what it
/// landed on. These run the script's own watch in Node against a fake window that
/// dispatches a press's events through the capture listeners first, the way a
/// browser does, and records what the page underneath would still have heard.
/// </para>
/// </summary>
public sealed class BacklogPopoverOutsideScriptTests
{
    /// <summary>The contract's "an outside click only closes": the panel is told to
    /// close, and the press's events never reach the page under it.</summary>
    [Fact]
    public void A_press_outside_closes_the_panel_and_reaches_nothing_under_it()
    {
        Assert.Equal(
            "page heard: none; prevented: click; closed: 1",
            Run("press(outsideNode)"));
    }

    /// <summary>Inside the component — the trigger, the panel, the backdrop — the
    /// press is the component's own, and the watch keeps out of it.</summary>
    [Fact]
    public void A_press_inside_is_left_to_the_component()
    {
        Assert.Equal(
            "page heard: pointerdown mousedown pointerup mouseup click; prevented: none; closed: 0",
            Run("press(insideNode)"));
    }

    /// <summary>A click with no press before it is a key's — Enter on a control the
    /// reader tabbed to — which the backdrop never caught either.</summary>
    [Fact]
    public void A_keyboard_click_outside_is_let_through()
    {
        Assert.Equal(
            "page heard: click; prevented: none; closed: 0",
            Run("dispatch('click', outsideNode)"));
    }

    /// <summary>A press with another button is not the click the contract is about:
    /// a right press opens the row's own menu, as it would with nothing open.</summary>
    [Fact]
    public void A_press_with_another_button_is_let_through()
    {
        Assert.Equal(
            "page heard: pointerdown mousedown pointerup mouseup click; prevented: none; closed: 0",
            Run("press(outsideNode, 2)"));
    }

    /// <summary>
    /// A primary press outside whose click never comes — a touch pan the browser
    /// cancelled, a context menu (Ctrl and a press on a Mac), a drag — must not leave
    /// the watch waiting for it: the next key's click on a control outside is the
    /// reader's, and goes through.
    /// </summary>
    [Theory]
    [InlineData("pointercancel")]
    [InlineData("contextmenu")]
    [InlineData("dragstart")]
    [InlineData("keydown")]
    public void A_press_whose_click_never_comes_does_not_swallow_a_later_keyboard_click(string ending)
    {
        Assert.Equal(
            $"page heard: {ending} click; prevented: none; closed: 0",
            Run($"dispatch('pointerdown', outsideNode); heard.length = 0; dispatch('{ending}', outsideNode); dispatch('click', outsideNode)"));
    }

    /// <summary>Closed is closed: once unwatched, an outside press reaches the page and
    /// closes nothing.</summary>
    [Fact]
    public void Unwatching_removes_every_listener()
    {
        Assert.Equal(
            "page heard: pointerdown mousedown pointerup mouseup click; prevented: none; closed: 0; listeners: 0",
            Run("window.backlogPopoverOutside.unwatch('panel'); press(outsideNode)"));
    }

    private static string Run(string act)
    {
        var harness = $$"""
            class Node { }
            const capture = new Map();
            const window = {
                addEventListener: (name, listener, useCapture) => {
                    if (!useCapture) throw new Error('not a capture listener');
                    if (!capture.has(name)) capture.set(name, new Set());
                    capture.get(name).add(listener);
                },
                removeEventListener: (name, listener) => capture.get(name)?.delete(listener),
            };
            const insideNode = new Node();
            const outsideNode = new Node();
            const root = { contains: node => node === insideNode };
            let closed = 0;
            const dotnet = { invokeMethodAsync: (method) => { if (method === 'CloseFromOutsideAsync') closed++; return Promise.resolve(); } };

            const heard = [];
            const prevented = [];
            const dispatch = (name, target, button = 0) => {
                let stopped = false;
                const event = {
                    target,
                    button,
                    stopPropagation: () => { stopped = true; },
                    preventDefault: () => { prevented.push(name); },
                };
                for (const listener of capture.get(name) ?? []) {
                    listener(event);
                    if (stopped) return;
                }
                heard.push(name);
            };
            const press = (target, button = 0) => ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'].forEach(name => dispatch(name, target, button));

            {{Cut("    const backlogPopoverWatches = new Map();", "    // Whether an element is at or under a width")}}

            window.backlogPopoverOutside.watch('panel', root, dotnet, 'CloseFromOutsideAsync');
            {{act}};

            const count = [...capture.values()].reduce((sum, set) => sum + set.size, 0);
            console.log(`page heard: ${heard.join(' ') || 'none'}; prevented: ${prevented.join(' ') || 'none'}; closed: ${closed}`
                + ({{(act.Contains("unwatch", StringComparison.Ordinal) ? "true" : "false")}} ? `; listeners: ${count}` : ''));
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

    /// <summary>The watch cut out of the script as it ships, from its map to the
    /// next declaration, so the function under test is the one the app loads.</summary>
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
