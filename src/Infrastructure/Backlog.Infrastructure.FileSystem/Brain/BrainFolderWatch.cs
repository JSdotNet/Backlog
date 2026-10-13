using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Devbook.Abstractions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Keeps the Brain's folder watch and its open panels in step with the Brain
/// setting (local ADR 0026 §4, local ADR 0015).
///
/// <para>The setting is the person's, the folder source reads it on every
/// resolve, and the refresher watches whichever folder it is handed — none of
/// the three knows of the others. This is the one place that does: once
/// <see cref="Start"/>ed it watches the folder the setting names
/// (<see cref="DevbookDatabaseRefresher.WatchBrain"/>); when the setting moves it
/// stops that watch and starts one on the new folder, or none when the setting
/// was cleared, and tells open panels through
/// <see cref="IDevbookFolderSource.NotifyContentChanged"/>; and when the Brain's
/// database was rebuilt it tells them again, so a pane showing what the build
/// reads catches up.</para>
///
/// <para>A folder that is set but not there — a drive not mounted yet, a sync
/// client not signed in — cannot be watched, so it is looked for again every
/// <see cref="DefaultMissingFolderRetry"/> until it appears, and then watched
/// and announced as if the setting had just named it.</para>
///
/// <para>Telling the panels is news, never a step of the work that caused it: a
/// panel that throws on it is logged here, so it can neither fail the settings
/// save that moved the folder nor be reported as a failed database build.</para>
///
/// <para>The panels read the Markdown, not the database (local ADR 0004), so
/// nothing waits on the watch: it only keeps the database current for the
/// readers that need one. Every event this hears arrives off the UI thread; a
/// panel that reloads on <see cref="IDevbookFolderSource.Changed"/> marshals
/// itself.</para>
///
/// <para>Each head resolves it and calls <see cref="Start"/> at startup, after
/// the refresher, so the Brain is watched from the first minute rather than from
/// the first time somebody opens the pane.</para>
/// </summary>
public sealed class BrainFolderWatch : IDisposable
{
    /// <summary>How often a set but missing Brain folder is looked for.</summary>
    public static readonly TimeSpan DefaultMissingFolderRetry = TimeSpan.FromSeconds(30);

    private readonly IBrainSettings _settings;
    private readonly IBrainFolderSource _brain;
    private readonly DevbookDatabaseRefresher _refresher;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly TimeSpan _missingFolderRetry;
    private readonly Lock _gate = new();

    private bool _started;
    private bool _disposed;
    private string? _root;
    private IDisposable? _watch;

    /// <summary>Whether the watched folder existed when the watch began; a watch
    /// on a missing folder sees nothing and is replaced once the folder is
    /// there.</summary>
    private bool _live;
    private ITimer? _retry;

    public BrainFolderWatch(
        IBrainSettings settings,
        IBrainFolderSource brain,
        DevbookDatabaseRefresher refresher,
        ILogger<BrainFolderWatch>? logger = null,
        TimeProvider? time = null,
        TimeSpan? missingFolderRetry = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(refresher);

        _settings = settings;
        _brain = brain;
        _refresher = refresher;
        _logger = logger ?? NullLogger<BrainFolderWatch>.Instance;
        _time = time ?? TimeProvider.System;
        _missingFolderRetry = missingFolderRetry ?? DefaultMissingFolderRetry;
    }

    /// <summary>The folder being watched now, or null.</summary>
    public string? WatchedRoot
    {
        get
        {
            lock (_gate) return _root;
        }
    }

    /// <summary>Watches the folder the setting names now, and follows the setting
    /// from here on. A second call does nothing.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;

            _settings.Changed += OnSettingChanged;
            _refresher.Rebuilt += OnRebuilt;
            Repoint();
        }
    }

    private void OnSettingChanged()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Repoint();
        }

        // Outside the lock: a subscriber that reloads may ask the source, and the
        // source asks nothing of this.
        NotifyPanels();
    }

    private void OnRebuilt(string root)
    {
        bool ours;
        lock (_gate)
        {
            ours = !_disposed && _root is not null && SameFolder(root, _root);
        }

        if (ours) NotifyPanels();
    }

    private void OnRetry()
    {
        lock (_gate)
        {
            if (_disposed || _root is null || _live || !Directory.Exists(_root)) return;
            Repoint();
        }

        NotifyPanels();
    }

    private void NotifyPanels()
    {
        try
        {
            _brain.NotifyContentChanged();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "A Devbook panel failed while reloading after the Brain changed.");
        }
    }

    /// <summary>Under <see cref="_gate"/>: the watch moved to the folder the
    /// source answers now, or stopped when it answers none; restarted when the
    /// folder it watched was missing and is there now.</summary>
    private void Repoint()
    {
        var root = _brain.BrainRoot;
        if (_watch is not null && _live && root is not null && SameFolder(root, _root!)) return;

        _retry?.Dispose();
        _retry = null;
        _watch?.Dispose();
        _watch = null;
        _root = null;
        _live = false;

        if (root is null) return;

        _live = Directory.Exists(root);
        _watch = _refresher.WatchBrain(root);
        _root = root;

        if (!_live)
        {
            _retry = _time.CreateTimer(_ => OnRetry(), null, _missingFolderRetry, _missingFolderRetry);
        }
    }

    private static bool SameFolder(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string folder)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return folder;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            if (_started)
            {
                _settings.Changed -= OnSettingChanged;
                _refresher.Rebuilt -= OnRebuilt;
            }

            _retry?.Dispose();
            _retry = null;
            _watch?.Dispose();
            _watch = null;
            _root = null;
        }
    }
}
