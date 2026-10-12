using System.Diagnostics;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <c>window.backlogShowModalLayer</c> and <c>backlogCloseModalLayer</c>, run rather
/// than read. bUnit has no browser, so these run the script's own functions in
/// Node against a stand-in for the native <c>dialog</c> and the toast tray.
/// </summary>
public sealed class ModalLayerScriptTests
{
    /// <summary>A cancelled Escape keydown never becomes a close request, so the
    /// browser cannot close the layer behind Modal's back — while the keydown still
    /// bubbles on to Blazor, which decides whether Escape closes the dialog.</summary>
    [Fact]
    public void Escape_on_the_layer_is_cancelled_and_still_bubbles()
    {
        var result = Run("""
            window.backlogShowModalLayer('layer');
            const escape = layer.dispatch('keydown', { key: 'Escape' });
            const other = layer.dispatch('keydown', { key: 'a' });
            const cancel = layer.dispatch('cancel', {});
            console.log(`escape prevented=${escape.defaultPrevented} stopped=${escape.stopped}; a prevented=${other.defaultPrevented}; cancel prevented=${cancel.defaultPrevented}`);
            """);

        Assert.Equal("escape prevented=true stopped=false; a prevented=false; cancel prevented=true", result);
    }

    /// <summary>Modal renders the layer already open, so a page without the script
    /// still shows it. showModal() throws on an open dialog, so it is closed first.</summary>
    [Fact]
    public void A_layer_rendered_open_is_closed_and_shown_modally()
    {
        var result = Run("""
            layer.open = true;
            window.backlogShowModalLayer('layer');
            console.log(calls.join(','));
            """);

        Assert.Equal("close,showModal", result);
    }

    /// <summary>A browser that refuses showModal() leaves the layer open in place
    /// rather than closed and invisible.</summary>
    [Fact]
    public void A_refused_show_modal_leaves_the_layer_open_in_place()
    {
        var result = Run("""
            layer.open = true;
            layer.showModal = () => { calls.push('showModal'); throw new Error('InvalidStateError'); };
            window.backlogShowModalLayer('layer');
            console.log(`${calls.join(',')} open=${layer.open}`);
            """);

        Assert.Equal("close,showModal open=true", result);
    }

    /// <summary>The tray is raised after the modal opens, so it lands above it in
    /// the top layer: hidden first when already shown, because only a fresh
    /// showPopover() moves it to the top.</summary>
    [Fact]
    public void Opening_a_modal_raises_the_toast_tray_above_it()
    {
        var result = Run("""
            tray.popoverOpen = true;
            trays.push(tray);
            window.backlogShowModalLayer('layer');
            console.log(`${calls.join(',')} popover=${tray.attributes.popover}`);
            """);

        Assert.Equal("showModal,hidePopover,showPopover popover=manual", result);
    }

    /// <summary>With the last modal gone the tray leaves the top layer, so it is
    /// placed by the page again rather than by its popover state.</summary>
    [Fact]
    public void Closing_the_last_modal_returns_the_tray_to_the_page()
    {
        var result = Run("""
            trays.push(tray);
            window.backlogShowModalLayer('layer');
            present = false;
            window.backlogCloseModalLayer('layer');
            console.log(`${calls.join(',')} popover=${tray.attributes.popover ?? 'none'} open=${tray.popoverOpen}`);
            """);

        Assert.Equal("showModal,showPopover,hidePopover popover=none open=false", result);
    }

    /// <summary>A confirm closing over a still-open dialog leaves the tray where
    /// it is: the dialog below is still in the top layer.</summary>
    [Fact]
    public void Closing_a_modal_over_another_keeps_the_tray_raised()
    {
        var result = Run("""
            trays.push(tray);
            window.backlogShowModalLayer('layer');
            otherModalOpen = true;
            present = false;
            window.backlogCloseModalLayer('layer');
            console.log(`${calls.join(',')} popover=${tray.attributes.popover ?? 'none'} open=${tray.popoverOpen}`);
            """);

        Assert.Equal("showModal,showPopover popover=manual open=true", result);
    }

    [Fact]
    public void No_tray_or_no_popover_support_is_not_an_error()
    {
        var result = Run("""
            window.backlogShowModalLayer('layer');
            const old = { attributes: {}, hasAttribute: n => false, setAttribute() { throw new Error('touched'); } };
            trays.push(old);
            window.backlogCloseModalLayer('layer');
            layer.open = false;
            window.backlogShowModalLayer('layer');
            console.log(calls.join(','));
            """);

        Assert.Equal("showModal,close,showModal", result);
    }

    [Fact]
    public void Closing_tolerates_a_layer_already_gone()
    {
        var result = Run("""
            window.backlogShowModalLayer('layer');
            present = false;
            window.backlogCloseModalLayer('layer');
            window.backlogCloseModalLayer('layer');
            console.log(calls.join(','));
            """);

        Assert.Equal("showModal", result);
    }

    private static string Run(string body)
    {
        var harness = $$"""
            const calls = [];
            let present = true;
            let otherModalOpen = false;
            const trays = [];
            class HTMLDialogElement {}
            class HTMLElement {}
            const listeners = {};
            const layer = Object.assign(new HTMLDialogElement(), {
                open: false,
                isConnected: true,
                addEventListener(type, fn) { (listeners[type] ??= []).push(fn); },
                removeEventListener(type, fn) { listeners[type] = (listeners[type] ?? []).filter(f => f !== fn); },
                showModal() { if (this.open) throw new Error('InvalidStateError'); calls.push('showModal'); this.open = true; },
                close() { calls.push('close'); this.open = false; },
                setAttribute(name) { if (name === 'open') this.open = true; },
                dispatch(type, init) {
                    const event = { ...init, type, defaultPrevented: false, stopped: false,
                        preventDefault() { this.defaultPrevented = true; },
                        stopPropagation() { this.stopped = true; },
                        stopImmediatePropagation() { this.stopped = true; } };
                    (listeners[type] ?? []).forEach(fn => fn(event));
                    return event;
                }
            });
            const tray = {
                attributes: {},
                popoverOpen: false,
                hasAttribute(name) { return name in this.attributes; },
                setAttribute(name, value) { this.attributes[name] = value; },
                removeAttribute(name) { delete this.attributes[name]; },
                matches(selector) { return selector === ':popover-open' && this.popoverOpen; },
                hidePopover() { calls.push('hidePopover'); this.popoverOpen = false; },
                showPopover() { calls.push('showPopover'); this.popoverOpen = true; }
            };
            const window = {};
            const document = {
                getElementById: id => id === 'layer' && present ? layer : null,
                querySelectorAll: selector => selector === '[data-backlog-top-layer]' ? trays : [],
                querySelector: selector => selector !== '.modal-layer:modal' ? null
                    : otherModalOpen ? {} : present && layer.open ? layer : null
            };

            {{LayerFunctions()}}

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

    /// <summary>The modal-layer functions cut out of the script as it ships, so the
    /// functions under test are the ones the app loads.</summary>
    private static string LayerFunctions()
    {
        var script = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        const string from = "    const backlogModalLayers = ";
        const string to = "        Whether the focus has landed outside every one";

        var begin = script.IndexOf(from, StringComparison.Ordinal);
        Assert.True(begin >= 0, "components.js no longer declares the modal layers map.");

        var end = script.IndexOf(to, begin, StringComparison.Ordinal);
        Assert.True(end > begin, "backlogFocusOutside no longer follows the modal layer functions in components.js.");

        // Cut back to the comment opener above backlogFocusOutside.
        end = script.LastIndexOf("/*", end, StringComparison.Ordinal);

        return script[begin..end];
    }
}
