using System.Diagnostics.CodeAnalysis;

using Backlog.Desktop.UI.Shell;

using Microsoft.Extensions.Logging;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What activating a <c>backlog://</c> link does in the desktop app (local ADR 0041
/// §7): a window on the surface it names once the app has started, and nothing but a
/// warning for a link that names no surface on offer, <c>proposals</c> included.
/// </summary>
public sealed class BacklogLinkActivationTests
{
    private sealed class Component;

    private readonly RecordingShellWindows _windows = new();
    private readonly RecordingLogger _logger = new();

    private BacklogLinkActivation Activation(params string[] offered) =>
        new(_windows, new Catalog(offered), _logger);

    [Fact]
    public void A_link_activated_after_start_opens_a_window_on_its_surface_with_its_query()
    {
        var activation = Activation("tasks");
        activation.Started();

        activation.Activate("backlog://window/tasks?plan=shell-pop-out-windows&entry=6742b6bf");

        var opened = Assert.Single(_windows.Opened);
        Assert.Equal("tasks", opened.SurfaceId);
        Assert.Equal("shell-pop-out-windows", opened.Query["plan"]);
        Assert.Equal("6742b6bf", opened.Query["entry"]);
        Assert.Empty(_logger.Warnings);
    }

    [Fact]
    public void A_link_activated_before_start_waits_and_opens_once_the_app_has_started()
    {
        var activation = Activation("devbook", "roadmap");

        activation.Activate("backlog://window/devbook?chapter=a.md");
        activation.Activate("backlog://window/roadmap");
        Assert.Empty(_windows.Opened);

        activation.Started();

        Assert.Equal(["devbook", "roadmap"], _windows.Opened.Select(opened => opened.SurfaceId));
    }

    [Fact]
    public void Starting_twice_opens_a_waiting_link_once()
    {
        var activation = Activation("tasks");
        activation.Activate("backlog://window/tasks");

        activation.Started();
        activation.Started();

        Assert.Single(_windows.Opened);
    }

    [Fact]
    public void Every_activation_opens_another_window()
    {
        var activation = Activation("tasks");
        activation.Started();

        activation.Activate("backlog://window/tasks?plan=a");
        activation.Activate("backlog://window/tasks?plan=a");

        Assert.Equal(2, _windows.Opened.Count);
    }

    [Theory]
    [InlineData("backlog://window/no-such-surface")]
    [InlineData("backlog://window/proposals")]
    [InlineData("backlog://window/Tasks")]
    public void A_surface_the_catalog_does_not_offer_opens_nothing_and_logs_a_warning(string link)
    {
        var activation = Activation("tasks");
        activation.Started();

        activation.Activate(link);

        Assert.Empty(_windows.Opened);
        var warning = Assert.Single(_logger.Warnings);
        Assert.Contains(BacklogWindowLink.Parse(link)!.SurfaceId, warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("backlog://devbook/JSdotNet-Backlog/a.md")]
    [InlineData("https://example.com/")]
    [InlineData("")]
    public void Text_that_is_no_window_link_opens_nothing_and_logs_a_warning(string link)
    {
        var activation = Activation("tasks");
        activation.Started();

        activation.Activate(link);

        Assert.Empty(_windows.Opened);
        Assert.Single(_logger.Warnings);
    }

    [Fact]
    public void A_waiting_link_whose_surface_is_not_offered_warns_at_start_and_the_rest_still_open()
    {
        var activation = Activation("tasks");
        activation.Activate("backlog://window/proposals");
        activation.Activate("backlog://window/tasks");

        activation.Started();

        Assert.Equal("tasks", Assert.Single(_windows.Opened).SurfaceId);
        Assert.Single(_logger.Warnings);
    }

    private sealed class Catalog(string[] offered) : ISurfaceCatalog
    {
        public IReadOnlyList<ShellSurface> Surfaces =>
            [.. offered.Select((id, index) => new ShellSurface(id, id, ShellSurfaceGroup.View, index, typeof(Component)))];

        public bool TryGet(string surfaceId, [NotNullWhen(true)] out ShellSurface? surface)
        {
            surface = Surfaces.FirstOrDefault(candidate => string.Equals(candidate.Id, surfaceId, StringComparison.Ordinal));
            return surface is not null;
        }
    }

    private sealed class RecordingShellWindows : IShellWindows
    {
        public List<(string SurfaceId, IReadOnlyDictionary<string, string> Query)> Opened { get; } = [];

        public void Open(string surfaceId, IReadOnlyDictionary<string, string>? query = null) =>
            Opened.Add((surfaceId, query ?? new Dictionary<string, string>()));

        public void Close(string windowKey) { }

        public void SetTitle(string windowKey, string title) { }

        public void ActivateMainWindow() { }
    }

    private sealed class RecordingLogger : ILogger<BacklogLinkActivation>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }
}
