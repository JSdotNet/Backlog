using System.Diagnostics;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <c>window.backlogFocusWithin</c>, run in Node against a stand-in document, cut out of
/// <c>components.js</c> as it ships.
/// <para>
/// A field group that saves when the focus leaves it asks whether the focus is still
/// inside, rather than whether it went somewhere else: a click on a spot nothing can
/// focus puts the focus on the body, and for a group that is leaving.
/// <c>backlogFocusOutside</c> answers that case false on purpose, for a region a
/// reader is still using, so the two questions get two helpers.
/// </para>
/// </summary>
public sealed class FocusWithinScriptTests
{
    [Fact]
    public void The_focus_is_within_a_group_only_while_an_element_inside_it_holds_it()
    {
        var result = Run("""
            const inside = [];
            document.activeElement = field;
            inside.push(window.backlogFocusWithin('#group'));
            document.activeElement = other;
            inside.push(window.backlogFocusWithin('#group'));
            document.activeElement = document.body;
            inside.push(window.backlogFocusWithin('#group'));
            document.activeElement = null;
            inside.push(window.backlogFocusWithin('#group'));
            document.activeElement = field;
            inside.push(window.backlogFocusWithin('#gone'));
            console.log(inside.join(' '));
            """);

        Assert.Equal("true false false false false", result);
    }

    private static string Run(string body)
    {
        var harness = $$"""
            const window = {};
            const group = { id: 'group', contains(node) { return node === this || node === field; } };
            const field = { id: 'field' };
            const other = { id: 'other' };
            const document = {
                body: { id: 'body' },
                documentElement: { id: 'html' },
                activeElement: null,
                querySelector(selector) { return selector === '#group' ? group : null; },
                querySelectorAll(selector) { return selector === '#group' ? [group] : []; }
            };

            {{ComponentsScript.Region("    window.backlogFocusWithin = ", "    window.backlogFocusOutside = ")}}

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
