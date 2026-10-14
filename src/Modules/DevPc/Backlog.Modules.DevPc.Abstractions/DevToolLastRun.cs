using System;
using System.Collections.Generic;
using System.Linq;

namespace Backlog.Modules.DevPc.Abstractions;

/// <summary>Which of the pane's runs a <see cref="DevToolLastRun"/> records.
/// Only the acts that change the machine and leave a transcript behind: a
/// switch-off, a tick or a catalog edit is a statement, not a run.</summary>
public enum DevToolRunKind
{
    UpdateAll,
    Update,
    Install,
    ClearStaleCache
}

/// <summary>How a run, or one tool's step in it, ended.</summary>
public enum DevToolRunOutcome
{
    Succeeded,
    Failed,

    /// <summary>Some tools updated and some did not. Only a whole run reads this
    /// way; a single step either worked or it did not.</summary>
    PartlyFailed
}

/// <summary>
/// One tool's part in a run.
/// </summary>
/// <param name="Name">The row's display name, as it read when the run started.</param>
/// <param name="FromVersion">The version the machine had, or <see langword="null"/>
/// when the tool was not installed — an install has nothing to come from.</param>
/// <param name="ToVersion">The version the run was taking it to: the row's
/// available version. Read with <paramref name="Outcome"/>; a failed step never
/// got there.</param>
public sealed record DevToolRunStep(
    string Name,
    string? FromVersion,
    string? ToVersion,
    DevToolRunOutcome Outcome,
    TimeSpan Duration)
{
    /// <summary>The step a row's Update or Install was, read off the row as it
    /// stood before the run.</summary>
    public static DevToolRunStep For(DevToolInfo tool, bool succeeded, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return new DevToolRunStep(
            tool.Name,
            tool.Installed ? NullIfBlank(tool.InstalledVersion) : null,
            NullIfBlank(tool.AvailableVersion),
            succeeded ? DevToolRunOutcome.Succeeded : DevToolRunOutcome.Failed,
            duration);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// The most recent finished run of the System tools pane — an Update all, one
/// row's Update or Install, or a Clear stale cache — with every tool it touched
/// and the whole transcript it left.
///
/// <para>Kept per machine, beside the per-PC tool choices, so the pane's Last
/// run card still has something to say after the app restarts. Only the last
/// one: each finished run overwrites the record before it, and a run still in
/// progress is never written. No history and no trend across runs is kept —
/// that was decided against.</para>
/// </summary>
public sealed record DevToolLastRun(
    DevToolRunKind Kind,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    DevToolRunOutcome Outcome,
    IReadOnlyList<DevToolRunStep> Steps,
    string Transcript)
{
    /// <summary>Elapsed time from start to finish.</summary>
    public TimeSpan Duration => FinishedAt - StartedAt;

    /// <summary>The record a finished run leaves, out of the port's result and
    /// the steps the run took.</summary>
    public static DevToolLastRun From(
        DevToolRunKind kind,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        DevToolActionResult result,
        IReadOnlyList<DevToolRunStep> steps)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(steps);

        return new DevToolLastRun(
            kind,
            startedAt,
            finishedAt,
            OutcomeOf(result.Succeeded, steps),
            steps,
            TranscriptOf(result));
    }

    /// <summary>Whether a row's run is an Update or an Install: the same port
    /// call, told apart by whether the machine had the tool.</summary>
    public static DevToolRunKind KindFor(DevToolInfo tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return tool.Installed ? DevToolRunKind.Update : DevToolRunKind.Install;
    }

    /// <summary>
    /// A run's outcome, from the port's verdict and its steps.
    ///
    /// <para>Partly failed only when the steps disagree: at least one tool got
    /// there and at least one did not. A failed run whose every step failed — or
    /// that never reached a step — failed outright.</para>
    /// </summary>
    public static DevToolRunOutcome OutcomeOf(bool succeeded, IReadOnlyList<DevToolRunStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var anySucceeded = steps.Any(step => step.Outcome is DevToolRunOutcome.Succeeded);
        var anyFailed = steps.Any(step => step.Outcome is not DevToolRunOutcome.Succeeded);

        if (anySucceeded && anyFailed)
        {
            return DevToolRunOutcome.PartlyFailed;
        }

        return succeeded && !anyFailed ? DevToolRunOutcome.Succeeded : DevToolRunOutcome.Failed;
    }

    /// <summary>
    /// The transcript of a run: every command in the order it ran, as the pane's
    /// command log draws it, then the run's own closing message.
    /// </summary>
    public static string TranscriptOf(DevToolActionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var blocks = result.Commands.Select(Transcribe).ToList();
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            blocks.Add(result.Message);
        }

        return string.Join("\n\n", blocks);
    }

    /// <summary>One command as a block: the line you could have typed, whatever it
    /// printed, and the exit code only when it is not zero.</summary>
    public static string Transcribe(DevToolCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var lines = new List<string> { $"$ {command.CommandLine}" };

        if (command.Output.Length > 0)
        {
            lines.Add(command.Output);
        }

        if (command.ExitCode != 0)
        {
            lines.Add($"exit code {command.ExitCode}");
        }

        return string.Join("\n", lines);
    }
}
