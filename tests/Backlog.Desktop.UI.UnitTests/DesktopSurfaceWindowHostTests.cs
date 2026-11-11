using Backlog.Desktop.UI.Mcp;
using Backlog.Desktop.UI.Shell;

using Microsoft.Extensions.Logging;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What <c>open_window</c> reaches in the desktop app (local ADR 0043 §7): one more of
/// the head's windows, and a link to the listener's page carrying a token the listener
/// redeems once.
/// </summary>
public sealed class DesktopSurfaceWindowHostTests
{
    private static readonly IReadOnlyDictionary<string, string> PlanQuery =
        new Dictionary<string, string> { ["plan"] = "shell-pop-out-windows" };

    [Fact]
    public async Task A_native_window_is_one_more_of_the_heads_windows()
    {
        var windows = new RecordingShellWindows();
        var host = new DesktopSurfaceWindowHost(windows, new WindowLinkTokens(), () => 5757);

        Assert.True(await host.TryOpenWindowAsync("tasks", PlanQuery));

        var (surfaceId, query) = Assert.Single(windows.Opened);
        Assert.Equal("tasks", surfaceId);
        Assert.Same(PlanQuery, query);
    }

    /// <summary>
    /// The head's answer is the tool's: a window that could not be built — no
    /// application to open it in, or a page that threw — reads as not opened, and the
    /// answer waits for the head rather than saying "opened" on the way in.
    /// </summary>
    [Fact]
    public async Task A_window_the_head_could_not_build_reads_as_not_opened_and_the_answer_waits_for_it()
    {
        var built = new TaskCompletionSource<bool>();
        var windows = new RecordingShellWindows(built.Task);
        var host = new DesktopSurfaceWindowHost(windows, new WindowLinkTokens(), () => 5757);

        var answer = host.TryOpenWindowAsync("tasks", PlanQuery);

        Assert.False(answer.IsCompleted);
        Assert.Single(windows.Opened);

        built.SetResult(false);
        Assert.False(await answer);
    }

    [Fact]
    public void A_web_link_names_the_listeners_port_and_a_token_it_redeems_once()
    {
        var tokens = new WindowLinkTokens();
        var windows = new RecordingShellWindows();
        var host = new DesktopSurfaceWindowHost(windows, tokens, () => 49213);

        var link = host.WebLink("tasks", PlanQuery, "test-client");

        Assert.NotNull(link);
        Assert.Equal("localhost", link.Host);
        Assert.Equal(49213, link.Port);
        Assert.Equal("/window/tasks", link.AbsolutePath);
        Assert.StartsWith("?plan=shell-pop-out-windows&t=", link.Query, StringComparison.Ordinal);
        Assert.Empty(windows.Opened);

        var token = SurfaceWindowRoute.QueryOf(link.AbsoluteUri)[WindowLinkTokens.QueryKey];
        Assert.NotNull(tokens.Redeem(token));
        Assert.Null(tokens.Redeem(token));
    }

    [Fact]
    public void Every_link_carries_a_token_of_its_own()
    {
        var host = new DesktopSurfaceWindowHost(new RecordingShellWindows(), new WindowLinkTokens(), () => 5757);

        var first = host.WebLink("devbook", new Dictionary<string, string>(), "test-client");
        var second = host.WebLink("devbook", new Dictionary<string, string>(), "test-client");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void No_link_while_the_listener_is_not_listening()
    {
        var log = new RecordingLogger();
        var host = new DesktopSurfaceWindowHost(new RecordingShellWindows(), new WindowLinkTokens(), () => null, log);

        Assert.Null(host.WebLink("tasks", PlanQuery, "test-client"));
        Assert.Empty(log.Entries);
    }

    /// <summary>
    /// Every link minted is logged (local ADR 0052 §4) with the surface, the caller and
    /// the moment its token stops redeeming — and never the token, nor the link that
    /// carries it, since the log is a file anyone reading it later could replay.
    /// </summary>
    [Fact]
    public void Every_minted_link_is_logged_with_its_surface_caller_and_expiry_and_never_its_token()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 11, 9, 30, 0, TimeSpan.Zero));
        var log = new RecordingLogger();
        var host = new DesktopSurfaceWindowHost(new RecordingShellWindows(), new WindowLinkTokens(clock), () => 5757, log);

        var link = host.WebLink("tasks", PlanQuery, "claude-code 2.1.0");
        Assert.NotNull(link);
        var token = SurfaceWindowRoute.QueryOf(link.AbsoluteUri)[WindowLinkTokens.QueryKey];

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("tasks", entry.Values["SurfaceId"]);
        Assert.Equal("claude-code 2.1.0", entry.Values["Caller"]);
        Assert.Equal(clock.GetUtcNow() + WindowLinkTokens.Lifetime, entry.Values["Expires"]);
        Assert.Contains("tasks", entry.Message, StringComparison.Ordinal);
        Assert.Contains("claude-code 2.1.0", entry.Message, StringComparison.Ordinal);
        Assert.Contains("2026-11-11T09:40:00", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(token, entry.Message, StringComparison.Ordinal);
        Assert.All(entry.Values.Values, value => Assert.DoesNotContain(token, value?.ToString() ?? string.Empty, StringComparison.Ordinal));

        host.WebLink("devbook", new Dictionary<string, string>(), "another-client");
        Assert.Equal(2, log.Entries.Count);
        Assert.Equal("devbook", log.Entries[1].Values["SurfaceId"]);
    }

    private sealed record LogEntry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Values);

    private sealed class RecordingLogger : ILogger<DesktopSurfaceWindowHost>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), values));
        }
    }

    [Fact]
    public void A_token_already_in_the_query_is_replaced_by_the_minted_one()
    {
        var tokens = new WindowLinkTokens();
        var host = new DesktopSurfaceWindowHost(new RecordingShellWindows(), tokens, () => 5757);

        var link = host.WebLink("tasks", new Dictionary<string, string> { [WindowLinkTokens.QueryKey] = "forged" }, "test-client");

        Assert.NotNull(link);
        Assert.Single(link.Query.Split('&'), part => part.StartsWith("t=", StringComparison.Ordinal) || part.StartsWith("?t=", StringComparison.Ordinal));
        Assert.DoesNotContain("forged", link.Query, StringComparison.Ordinal);
        Assert.NotNull(tokens.Redeem(SurfaceWindowRoute.QueryOf(link.AbsoluteUri)[WindowLinkTokens.QueryKey]));
    }

    /// <summary>Records every window asked for, and answers each with
    /// <paramref name="answer"/> — built, unless a test says otherwise.</summary>
    private sealed class RecordingShellWindows(Task<bool>? answer = null) : IShellWindows
    {
        public List<(string SurfaceId, IReadOnlyDictionary<string, string>? Query)> Opened { get; } = [];

        public void Open(string surfaceId, IReadOnlyDictionary<string, string>? query = null) =>
            throw new NotSupportedException("open_window awaits OpenAsync; it never opens fire and forget.");

        public Task<bool> OpenAsync(string surfaceId, IReadOnlyDictionary<string, string>? query = null)
        {
            Opened.Add((surfaceId, query));
            return answer ?? Task.FromResult(true);
        }

        public void Close(string windowKey)
        {
        }

        public void SetTitle(string windowKey, string title)
        {
        }

        public void ActivateMainWindow()
        {
        }
    }
}
