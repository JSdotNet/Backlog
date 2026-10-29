namespace Backlog.Modules.Notifications.Abstractions;

/// <summary>
/// The three timings the Notifications settings page shows and edits for the question
/// relay (local ADR 0041): how long without input makes the person away, how long a
/// question asked at the keyboard waits before it is published, and how long a dialog's
/// hook waits for an answer from Backlog.
/// <para>
/// The Sessions context owns what they mean (<c>.devbook/domain/sessions/domain.md#presence</c>);
/// this port is how the page reaches them without seeing that context
/// (<c>ModuleBoundaryTests</c>). A desktop host answers it over its workspace settings file,
/// which is machine-local: the timings never travel, like the presence they shape.
/// </para>
/// </summary>
public interface INotificationTimingSettings
{
    /// <summary>How long without keyboard or mouse input makes the person away.</summary>
    TimeSpan AwayAfter { get; }

    /// <summary>How long a question asked while the person is at the keyboard waits
    /// before it becomes a notification.</summary>
    TimeSpan NotifyAfter { get; }

    /// <summary>How long a question dialog's hook waits for an answer from Backlog.</summary>
    TimeSpan DialogWait { get; }

    /// <summary>Keeps the three timings, answering why they could not be kept as one
    /// sentence, or null when they were.</summary>
    string? Set(TimeSpan awayAfter, TimeSpan notifyAfter, TimeSpan dialogWait);

    /// <summary>Raised after the timings change, wherever they were changed from.</summary>
    event Action? Changed;
}
