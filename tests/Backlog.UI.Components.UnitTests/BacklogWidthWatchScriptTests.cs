using System.Diagnostics;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <c>window.backlogWidthWatch</c>, run rather than read.
/// <para>
/// The Tasks pane folds Source and Type into one Filter menu, and moves the
/// open-work summary beside New entry, on this script's word that the list column
/// is at or under its 48rem step. A word said on every resize would re-render the
/// pane once per frame of a drag, and a word said for a hidden column would fold a
/// bar nobody can see; these run the script's own function in Node against a fake
/// observer whose widths are given by hand.
/// </para>
/// </summary>
public sealed class BacklogWidthWatchScriptTests
{
    /// <summary>Only a crossing is reported: the first width says which side the
    /// column starts on, and widths on the same side say nothing more.</summary>
    [Fact]
    public void Only_a_crossing_of_the_threshold_is_reported()
    {
        // 48rem at 16px is 768px.
        Assert.Equal("false; true; false", Run("1000, 900, 768, 700, 769, 1200"));
    }

    /// <summary>A column of no width is not on screen — a takeover hid it — so it
    /// says nothing either way, and the pane keeps the shape it had.</summary>
    [Fact]
    public void A_hidden_column_reports_nothing()
    {
        Assert.Equal("false", Run("1000, 0, 0, 1000"));
    }

    [Fact]
    public void Unwatching_disconnects_the_observer()
    {
        Assert.Equal("true; disconnected", Run("500", unwatch: true));
    }

    private static string Run(string widths, bool unwatch = false)
    {
        var harness = $$"""
            const said = [];
            let observer = null;
            class ResizeObserver {
                constructor(callback) { this.callback = callback; observer = this; }
                observe() { }
                disconnect() { said.push('disconnected'); }
            }
            const window = {};
            const backlogRootFontSize = () => 16;
            const dotnet = { invokeMethodAsync: (method, narrow) => { said.push(method === 'ListNarrowChangedAsync' ? String(narrow) : 'wrong method'); return Promise.resolve(); } };

            {{Cut("    const backlogWidthWatches = new Map();", "    // A window drag fires resize many times")}}

            window.backlogWidthWatch.watch('list', {}, dotnet, 'ListNarrowChangedAsync', 48);
            for (const width of [{{widths}}]) observer.callback([{ contentRect: { width } }]);
            if ({{(unwatch ? "true" : "false")}}) window.backlogWidthWatch.unwatch('list');

            console.log(said.join('; '));
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
