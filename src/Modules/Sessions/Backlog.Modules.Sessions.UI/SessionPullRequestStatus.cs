using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UI;

/// <summary>
/// How a pull request a session shipped stands, as the host read it: the readiness
/// verdict's chip label, the badge tone it wears, the pull request's title, and the
/// one-sentence reason behind the verdict.
/// <para>
/// Handed in by the host rather than read here, for the reason the task opener is:
/// asking GitHub is the pull requests pane's business, and the verdict is derived
/// there (<c>PullRequestVerdict</c>), so a pull request reads the same on both
/// screens. This pane only draws what it is given.
/// </para>
/// </summary>
/// <param name="Label">The verdict chip's text, such as "Checks running".</param>
/// <param name="Tone">One of the badge tone scale's names — quiet, live, alert, fault,
/// settled — or archived for a pull request closed without merging.</param>
/// <param name="Title">The pull request's own title, where GitHub gave one.</param>
/// <param name="Reason">Why the verdict is what it is, for the chip's tooltip.</param>
public sealed record SessionPullRequestStatus(string Label, string Tone, string? Title = null, string? Reason = null);

/// <summary>
/// Where a delivery run stands, in the words the list row shows beside its stage
/// strip, and its stages as the strip's steps.
/// </summary>
internal static class DeliveryRunProgress
{
    /// <summary>The run's stages as strip steps, in the order the run declared them.</summary>
    public static IReadOnlyList<Backlog.UI.Components.Feedback.FlowStep> Steps(DeliveryRun run) =>
        [.. run.Stages.Select((stage, index) => new Backlog.UI.Components.Feedback.FlowStep(
            string.IsNullOrWhiteSpace(stage.Name) ? $"Stage {index + 1}" : stage.Name,
            DeliveryRunLine.Tone(stage.Status),
            DeliveryRunLine.StatusLabel(stage.Status)))];

    /// <summary>
    /// One line: the skill, then where the run stands — every stage done, the stage it
    /// failed or was parked at, or the stage it is on and how far along that is.
    /// </summary>
    public static string Summary(DeliveryRun run)
    {
        var stages = run.Stages;
        var status = run.Status.ToLowerInvariant();
        string Name(int index) => string.IsNullOrWhiteSpace(stages[index].Name) ? $"stage {index + 1}" : stages[index].Name;

        string Where()
        {
            if (stages.Count == 0) return DeliveryRunLine.StatusLabel(run.Status);

            var failed = Index(stage => stage.Status.ToLowerInvariant() is "failed" or "error");
            if (failed >= 0) return $"{Name(failed)} failed";

            var blocked = Index(stage => stage.Status.Equals("blocked", StringComparison.OrdinalIgnoreCase));
            if (status is "parked") return $"parked at {Name(blocked >= 0 ? blocked : Math.Max(0, FirstOpen()))}";
            if (blocked >= 0) return $"blocked at {Name(blocked)}";

            if (status is "done" or "completed" or "success")
            {
                var done = stages.Count(stage => DeliveryRunLine.Tone(stage.Status) is Backlog.UI.Components.Feedback.FlowStepTone.Done or Backlog.UI.Components.Feedback.FlowStepTone.Skipped);

                return done == stages.Count
                    ? $"all {stages.Count} {(stages.Count == 1 ? "stage" : "stages")} done"
                    : $"{DeliveryRunLine.StatusLabel(run.Status)}, {done} of {stages.Count} stages done";
            }

            var current = Index(stage => DeliveryRunLine.Tone(stage.Status) is Backlog.UI.Components.Feedback.FlowStepTone.Active);
            if (current < 0) current = FirstOpen();

            return current >= 0 && run.InProgress
                ? $"{Name(current)} ({current + 1} of {stages.Count})"
                : DeliveryRunLine.StatusLabel(run.Status);
        }

        int Index(Func<DeliveryRunStage, bool> predicate)
        {
            for (var index = 0; index < stages.Count; index++)
            {
                if (predicate(stages[index])) return index;
            }

            return -1;
        }

        int FirstOpen() => Index(stage => DeliveryRunLine.Tone(stage.Status) is Backlog.UI.Components.Feedback.FlowStepTone.Pending or Backlog.UI.Components.Feedback.FlowStepTone.Active);

        return $"{run.SkillId} · {Where()}";
    }
}
