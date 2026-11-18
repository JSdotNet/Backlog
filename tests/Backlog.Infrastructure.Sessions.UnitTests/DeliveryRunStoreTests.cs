using System.Text.Json.Nodes;
using Backlog.Infrastructure.Sessions;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// The run store's write replacing a run file that a reader outside the per-run gate holds
/// open for a moment — the pane's reader, the telemetry's first lookup, another process.
/// On Windows the replace is refused while any handle is open, so a write that gave up at
/// the first refusal lost the run's update and failed the caller.
/// </summary>
public sealed class DeliveryRunStoreTests : IDisposable
{
    private const string Worktree = @"D:\n\5c1e0b7a";
    private const string RunId = "run-1";

    private readonly string _home = Path.Combine(Path.GetTempPath(), "backlog-delivery-run-store-tests", Guid.NewGuid().ToString("n"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_write_that_meets_the_run_file_held_open_by_a_reader_lands_once_the_reader_lets_go()
    {
        var store = new DeliveryRunStore(_home);
        await store.WriteAsync(Worktree, RunId, Run("before"), Cancellation);
        var path = store.PathFor(Worktree, RunId);

        Task write;

        // File.ReadAllText's own sharing: other readers allowed, a replace refused.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            write = store.WriteAsync(Worktree, RunId, Run("after"), Cancellation);
            await Task.Delay(150, Cancellation);
        }

        await write;

        Assert.Equal("after", (string?)(await store.ReadAsync(Worktree, RunId, Cancellation))?["title"]);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task A_run_file_held_past_the_replace_window_fails_the_write_and_leaves_no_temp_file()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Only Windows refuses to replace a file another handle holds open.");

        var store = new DeliveryRunStore(_home, replaceWindow: TimeSpan.FromMilliseconds(100));
        await store.WriteAsync(Worktree, RunId, Run("before"), Cancellation);
        var path = store.PathFor(Worktree, RunId);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.WriteAsync(Worktree, RunId, Run("after"), Cancellation));
        }

        Assert.Equal("before", (string?)(await store.ReadAsync(Worktree, RunId, Cancellation))?["title"]);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    private static JsonObject Run(string title) => new() { ["id"] = RunId, ["title"] = title };

    public void Dispose()
    {
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
