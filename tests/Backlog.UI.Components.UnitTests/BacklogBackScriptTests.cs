using System.Diagnostics;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <c>window.backlogBack</c>, the page's half of the phone's Back key, run rather than read.
/// <para>
/// A sheet or a dialog opens without a navigation, so Android's Back walked the WebView's
/// history from under it: a capture open on the Inbox, Back, and the phone was on the tab
/// before. The Android head now asks this first. It hands the topmost open layer Escape —
/// the key each one already closes on — and answers true, or answers false with nothing
/// open so Back walks the history as it always did. bUnit has no document to query, so
/// these run the script in Node against a stand-in one.
/// </para>
/// </summary>
public sealed class BacklogBackScriptTests
{
    [Fact]
    public void With_nothing_open_back_is_left_to_the_history()
    {
        var result = Run("""
            console.log(`${window.backlogBack()} ${heard.join(',') || 'none'}`);
            """);

        Assert.Equal("false none", result);
    }

    /// <summary>The bug: a capture sheet open on the Inbox, the focus left on the row that
    /// opened it. The sheet hears Escape and Back is consumed.</summary>
    [Fact]
    public void An_open_sheet_is_handed_escape_and_back_is_consumed()
    {
        var result = Run("""
            sheet.open = true;
            console.log(`${window.backlogBack()} ${heard.join(',')}`);
            """);

        Assert.Equal("true sheet:Escape", result);
    }

    /// <summary>A dialog opened over the sheet is the one Back closes. A <c>Modal</c>'s keys
    /// are held by the panel inside its native dialog, so that panel is what hears it.</summary>
    [Fact]
    public void A_dialog_over_the_sheet_is_closed_first_through_its_keyed_panel()
    {
        var result = Run("""
            sheet.open = true;
            confirm.open = true;
            console.log(`${window.backlogBack()} ${heard.join(',')}`);
            """);

        Assert.Equal("true confirm-panel:Escape", result);
    }

    /// <summary>A native dialog sits in the top layer, above every sheet, wherever the
    /// document puts it — here before the sheet — so it is the one Back closes.</summary>
    [Fact]
    public void An_open_native_dialog_comes_before_a_sheet_later_in_the_document()
    {
        var result = Run("""
            confirm.open = true;
            sheet.open = true;
            console.log(`${window.backlogBack()} ${heard.join(',')}`);
            """);

        Assert.Equal("true confirm-panel:Escape", result);
    }

    /// <summary>A layer under an inert ancestor, or hidden, is not open to the person: the
    /// next one down is handed Escape, and with none Back is left to the history.</summary>
    [Theory]
    [InlineData("confirm.open = true; sheet.open = true; confirm.parent = new HTMLElement('inert-host'); confirm.parent.inert = true;", "true sheet:Escape")]
    [InlineData("confirm.open = true; sheet.open = true; confirm.visibility = 'hidden';", "true sheet:Escape")]
    [InlineData("sheet.open = true; page.inert = true;", "false none")]
    [InlineData("sheet.open = true; sheet.visibility = 'hidden';", "false none")]
    public void A_layer_under_an_inert_ancestor_or_hidden_is_passed_by(string setup, string expected)
    {
        var result = Run($$"""
            {{setup}}
            console.log(`${window.backlogBack()} ${heard.join(',') || 'none'}`);
            """);

        Assert.Equal(expected, result);
    }

    /// <summary>With the focus inside the layer, the key starts where the focus is and
    /// bubbles to the layer's handler, as a pressed Escape would.</summary>
    [Fact]
    public void With_the_focus_inside_the_layer_the_key_starts_at_the_focus()
    {
        var result = Run("""
            sheet.open = true;
            document.activeElement = sheetField;
            console.log(`${window.backlogBack()} ${heard.join(',')}`);
            """);

        Assert.Equal("true sheet-field:Escape", result);
    }

    /// <summary>A layer in the document but not drawn is not open to the person, so Back
    /// passes it by.</summary>
    [Fact]
    public void A_layer_that_is_not_drawn_is_passed_by()
    {
        var result = Run("""
            sheet.open = true;
            sheet.drawn = false;
            console.log(`${window.backlogBack()} ${heard.join(',') || 'none'}`);
            """);

        Assert.Equal("false none", result);
    }

    private static string Run(string body)
    {
        var harness = $$"""
            const window = { addEventListener() {}, removeEventListener() {} };
            const heard = [];

            class HTMLElement {
                constructor(name, parent) {
                    this.name = name; this.parent = parent ?? null; this.open = false; this.drawn = true;
                    this.inert = false; this.visibility = 'visible'; this.panel = null;
                }
                contains(other) { for (let node = other; node; node = node.parent) if (node === this) return true; return false; }
                closest(selector) {
                    if (selector !== '[inert]') throw new Error(`unexpected selector ${selector}`);
                    for (let node = this; node; node = node.parent) if (node.inert) return node;
                    return null;
                }
                getClientRects() { return this.drawn ? [{}] : []; }
                querySelector(selector) { return selector === '[tabindex="-1"]' ? this.panel : null; }
                dispatchEvent(event) { heard.push(`${this.name}:${event.key}`); return true; }
            }
            class HTMLDialogElement extends HTMLElement {}
            const getComputedStyle = element => ({ visibility: element.visibility });
            class KeyboardEvent {
                constructor(type, init) { Object.assign(this, init); this.type = type; }
            }

            const page = new HTMLElement('page');
            const row = new HTMLElement('row', page);
            const confirm = new HTMLDialogElement('confirm', page);
            confirm.panel = new HTMLElement('confirm-panel', confirm);
            const sheet = new HTMLElement('sheet', page);
            const sheetField = new HTMLElement('sheet-field', sheet);

            // Document order puts the sheet after the dialog, as a host that renders its
            // confirmation first would: the dialog is still the one on top, in the top layer.
            const document = {
                activeElement: row,
                querySelectorAll(selector) {
                    if (selector === 'dialog[open]') return [confirm].filter(layer => layer.open);
                    if (selector === '[role="dialog"][aria-modal="true"]') return [sheet].filter(layer => layer.open);
                    throw new Error(`unexpected selector ${selector}`);
                }
            };

            {{ComponentsScript.Region("    function backlogIsDrawn(element) {", "    window.backlogFocusTrap = (id, restoreToId, ownsEscape) => {")}}

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
}
