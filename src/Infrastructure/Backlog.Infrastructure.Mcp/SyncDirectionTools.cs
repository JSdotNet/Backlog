using System.ComponentModel;
using System.Globalization;

using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Devbook;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// Which way each devbook sync unit is levelled, answered over Backlog's direction
/// map (<see cref="IDevbookSyncDirectionStore"/>) — what the pull and push sweeps
/// read where they used to run <c>units.mjs --direction</c>.
/// <para>
/// <b>Why the map and not the chapter.</b> Devbook's branch-true decision moved
/// <c>sync</c> out of the Markdown: a branch states what is true of its code, and
/// which way a sweep may level a unit is a choice about the work, so the host that
/// runs the sweeps keeps it. A unit resolves nearest wins over the map's entries —
/// unit, page, context, folder, then <c>report</c> — by
/// <see cref="DevbookSync.ResolveUnit"/>, the mirror of <c>units.mjs</c>'s
/// <c>syncSources</c>.
/// </para>
/// <para>
/// <b>The answer is units.mjs's own field names</b> — <c>sync</c>, <c>syncFrom</c>,
/// <c>syncLevel</c> — so a sweep reads a unit here as it read one there. The unit
/// is echoed as sent, and <c>syncFrom</c> is written in the same path form: a
/// <c>.devbook/…</c> unit gets a <c>.devbook/…</c> source, so a caller can match it
/// against the ids it already holds. Not named <c>devbook_*</c>: that prefix is the
/// <c>devbook.decision@1</c> group a caller binds by, and this is no part of it.
/// </para>
/// <para>
/// Read-only. A direction is set in the devbook view, by a person; a sweep only
/// reads which way it may go.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class SyncDirectionTools(IDevbookSyncDirectionStore directions, IRepositoryDirectory repositories)
{
    internal const string ListSyncDirections = "list_sync_directions";

    /// <summary>The code the refusal of the Brain carries.</summary>
    internal const string BrainCode = "brain.no_sync_directions";

    /// <summary>
    /// This class as a switchable group, behind <c>repository-devbook</c> — the
    /// key <see cref="BacklogMcpTools.Devbook"/> and <see cref="DecisionTools"/>
    /// read. Somebody switching the devbook off has switched off the units these
    /// directions are about. A group of its own because it is another port: that
    /// class keeps subjects' workflow state, this reads the direction map.
    /// </summary>
    internal static BacklogMcpTools.McpToolGroup Group { get; } = new(
        DevbookFeatures.RepositoryDevbook,
        typeof(SyncDirectionTools),
        [ListSyncDirections]);

    // Closed: the map is Backlog's own store and nothing on this path can reach
    // GitHub.
    [McpServerTool(Name = ListSyncDirections, Title = "List the devbook sync directions", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Which way each devbook sync unit is levelled, from Backlog's direction map — what a pull or push sweep reads "
        + "instead of units.mjs --direction. For every unit id sent (as units.mjs --groups --json prints it, e.g. "
        + ".devbook/domain/inbox/domain.md#inbox-item or .devbook/domain/inbox#shared-types): { unit, sync, syncFrom, "
        + "syncLevel }, resolved nearest wins — unit, page, context, folder — and report from default when nothing "
        + "states one. syncFrom is the stating block in the unit's own path form, or null. Also every stated entry: "
        + "{ address, direction, by, at }. A pull sweep takes pull and sync units, a push sweep push and sync. "
        + "Read-only; a direction is set in Backlog's devbook view.")]
    public SyncDirectionsPayload ListSyncDirectionsOf(
        [Description("The repository in owner/name form, e.g. JSdotNet/Backlog. The Brain (" + BrainMcpScope.Value + ") has no sync units and is refused.")]
        string repository,
        [Description("The unit ids to resolve, as units.mjs prints them. Omitted: the stated entries alone.")]
        IReadOnlyList<string>? units = null)
    {
        if (BrainMcpScope.Is(repository))
        {
            throw RepositoryScope.Failure(Error.Validation(
                BrainCode,
                $"The Brain ({BrainMcpScope.Value}) has no devbook sync units. Name a repository in owner/name form."));
        }

        var scope = RepositoryScope.Resolve(repositories, repository).ValueOrThrow();

        // Listed first: the first read of a repository's map is what imports the
        // directions its Markdown still states, and the units below should see them.
        var stated = directions.List(scope.Alias);

        return new SyncDirectionsPayload(
            scope.Id,
            [.. (units ?? []).Where(unit => !string.IsNullOrWhiteSpace(unit)).Select(unit => Resolve(scope.Alias, unit))],
            [.. stated.Select(statement => new SyncStatementPayload(statement.Address, statement.Direction, statement.By, Utc(statement.At)))]);
    }

    /// <summary>One unit as units.mjs would answer it. A unit in no devbook folder
    /// is <c>report</c> from <c>default</c>: nothing can state a direction for
    /// it.</summary>
    private SyncUnitPayload Resolve(string alias, string unit)
    {
        if (!DevbookSync.TryAddress(unit, out var folder, out var address))
        {
            return new SyncUnitPayload(unit, DevbookSync.DefaultDirection, null, "default");
        }

        var direction = DevbookSync.ResolveUnit(address, source => directions.Get(alias, source));

        return new SyncUnitPayload(
            unit,
            direction.Direction,
            direction.Source is { } source ? SentForm(unit, folder, address, source) : null,
            direction.From is { } level ? level.ToString().ToLowerInvariant() : "default");
    }

    /// <summary>
    /// <paramref name="source"/> in the path form <paramref name="unit"/> was sent
    /// in: the sent unit's prefix up to its folder-relative path —
    /// <c>.devbook/domain/</c>, <c>.domain/</c>, <c>domain/</c> — in front of the
    /// source's folder-relative path.
    /// </summary>
    private static string SentForm(string unit, DevbookFolder folder, string address, string source)
    {
        var name = folder.ToString().ToLowerInvariant();
        var sent = unit.Trim().Replace('\\', '/').Split('#', 2)[0];
        var relative = address.Split('#', 2)[0][(name.Length + 1)..];
        var prefix = sent.EndsWith(relative, StringComparison.Ordinal) ? sent[..^relative.Length] : $"{name}/";

        return prefix + source[(name.Length + 1)..];
    }

    /// <summary>ISO 8601 with a <c>Z</c>, as <see cref="DevbookDecisionPayload"/>
    /// spells a stamp.</summary>
    private static string Utc(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
}

/// <summary>What <c>list_sync_directions</c> answers: the repository as
/// <c>owner/name</c>, the units asked about, and every stated entry.</summary>
public sealed record SyncDirectionsPayload(
    string Repository,
    IReadOnlyList<SyncUnitPayload> Units,
    IReadOnlyList<SyncStatementPayload> Stated);

/// <summary>One unit's direction, in <c>units.mjs</c>'s own field names.</summary>
/// <param name="Unit">The unit id as sent.</param>
/// <param name="Sync">The direction in force.</param>
/// <param name="SyncFrom">The block that states it, in the unit's path form; null
/// when nothing does.</param>
/// <param name="SyncLevel"><c>unit</c>, <c>page</c>, <c>context</c>,
/// <c>folder</c>, or <c>default</c>.</param>
public sealed record SyncUnitPayload(string Unit, string Sync, string? SyncFrom, string SyncLevel);

/// <summary>One entry of the map: the block's folder-qualified address, its
/// direction, and who set it when (<c>markdown</c> for one the import copied
/// in).</summary>
public sealed record SyncStatementPayload(string Address, string Direction, string By, string At);
