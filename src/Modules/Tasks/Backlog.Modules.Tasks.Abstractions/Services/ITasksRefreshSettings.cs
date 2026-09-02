namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// How often the list re-reads the store because something outside this app may
/// have written to it.
/// <para>
/// Two settings rather than one, because "off" and "how often" are separate
/// answers: somebody who turns the check off has said the store is theirs alone,
/// and the interval they had chosen is still theirs when they turn it back on.
/// </para>
/// </summary>
public sealed record TasksRefreshSettings
{
    /// <summary>Slow enough that a shared folder has finished writing, quick
    /// enough that a change made on the other machine is on screen before
    /// anybody thinks to reach for a reload.</summary>
    public const int DefaultPollingIntervalSeconds = 5;

    /// <summary>A floor rather than a preference. Below a second the check stops
    /// being a poll and becomes a spin, and the store it reads is a file.</summary>
    public const int MinimumPollingIntervalSeconds = 1;

    /// <summary>On by default: the store is a single file, and a file in a synced
    /// folder is the ordinary way two machines share one backlog today.</summary>
    public bool PollingEnabled { get; init; } = true;

    public int PollingIntervalSeconds { get; init; } = DefaultPollingIntervalSeconds;
}

/// <summary>
/// Whether the list watches the store for changes it did not make, and how often.
/// <para>
/// A port on Tasks' own surface for the same reason
/// <see cref="ITaskStore"/> and <see cref="IRoadmapTagSource"/> are: a screen
/// renders one context and asks that context's module, and the file the answer is
/// kept in is an adapter's business rather than the list's.
/// </para>
/// <para>
/// This is deliberately an interim answer and is shaped to be demoted rather than
/// removed. When the store learns to push — the cloud sync module's job, which
/// this touches nothing of — the same switch becomes "fall back to polling when
/// push is unavailable", because nothing but a timer and a file timestamp hangs
/// off it.
/// </para>
/// </summary>
public interface ITasksRefreshSettings
{
    /// <summary>Raised after either setting changes, so a running poll can be
    /// restarted, rescaled or stopped without waiting for a restart.</summary>
    event Action? Changed;

    /// <summary>The choices that have been made, as persisted.</summary>
    TasksRefreshSettings Current { get; }

    /// <summary>Where those choices are written — shown on the settings page so
    /// the file can be found in a file manager.</summary>
    string SettingsPath { get; }

    /// <summary>Switches the check on or off. Returns an error message rather
    /// than throwing when the choice could not be saved — a settings toggle is an
    /// ordinary thing to click.</summary>
    string? SetPollingEnabled(bool enabled);

    /// <summary>Sets how often the check runs. Returns an error message and
    /// leaves the setting alone for an interval below
    /// <see cref="TasksRefreshSettings.MinimumPollingIntervalSeconds"/> — a
    /// number typed into a settings field is an ordinary thing to get wrong.</summary>
    string? SetPollingIntervalSeconds(int seconds);
}
