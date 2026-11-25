using System.Diagnostics;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <c>window.backlogPaneResizer</c> and the separator's key defaults, run rather than
/// read.
/// <para>
/// bUnit has no JS engine, so the two things a <c>SplitPane</c> leaves to the script
/// run here in Node, cut out of <c>components.js</c> as it ships, against a stand-in
/// document: which keys on a separator have their default refused, and when a
/// stacked split's ceiling is measured.
/// </para>
/// </summary>
public sealed class SplitPaneScriptTests
{
    /// <summary>
    /// The keys a side-by-side separator handles are refused their default, and Tab is
    /// not — the separator was a keyboard trap while every key's default was refused.
    /// </summary>
    [Fact]
    public void Beside_the_separator_refuses_only_the_keys_it_handles()
    {
        var result = Run("""
            const layout = split('horizontal');
            const keys = ['ArrowLeft', 'ArrowRight', 'PageUp', 'PageDown', 'Home', 'End', 'ArrowUp', 'ArrowDown', 'Tab'];
            console.log(keys.map(key => `${key}=${press(layout.separator, key)}`).join(' '));
            """);

        Assert.Equal(
            "ArrowLeft=true ArrowRight=true PageUp=true PageDown=true Home=true End=true ArrowUp=false ArrowDown=false Tab=false",
            result);
    }

    /// <summary>Stacked, Up and Down are the separator's and Left and Right are not;
    /// Tab and Shift+Tab always leave.</summary>
    [Fact]
    public void Stacked_the_separator_refuses_only_the_keys_it_handles()
    {
        var result = Run("""
            const layout = split('vertical');
            const keys = ['ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End', 'ArrowLeft', 'ArrowRight', 'Tab'];
            const shiftTab = press(layout.separator, 'Tab', { shiftKey: true });
            console.log(keys.map(key => `${key}=${press(layout.separator, key)}`).join(' ') + ` ShiftTab=${shiftTab}`);
            """);

        Assert.Equal(
            "ArrowUp=true ArrowDown=true PageUp=true PageDown=true Home=true End=true ArrowLeft=false ArrowRight=false Tab=false ShiftTab=false",
            result);
    }

    /// <summary>A key anywhere else is none of the separator's business.</summary>
    [Fact]
    public void A_key_outside_a_separator_keeps_its_default()
    {
        var result = Run("""
            const layout = split('horizontal');
            console.log(`${press(layout.start, 'ArrowLeft')} ${press(layout.start, 'Home')}`);
            """);

        Assert.Equal("false false", result);
    }

    /// <summary>
    /// A stacked split's ceiling follows its host's height, not only the window's.
    /// <para>
    /// Its height comes from the column it sits in, which can change with no window
    /// resize — a map shown, a column laid out after the first render. A split not
    /// laid out yet reports nothing, rather than a ceiling of its own minimum that
    /// would pin the map there; the observer reports once it has a height, again when
    /// that height changes, and never after dispose.
    /// </para>
    /// </summary>
    [Fact]
    public void A_stacked_split_reports_its_ceiling_when_its_height_changes_and_not_before_it_has_one()
    {
        var result = Run("""
            const layout = split('vertical', 0);
            window.backlogPaneResizer.initialize(owner, 'k');
            const atInitialize = reports.length;

            layout.clientHeight = 400;
            observers[0].fire();
            layout.clientHeight = 320;
            observers[0].fire();

            window.backlogPaneResizer.dispose('k');
            console.log(`initialize=${atInitialize} reports=${reports.join(',')} disconnected=${observers[0].disconnected}`);
            """);

        // 400px / 16 = 25rem - 10rem floor - 1 = 14; 320px = 20rem -> 9.
        Assert.Equal("initialize=0 reports=14,9 disconnected=true", result);
    }

    /// <summary>A side-by-side split is measured as it always was: at initialize, and
    /// on the window's resize, with no observer of its own.</summary>
    [Fact]
    public void A_side_by_side_split_is_measured_as_before()
    {
        var result = Run("""
            const layout = split('horizontal', 0, 800);
            window.backlogPaneResizer.initialize(owner, 'k');
            console.log(`reports=${reports.join(',')} observers=${observers.length}`);
            """);

        // 800px / 16 = 50rem - 22rem floor - 1 = 27.
        Assert.Equal("reports=27 observers=0", result);
    }

    /// <summary>
    /// The bounds are reported per owner, each to the split it measured, and a split
    /// that is not mounted costs the owners after it nothing.
    /// <para>
    /// Two splits on one page do not have the same room. An owner registered for a
    /// layout not in the document yet (mid-render, or disposed) is skipped, and the
    /// loop carries on to the next owner rather than returning.
    /// </para>
    /// </summary>
    [Fact]
    public void Pane_bounds_are_reported_per_owner_and_an_unmounted_split_costs_the_others_nothing()
    {
        var result = Run("""
            window.backlogPaneResizer.initialize(ownerNamed('gone'), 'gone');
            split('horizontal', 0, 800, 'a');
            split('horizontal', 0, 1200, 'b');
            window.backlogPaneResizer.initialize(ownerNamed('a'), 'a');
            window.backlogPaneResizer.initialize(ownerNamed('b'), 'b');
            console.log(reports.join(','));
            """);

        // 800px = 50rem - 22rem floor - 1 = 27; 1200px = 75rem -> 52. Each
        // initialize reports every mounted owner; 'gone' never reports.
        Assert.Equal("a:27,a:27,b:52", result);
    }

    private static string Run(string body)
    {
        var harness = $$"""
            const keydownListeners = [];
            const window = {};
            const reports = [];
            const observers = [];
            const owner = {
                invokeMethodAsync(method, value) { if (method === 'SetSidePaneMaxWidthAsync') reports.push(value); return Promise.resolve(); }
            };
            const ownerNamed = name => ({
                invokeMethodAsync(method, value) { if (method === 'SetSidePaneMaxWidthAsync') reports.push(`${name}:${value}`); return Promise.resolve(); }
            });

            class Element {
                constructor(attributes, parent) { this.attributes = attributes ?? {}; this.parent = parent ?? null; this.clientHeight = 0; this.clientWidth = 0; }
                getAttribute(name) { return this.attributes[name] ?? null; }
                matches(selector) {
                    const match = /^\[([a-z-]+)(?:="([^"]*)")?\]$/.exec(selector);
                    return match != null && (match[1] in this.attributes) && (match[2] === undefined || this.attributes[match[1]] === match[2]);
                }
                closest(selector) { for (let node = this; node; node = node.parent) if (node.matches(selector)) return node; return null; }
            }

            class ResizeObserver {
                constructor(callback) { this.callback = callback; this.disconnected = false; observers.push(this); }
                observe(target) { this.target = target; }
                disconnect() { this.disconnected = true; }
                fire() { if (!this.disconnected) this.callback([{ target: this.target }]); }
            }

            const layouts = [];
            const document = {
                documentElement: {},
                addEventListener(type, fn) { if (type === 'keydown') keydownListeners.push(fn); },
                querySelector(selector) { return layouts.find(layout => layout.matches(selector)) ?? null; }
            };
            const getComputedStyle = () => ({
                fontSize: '16px',
                rowGap: '0px',
                columnGap: '0px',
                getPropertyValue: name => ({ '--pane-min-height': '10rem', '--pane-min-width': '22rem' })[name] ?? ''
            });
            const requestAnimationFrame = () => 0;

            function split(orientation, height, width, key) {
                const attributes = { 'data-pane-split': 'true', 'data-pane-orientation': orientation, 'data-pane-owner': key ?? 'k' };
                if (orientation === 'vertical') attributes['data-pane-min'] = '4';
                const layout = new Element(attributes);
                layout.clientHeight = height ?? 0;
                layout.clientWidth = width ?? 0;
                layout.start = new Element({}, layout);
                layout.separator = new Element({ 'data-pane-resizer': 'true', role: 'separator' }, layout);
                layouts.push(layout);
                return layout;
            }

            function press(target, key, init) {
                const event = { key, target, defaultPrevented: false, ...(init ?? {}) };
                event.preventDefault = function () { this.defaultPrevented = true; };
                for (const listener of keydownListeners) listener(event);
                return event.defaultPrevented;
            }

            {{ResizerRegion()}}

            {{body}}
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

    /// <summary>The resizer from its constants to the separator's key listener, cut
    /// out of the script as it ships, so the code under test is the code the app
    /// loads.</summary>
    private static string ResizerRegion() =>
        ComponentsScript.Region("    const BACKLOG_PANE_MIN_REM = 24;", "    // An open PopoverButton's outside click");
}
