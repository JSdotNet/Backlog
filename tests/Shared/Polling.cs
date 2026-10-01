using System.Diagnostics;

namespace Backlog.Tests;

/// <summary>
/// Waiting for something a test cannot await directly — a debounced save, a
/// background write — to become true.
///
/// <para>Each file that needed this used to write its own loop around
/// <c>Task.Delay(50)</c> and a wall-clock deadline. Sitting in a private helper,
/// the delay never got the running test's cancellation token and the analyzer
/// that asks for one could not see it, so a cancelled run sat out the whole
/// deadline. This one passes the token.</para>
///
/// <para>Linked into every test project by <c>tests/Directory.Build.props</c>;
/// it depends on nothing but xUnit.</para>
/// </summary>
internal static class Polling
{
    private static readonly TimeSpan DefaultPatience = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Returns once <paramref name="condition"/> answers true, and fails the test
    /// if it has not within <paramref name="within"/> (ten seconds by default).
    /// </summary>
    /// <param name="condition">Asked again every fifty milliseconds.</param>
    /// <param name="because">The failure message; read only when the wait
    /// times out, so it can describe the state the condition last saw.</param>
    /// <param name="within">How long to keep asking.</param>
    public static async Task WaitUntilAsync(Func<Task<bool>> condition, Func<string>? because = null, TimeSpan? within = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var patience = within ?? DefaultPatience;
        var elapsed = Stopwatch.StartNew();

        while (!await condition())
        {
            Assert.True(elapsed.Elapsed < patience, because?.Invoke() ?? $"The condition did not hold within {patience}.");
            await Task.Delay(Interval, cancellationToken);
        }
    }
}
