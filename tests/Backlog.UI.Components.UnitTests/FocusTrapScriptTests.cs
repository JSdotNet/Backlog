using System.Diagnostics;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <c>window.backlogFocusTrap</c>'s Escape hold, run rather than read.
/// <para>
/// A detail sheet opened with the pointer — a node on the Devbook's atlas canvas, a
/// row of its index — could leave the focus on what was pressed, outside the sheet.
/// Escape then went to the surface behind it: the Devbook closes on Escape, so one
/// press closed the sheet and the whole Devbook with it, because the sheet's own
/// boundary only stops a key that starts inside it. bUnit has no focus and no
/// capture phase, so these run the script's own trap in Node against a stand-in
/// document whose keydown goes through the window's capture listeners first.
/// </para>
/// </summary>
public sealed class FocusTrapScriptTests
{
    /// <summary>The bug: Escape pressed on the canvas while the sheet is open. The
    /// surface never hears it; the sheet does, with the focus moved into it.</summary>
    [Fact]
    public void An_escape_outside_an_open_sheet_goes_to_the_sheet_and_not_the_surface()
    {
        var result = Run("""
            window.backlogFocusTrap('sheet', null, true);
            const event = press(canvas, 'Escape');
            console.log(`surface=${surfaceHeard} sheet=${sheetHeard} active=${document.activeElement.name} prevented=${event.defaultPrevented}`);
            """);

        Assert.Equal("surface=0 sheet=1 active=sheet prevented=true", result);
    }

    /// <summary>A key pressed inside the sheet is the sheet's own handler's already,
    /// and passes through untouched; another key outside it is not Escape's business.</summary>
    [Fact]
    public void A_key_inside_the_sheet_or_another_key_outside_it_passes_through()
    {
        var result = Run("""
            window.backlogFocusTrap('sheet', null, true);
            press(field, 'Escape');
            press(canvas, 'a');
            console.log(`surface=${surfaceHeard} sheet=${sheetHeard}`);
            """);

        Assert.Equal("surface=1 sheet=1", result);
    }

    /// <summary>Released with the sheet, and not held at all for a sheet that does not
    /// close on Escape: the surface hears the key again.</summary>
    [Fact]
    public void A_released_trap_or_one_that_does_not_own_escape_holds_nothing()
    {
        var result = Run("""
            window.backlogFocusTrap('sheet', null, true);
            window.backlogReleaseFocusTrap('sheet');
            press(canvas, 'Escape');
            window.backlogFocusTrap('sheet', null, false);
            press(canvas, 'Escape');
            console.log(`surface=${surfaceHeard} sheet=${sheetHeard}`);
            """);

        Assert.Equal("surface=2 sheet=0", result);
    }

    /// <summary>A dialog open over the sheet keeps its own Escape.</summary>
    [Fact]
    public void An_escape_in_a_dialog_over_the_sheet_is_left_to_the_dialog()
    {
        var result = Run("""
            window.backlogFocusTrap('sheet', null, true);
            press(dialogField, 'Escape');
            console.log(`surface=${surfaceHeard} sheet=${sheetHeard}`);
            """);

        Assert.Equal("surface=1 sheet=0", result);
    }

    private static string Run(string body)
    {
        var harness = $$"""
            const captureListeners = [];
            const window = {
                addEventListener(type, fn, capture) { if (type === 'keydown' && capture) captureListeners.push(fn); },
                removeEventListener(type, fn, capture) {
                    const at = captureListeners.indexOf(fn);
                    if (at >= 0) captureListeners.splice(at, 1);
                }
            };
            let surfaceHeard = 0;
            let sheetHeard = 0;

            class HTMLElement {
                constructor(name, parent, dialog) { this.name = name; this.parent = parent ?? null; this.dialog = dialog ?? false; this.dataset = {}; this.isConnected = true; }
                focus() { document.activeElement = this; }
                contains(other) { for (let node = other; node; node = node.parent) if (node === this) return true; return false; }
                closest() { for (let node = this; node; node = node.parent) if (node.dialog) return node; return null; }
                addEventListener() {}
                removeEventListener() {}
                querySelectorAll() { return []; }
                getAttribute() { return null; }
                hasAttribute() { return false; }
                dispatchEvent(event) { deliver(this, event); return !event.defaultPrevented; }
            }
            class KeyboardEvent {
                constructor(type, init) { Object.assign(this, init); this.type = type; }
            }
            const requestAnimationFrame = () => 0;

            const body = new HTMLElement('body');
            const sheet = new HTMLElement('sheet');
            const field = new HTMLElement('field', sheet);
            const canvas = new HTMLElement('canvas');
            const modal = new HTMLElement('modal', null, true);
            const dialogField = new HTMLElement('dialog-field', modal);
            const document = {
                body,
                activeElement: body,
                getElementById: id => id === 'sheet' ? sheet : null
            };

            // The capture phase first, then the target's own ancestors: the sheet's
            // handler ends Blazor's walk for a key inside it, as Blazor's
            // stopPropagation does; anything else reaches the surface.
            function deliver(target, event) {
                event.target = target;
                event.defaultPrevented ??= false;
                event.stopped = false;
                event.preventDefault = function () { this.defaultPrevented = true; };
                event.stopPropagation = function () { this.stopped = true; };

                for (const listener of [...captureListeners]) {
                    listener(event);
                    if (event.stopped) return event;
                }

                if (sheet.contains(target)) { if (event.key === 'Escape') sheetHeard++; return event; }
                surfaceHeard++;
                return event;
            }

            function press(target, key) {
                target.focus();
                return deliver(target, { key });
            }

            {{TrapFunctions()}}

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

    /// <summary>The focus helpers from the two maps they share to the end of the
    /// trap's release, cut out of the script as it ships, so the functions under test
    /// are the ones the app loads.</summary>
    private static string TrapFunctions() =>
        ComponentsScript.Region("    const backlogFocusTrapListeners = new Map();", "    const backlogModalLayers = new Map();");
}
