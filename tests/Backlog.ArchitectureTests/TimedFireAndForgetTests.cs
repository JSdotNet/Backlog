namespace Backlog.ArchitectureTests;

/// <summary>
/// A screen that waits before undoing something it just did — a confirmation
/// that fades, a badge that stops flashing, a debounce that saves — starts a
/// delay nobody awaits. If that delay takes no cancellation token, nothing can
/// stop it: closing the pane, switching workspace, or shutting the host down all
/// leave it running, and it comes back to touch state that is gone.
///
/// <para>In the app that is an exception the user never sees. In the test host it
/// is work still queued on the runner's own threads after the assembly has
/// finished, which is how the desktop UI suite came to sit out xUnit's
/// foreground-thread grace period and exit 1 on a fully green run — issue
/// #211.</para>
///
/// <para>The rule is a source scan because the shape is the defect: whether the
/// token is honoured is a unit test's business, but whether one was asked for at
/// all can be read off the call. <c>Toast</c> and <c>CopyButton</c> in the shared
/// component library are what compliance looks like.</para>
/// </summary>
public class TimedFireAndForgetTests
{
    [Fact]
    public void Every_timed_wait_in_the_user_interface_can_be_cancelled()
    {
        var delays = Delays().ToList();

        Assert.NotEmpty(delays);

        var offenders = delays
            .Where(delay => !IsCancellable(delay))
            .Select(delay => $"{delay.File}:{delay.Line}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These waits take no cancellation token, so nothing that owns them can stop them — a disposed "
            + "screen or a shut-down host leaves each one running and it returns to state that no longer "
            + "exists. Hold a CancellationTokenSource, pass its token, and cancel it on disposal, the way "
            + "src/Core/Backlog.UI.Components/Feedback/Toast.razor does:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>Reads the argument list rather than looking for the word
    /// "token": a delay is cancellable when it was handed something to cancel
    /// it with, and that is a second argument.</summary>
    private static bool IsCancellable(Delay delay) => delay.Arguments.Contains(',', StringComparison.Ordinal);

    private static IEnumerable<Delay> Delays()
    {
        foreach (var source in Sources())
        {
            var lines = File.ReadAllLines(source.FullName);

            for (var index = 0; index < lines.Length; index++)
            {
                var start = lines[index].IndexOf("Task.Delay(", StringComparison.Ordinal);
                if (start < 0) continue;

                yield return new Delay(Relative(source), index + 1, Arguments(lines[index], start + "Task.Delay(".Length));
            }
        }
    }

    /// <summary>The text between the call's parentheses. Every wait in this
    /// repository is written on one line, so a depth count over that line is
    /// enough to find the closing one.</summary>
    private static string Arguments(string line, int start)
    {
        var depth = 1;

        for (var index = start; index < line.Length; index++)
        {
            depth += line[index] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0) return line[start..index];
        }

        return line[start..];
    }

    /// <summary>
    /// The screens the app ships: the shell and its heads, each context's UI
    /// project, and the shared component library they all render.
    ///
    /// <para><c>src/Harness</c> is deliberately absent. The storybook exists to
    /// be poked at by hand, its fake latency is the exhibit, and nothing there
    /// outlives a browser tab.</para>
    /// </summary>
    private static IEnumerable<FileInfo> Sources() =>
        Repository.UserInterfaceFolders()
            .Append(new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src", "Core", "Backlog.UI.Components")))
            .Where(folder => folder.Exists)
            .SelectMany(folder => folder.EnumerateFiles("*.*", SearchOption.AllDirectories))
            .Where(file => file.Extension is ".cs" or ".razor")
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .DistinctBy(file => file.FullName);

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/');

    private sealed record Delay(string File, int Line, string Arguments);
}
