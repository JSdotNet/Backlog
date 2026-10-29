using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.UI;
using Backlog.Modules.Inbox.UI;
using Backlog.UI.Components.Feedback;
using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The Inbox's Capture, answered for one window (local ADR 0041 §1, §5): one run over
/// the sources switched on in Settings, reported back as a sentence the Inbox can print,
/// and the Capture context's own sources panel for the Inbox's Sources slot. The Inbox
/// surface reads both through <see cref="InboxSurfaceWiring"/>, so the Inbox stays free
/// of Capture's types while its button and its switches sit together on one pane.
/// <para>
/// It moved out of <c>Home</c> when the Inbox became a surface a window of its own can
/// show. Held here rather than in the surface for the reason the Inbox's own view state
/// lives in <see cref="InboxDesktopState"/>: the surface re-mounts each time it is shown,
/// and a run's result dropping at that moment would leave the reader with a button that
/// did nothing. One per window, with the window's view state.
/// </para>
/// </summary>
public sealed class ShellInboxCapture
{
    /// <summary>Where a failed re-read of the Inbox after a capture is reported.</summary>
    public const string InboxReloadErrorTestId = "inbox-reload-error";

    private readonly ICaptureRunner _runner;
    private readonly InboxDesktopState _inbox;
    private readonly IToastChannel? _toasts;

    public ShellInboxCapture(ICaptureRunner runner, InboxDesktopState inbox, IToastChannel? toasts)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(inbox);

        _runner = runner;
        _inbox = inbox;
        _toasts = toasts;
        Sources = builder =>
        {
            builder.OpenComponent<CaptureSourcesPanel>(0);
            builder.AddComponentParameter(
                1,
                nameof(CaptureSourcesPanel.OnImported),
                EventCallback.Factory.Create<CaptureRunResultDto>(this, ImportedAsync));
            builder.CloseComponent();
        };
    }

    /// <summary>Whether a capture run is in flight.</summary>
    public bool Running { get; private set; }

    /// <summary>What the last run said; null before the first.</summary>
    public string? Message { get; private set; }

    /// <summary>The Capture context's sources panel, made once.</summary>
    public RenderFragment Sources { get; }

    /// <summary>The window's wiring for the Inbox surface, over this capture.</summary>
    public InboxSurfaceWiring Wiring() => new()
    {
        Capture = RunAsync,
        CaptureRunning = () => Running,
        CaptureMessage = () => Message,
        Sources = Sources,
    };

    /// <summary>
    /// One run, and — when it delivered anything — the Inbox re-read so the new rows are
    /// on screen without a restart, the way a sync pull re-reads it. The runner never
    /// throws for a source, so a throw here is the run itself failing to start, and that
    /// is reported in the same line. The re-read is kept out of that try: its failure is
    /// the Inbox's to report, not the run's.
    /// </summary>
    public async Task RunAsync()
    {
        if (Running) return;

        Running = true;
        Message = null;
        CaptureRunResultDto? run = null;

        try
        {
            run = await _runner.RunAsync();
            Message = Describe(run);
        }
        catch (Exception ex)
        {
            Message = $"Capture could not run: {ex.Message}";
        }
        finally
        {
            Running = false;
        }

        if (run is { TotalNewItems: > 0 }) await ReloadInboxAsync();
    }

    /// <summary>The sources panel's Import file…, answered the way a run is.</summary>
    private async Task ImportedAsync(CaptureRunResultDto run)
    {
        if (run.TotalNewItems > 0) await ReloadInboxAsync();
    }

    /// <summary>Before the Inbox has loaded at all, its first load reads whatever the
    /// run delivered, so there is nothing to re-read.</summary>
    private async Task ReloadInboxAsync()
    {
        if (!_inbox.Loaded) return;

        try
        {
            await _inbox.ReloadAsync();
        }
        catch (Exception ex)
        {
            _toasts?.Publish(ToastMessage.Error($"The Inbox could not be refreshed after a capture: {ex.Message}", InboxReloadErrorTestId));
        }
    }

    /// <summary>A run's result in one line: nothing enabled points at the Sources
    /// button; otherwise each source's own sentence and, for more than one, the total.</summary>
    internal static string Describe(CaptureRunResultDto run)
    {
        if (run.NothingEnabled)
        {
            return "No capture sources are enabled. Press Sources in the Inbox header and switch one on.";
        }

        var sources = string.Join(" ", run.Sources.Select(source => source.Message));

        if (run.Sources.Count == 1) return sources;

        var total = run.TotalNewItems == 1 ? "1 new item in all." : $"{run.TotalNewItems} new items in all.";

        return $"{sources} {total}";
    }
}
