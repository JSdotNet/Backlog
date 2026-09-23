using System.ComponentModel;

using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The work a session asks about: the backlog, and the items one import plan
/// produced. Both read-only.
/// <para>
/// One class for the two because they are one switchable group
/// (<see cref="BacklogMcpTools.Work"/>) and one read: the entries, narrowed two
/// ways. The roadmap used to ride here and now has its own class, because it has
/// its own context's feature key and its own port — see
/// <see cref="RoadmapTools"/>.
/// </para>
/// <para>
/// <b>An instance per call, and that is the point.</b> <c>ITaskItems</c> is
/// registered <c>AddScoped</c>, so nothing may hold one on a singleton. The SDK's
/// <c>WithTools&lt;T&gt;()</c> constructs the tool class with
/// <c>ActivatorUtilities.CreateInstance(request.Services, …)</c> for every
/// invocation, and <c>request.Services</c> is the request's own scope — which
/// makes ordinary constructor injection the correct shape here and a captured
/// singleton the incorrect one. The host has to leave
/// <c>McpServerOptions.ScopeRequests</c> alone for that to hold.
/// </para>
/// <para>
/// Every narrowing below happens in memory, because <see cref="ITaskItems"/> has
/// exactly one read — everything in rank order — and no filter by repository,
/// status or plan. That is the same thing the Tasks pane does with the same list.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class WorkTools(ITaskItems entries, IRepositoryDirectory repositories)
{
    internal const string ListEntries = "list_entries";
    internal const string GetPlanItems = "get_plan_items";

    [McpServerTool(Name = ListEntries, Title = "List backlog entries", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("The backlog entries filed against one repository, in rank order. Read-only.")]
    public async Task<EntryListPayload> ListEntriesAsync(
        [Description("The repository in owner/name form, e.g. JSdotNet/Backlog — the git remote of the checkout you are working in.")]
        string repository,
        CancellationToken cancellationToken = default)
    {
        var scope = RepositoryScope.Resolve(repositories, repository).ValueOrThrow();

        var all = await entries.ListAsync(cancellationToken).ConfigureAwait(false);

        // RepoIds hold `owner/name` ids and not aliases — the value an entry has
        // stored since the alias became a renameable label — so this matches the
        // resolved Id. Matched without regard to case for the same reason the
        // directory resolves that way: GitHub is case-preserving, not
        // case-sensitive, and an entry filed before a casing change is the same
        // entry.
        var filtered = all
            .Where(entry => entry.RepoIds is { } ids
                && ids.Contains(scope.Id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Order)
            .Select(Projections.Entry)
            .ToList();

        return new EntryListPayload(scope.Id, scope.Alias, filtered.Count, filtered);
    }

    [McpServerTool(Name = GetPlanItems, Title = "Get the items of an import plan", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("The backlog entries one import plan produced, in rank order, whichever repository they are filed against. Read-only.")]
    public async Task<PlanItemsPayload> GetPlanItemsAsync(
        [Description("The plan tag exactly as the entries carry it, sigil included, e.g. +backlog-mcp-server.")]
        string planId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            throw RepositoryScope.Failure(Error.Validation(
                "plan.required",
                "A plan id is required — the plan tag the entries carry, sigil and all."));
        }

        var all = await entries.ListAsync(cancellationToken).ConfigureAwait(false);

        // Ordinal, and the caller's value used exactly as it arrived. The stored
        // value carries the plan tag's `+` sigil (ImportPlanCommand.SharedTag),
        // so a tool that stripped or added one would answer for a plan nobody
        // named — and a case-insensitive match would merge two tags the grammar
        // keeps apart.
        var filtered = all
            .Where(entry => string.Equals(entry.ImportPlanId, planId, StringComparison.Ordinal))
            .OrderBy(entry => entry.Order)
            .Select(Projections.Entry)
            .ToList();

        return new PlanItemsPayload(planId, filtered.Count, filtered);
    }
}
