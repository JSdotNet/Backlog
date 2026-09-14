using Backlog.Modules.Capture.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Capture.Abstractions.Services;

/// <summary>
/// Which sources are watched and what they are pointed at.
/// <para>
/// A port on Capture's own surface, the same shape as Tasks'
/// <c>ITasksRefreshSettings</c>: the settings screen writes it, the run reads it,
/// and the file the answer is kept in is an adapter's business. Neither side
/// sees the other.
/// </para>
/// </summary>
public interface ICaptureSourceSettings
{
    /// <summary>Raised after a choice changes, so a screen showing the sources
    /// can redraw without waiting for a restart.</summary>
    event Action? Changed;

    /// <summary>The choices that have been made, as persisted.</summary>
    CaptureSourceSettings Current { get; }

    /// <summary>Where those choices are written — shown on the settings page so
    /// the file can be found in a file manager.</summary>
    string SettingsPath { get; }

    /// <summary>Switches a source on or off. Returns an error message rather
    /// than throwing when the choice could not be saved — a settings toggle is
    /// an ordinary thing to click.</summary>
    string? SetEnabled(CaptureSourceKind kind, bool enabled);

    /// <summary>Replaces what a source is pointed at. Blank lines are dropped
    /// and duplicates folded, so what is stored is what a run will look at.
    /// Returns an error message rather than throwing when it could not be
    /// saved.</summary>
    string? SetTargets(CaptureSourceKind kind, IReadOnlyList<string> targets);
}
