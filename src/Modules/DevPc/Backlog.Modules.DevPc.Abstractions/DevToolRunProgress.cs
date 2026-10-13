using System;
using System.Collections.Generic;
using System.Linq;

namespace Backlog.Modules.DevPc.Abstractions;

/// <summary>Where one tool stands in a run that is still going.</summary>
public enum DevToolRunStepState
{
    /// <summary>Not reached yet.</summary>
    Queued,

    /// <summary>The step the run is on now.</summary>
    Running,

    /// <summary>Finished and got there.</summary>
    Done,

    /// <summary>Finished and did not get there.</summary>
    Failed
}

/// <summary>
/// One tool's place in a run that is still going: what it is, which version it is
/// being taken from and to, where it stands, and — once it has finished — how long
/// it took and, for a failed step, what the port said about it.
/// </summary>
/// <param name="Key">The row's catalog key, so the pane can put a failure back on
/// the row it belongs to.</param>
/// <param name="Name">The row's display name, as it read when the run started.</param>
/// <param name="FromVersion">The version the machine had, or <see langword="null"/>
/// for an install.</param>
/// <param name="ToVersion">The version the step is taking it to.</param>
/// <param name="State">Where the step stands.</param>
public sealed record DevToolRunQueueItem(
    string Key,
    string Name,
    string? FromVersion,
    string? ToVersion,
    DevToolRunStepState State)
{
    /// <summary>How long the step took. Null until it has finished.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>What the port said about a failed step. Null for every other
    /// state.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// Whether Update all takes this row: the one rule the port runs by and the
    /// pane draws its queue by, so the two cannot disagree about what a run holds.
    ///
    /// <para>A marketplace Claude has never been told about is included though it
    /// has no version to be behind: every Claude plugin id resolves against one.
    /// An application is included when it is behind or absent — "make this machine
    /// match the catalog". <see cref="DevToolInfo.CanInstall"/> is what keeps that
    /// honest: a checklist row or a detect-only package is permanently "not
    /// installed", and would otherwise fail on every sweep forever.</para>
    /// </summary>
    public static bool InUpdateAll(DevToolInfo tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return tool.Kind switch
        {
            DevToolKind.Marketplace => tool.CanUpdate || !tool.Installed,
            DevToolKind.Application => tool.CanUpdate || tool.CanInstall,
            _ => tool.CanUpdate
        };
    }

    /// <summary>The queue item a row starts a run as: queued, read off the row as
    /// it stood before the run — the same reading <see cref="DevToolRunStep.For"/>
    /// takes once the step has finished.</summary>
    public static DevToolRunQueueItem Queue(DevToolInfo tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var step = DevToolRunStep.For(tool, succeeded: true, TimeSpan.Zero);
        return new DevToolRunQueueItem(tool.Key, tool.Name, step.FromVersion, step.ToVersion, DevToolRunStepState.Queued);
    }
}

/// <summary>
/// A snapshot of a run that is still going: every step it will take, in order,
/// with where each stands, and the transcript so far.
///
/// <para>A whole snapshot each time rather than a delta, so a reader that missed
/// one report — a render that coalesced two — still draws the right queue from the
/// next. The run reports one when it has decided what it will do, and again as each
/// step starts and as each finishes.</para>
/// </summary>
public sealed record DevToolRunProgress(
    IReadOnlyList<DevToolRunQueueItem> Items,
    IReadOnlyList<DevToolCommand> Commands)
{
    /// <summary>How many steps the run takes.</summary>
    public int Total => Items.Count;

    /// <summary>How many steps finished and got there.</summary>
    public int Done => Items.Count(item => item.State is DevToolRunStepState.Done);

    /// <summary>How many steps finished and did not.</summary>
    public int Failed => Items.Count(item => item.State is DevToolRunStepState.Failed);

    /// <summary>How many steps are going now.</summary>
    public int Running => Items.Count(item => item.State is DevToolRunStepState.Running);

    /// <summary>How many steps have finished, either way — what the progress bar
    /// fills by.</summary>
    public int Finished => Done + Failed;
}
