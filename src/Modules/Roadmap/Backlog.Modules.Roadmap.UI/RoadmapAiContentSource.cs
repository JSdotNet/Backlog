using System.Text;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.SharedKernel.Ai;

namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// The Roadmap's answer to <see cref="IAiContentSource"/>: the plan — its
/// milestones and its items — as <see cref="IRoadmapPlanning"/> holds it.
/// </summary>
/// <remarks>
/// <para>
/// The same read the band makes, <see cref="IRoadmapPlanning.GetPlanAsync"/>
/// with no scope, because the plan is one document across every repository and
/// the band draws all of it. What the band does after that — the window on
/// screen, which lane is collapsed, the item being dragged — is the reader
/// arranging the screen, and none of it reaches here.
/// </para>
/// <para>
/// Milestones first, then items, each in the plan's own order. A milestone is a
/// fixed point the items are planned against, so the assistant reads the dates
/// that do not move before the ones that do. Dates are written ISO, because the
/// plan's own precision is a day and a locale-formatted date is a second thing
/// for the assistant to parse.
/// </para>
/// </remarks>
internal sealed class RoadmapAiContentSource(IRoadmapPlanning planning) : IAiContentSource
{
    public string AreaKey => "roadmap";

    public string AreaTitle => "Roadmap";

    public async Task<AiContent> ComposeAsync(AiContentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = await planning.GetPlanAsync(cancellationToken).ConfigureAwait(false);

        // Milestones are named by items, so the lookup is built once for the
        // whole plan rather than once per item.
        var milestonesById = plan.Milestones.ToDictionary(milestone => milestone.Id, milestone => milestone.Title);

        IReadOnlyList<string> records =
        [
            .. plan.Milestones.Select(Text),
            .. plan.Items.Select(item => Text(item, milestonesById))
        ];

        return AiContentBudget.Compose(AreaKey, AreaTitle, records, record => record, request.Question, request.BudgetCharacters);
    }

    /// <summary>"Milestone: name, date, kind, repositories, lane."</summary>
    internal static string Text(RoadmapMilestoneDto milestone)
    {
        var text = new StringBuilder("Milestone: ").Append(milestone.Title.Trim());
        text.Append(", on ").Append(Date(milestone.On));
        text.Append(", ").Append(milestone.Kind.ToString().ToLowerInvariant());

        if (milestone.IsPlanWide) text.Append(", plan-wide");
        AppendRepositories(text, milestone.RepositoryAliases);
        if (!string.IsNullOrWhiteSpace(milestone.Lane)) text.Append(", lane ").Append(milestone.Lane.Trim());

        return text.ToString();
    }

    /// <summary>"Item: title, window, priority, repositories, lane, milestones
    /// it waits on, tag; then its notes on their own lines when it has any."</summary>
    internal static string Text(RoadmapItemDto item, IReadOnlyDictionary<Guid, string> milestonesById)
    {
        var text = new StringBuilder("Item: ").Append(item.Title.Trim());
        text.Append(", ").Append(Date(item.Start)).Append(" to ").Append(Date(item.End));
        text.Append(", ").Append(item.Priority.ToString().ToLowerInvariant()).Append(" priority");

        AppendRepositories(text, item.RepositoryAliases);
        if (!string.IsNullOrWhiteSpace(item.Lane)) text.Append(", lane ").Append(item.Lane.Trim());

        // Only the dependencies that are milestones are named: an item that waits
        // on another item is a fact about ordering the plan already shows by
        // date, and naming it would need a second pass over the items.
        var milestones = item.DependsOn
            .Where(milestonesById.ContainsKey)
            .Select(id => milestonesById[id])
            .ToList();

        if (milestones.Count > 0) text.Append(", before milestone ").Append(string.Join(" and ", milestones));
        if (!string.IsNullOrWhiteSpace(item.Tag)) text.Append(", tagged +").Append(item.Tag.Trim());
        if (!string.IsNullOrWhiteSpace(item.Notes)) text.Append('\n').Append(item.Notes.Trim());

        return text.ToString();
    }

    private static void AppendRepositories(StringBuilder text, IReadOnlyList<string> aliases)
    {
        if (aliases.Count == 0) return;

        text.Append(aliases.Count == 1 ? ", repository " : ", repositories ").Append(string.Join(", ", aliases));
    }

    private static string Date(DateOnly date) => AiContentBudget.Date(date);
}
