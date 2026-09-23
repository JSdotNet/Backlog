using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The backlog, as the port offers it: one read, and the three writes the
/// tracker tools are allowed to make.
/// <para>
/// <b>The three are recorded; every other write still throws.</b> That split is
/// the assertion rather than a convenience. It used to be "every write throws",
/// which stated local ADR 0012's read-only claim in the only place a test could
/// fail on it, and <see cref="TrackerTools"/> made the blanket form false.
/// What survives is the part that was always the point: a tool reaching for
/// <c>DeleteAsync</c>, <c>ReorderAsync</c>, <c>ImportPlanAsync</c> or either
/// repository pass fails here rather than quietly passing — so the absence of a
/// delete tool is enforced by this double as well as by the catalog.
/// </para>
/// <para>
/// The read-only tools are still held to the old claim, and held to it by these
/// same recordings: <see cref="Saves"/>, <see cref="Links"/> and
/// <see cref="Usages"/> are empty after a read, which is what
/// <c>WorkToolsTests</c> and <c>TrackerToolsTests</c> assert about them. A
/// recorded write can be shown not to have happened; a thrown one could only be
/// shown not to have thrown.
/// </para>
/// </summary>
/// <param name="entries">The backlog this double answers with. A save replaces
/// the entry it names, so a tool that reads back what it wrote sees what it
/// wrote.</param>
internal sealed class FakeTaskItems(params TaskItemDto[] entries) : ITaskItems
{
    private readonly List<TaskItemDto> _entries = [.. entries];

    /// <summary>How many times the list was asked for. A tool that reads the
    /// whole backlog twice to answer one question is a tool worth noticing.</summary>
    public int Reads { get; private set; }

    /// <summary>Every <c>SaveFromTextAsync</c>, in order, exactly as it arrived —
    /// the raw text included, because the text <em>is</em> the entry and a test
    /// about what a write preserved has nowhere else to look.</summary>
    public List<SaveCall> Saves { get; } = [];

    /// <summary>Every <c>LinkToIssueAsync</c>, in order. The target type is
    /// recorded because getting it wrong is the failure that shows up on a screen
    /// rather than in a payload.</summary>
    public List<LinkCall> Links { get; } = [];

    /// <summary>Every <c>RecordUsageAsync</c>, in order.</summary>
    public List<UsageCall> Usages { get; } = [];

    public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reads++;

        return Task.FromResult<IReadOnlyList<TaskItemDto>>([.. _entries]);
    }

    /// <summary>
    /// Records the save and answers with the entry the text describes.
    /// <para>
    /// The answer is parsed out of the raw text rather than echoed from the
    /// entry that was there, because that is what the real save does and it is
    /// what the tools read back: a transition that returned the old status would
    /// let a tool claiming to have moved something pass without having moved it.
    /// <c>EntryTextParser</c> is the module's own reader, so the double and the
    /// production path cannot disagree about what a block of text says.
    /// </para>
    /// </summary>
    public Task<Result<SavedTaskDto>> SaveFromTextAsync(
        Guid? id,
        string rawText,
        int order,
        string? sourceInboxId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Saves.Add(new SaveCall(id, rawText, order));

        var parsed = EntryTextParser.Parse(rawText);

        if (string.IsNullOrWhiteSpace(parsed.Title))
        {
            return Task.FromResult<Result<SavedTaskDto>>(
                Error.Validation("entry.title_required", "An entry needs a title."));
        }

        var existing = id is { } known ? _entries.FirstOrDefault(entry => entry.Id == known) : null;

        var saved = new TaskItemDto(
            existing?.Id ?? id ?? Guid.NewGuid(),
            parsed.Title,
            parsed.Body,
            parsed.Type ?? EntryType.Task,
            parsed.Priority ?? Priority.Medium,
            parsed.Status ?? EntryStatus.Draft,
            parsed.Area,
            parsed.Tags,
            order,
            parsed.SubItems.Count,
            parsed.SubItems.Count(subItem => subItem.Done),
            existing?.Projections ?? [],
            RepoIds: parsed.RepoIds);

        if (existing is not null) _entries[_entries.IndexOf(existing)] = saved;
        else _entries.Add(saved);

        return Task.FromResult<Result<SavedTaskDto>>(new SavedTaskDto(saved));
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw Written(nameof(DeleteAsync));

    public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) =>
        throw Written(nameof(ReorderAsync));

    public Task<Result<TaskItemDto>> LinkToIssueAsync(
        Guid id,
        string repoId,
        string externalId,
        string targetType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Links.Add(new LinkCall(id, repoId, externalId, targetType));

        var existing = _entries.FirstOrDefault(entry => entry.Id == id);

        if (existing is null)
        {
            // The module's own code for a missing entry, so a tool leaning on the
            // port to refuse rather than pre-reading is exercised against the
            // refusal it will actually meet.
            return Task.FromResult<Result<TaskItemDto>>(
                Error.NotFound("entry.not_found", "That entry no longer exists."));
        }

        var linked = existing with
        {
            Projections = [.. existing.Projections, new EntryProjectionDto(repoId, externalId, targetType)]
        };

        _entries[_entries.IndexOf(existing)] = linked;

        return Task.FromResult<Result<TaskItemDto>>(linked);
    }

    public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Usages.Add(new UsageCall(id, action));

        return Task.CompletedTask;
    }

    public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) =>
        throw Written(nameof(ReconcileRepositoryIdsAsync));

    public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) =>
        throw Written(nameof(RenameRepositoryAsync));

    public Task<Result<ImportPlanResultDto>> ImportPlanAsync(
        string rawText,
        string? defaultRepo = null,
        IReadOnlyDictionary<string, string>? repoMatches = null,
        string? sourceInboxId = null,
        CancellationToken cancellationToken = default) => throw Written(nameof(ImportPlanAsync));

    private static InvalidOperationException Written(string member) =>
        new($"An MCP tool called ITaskItems.{member}, which no tool in this assembly may.");

    /// <summary>One save, as it arrived.</summary>
    internal sealed record SaveCall(Guid? Id, string RawText, int Order);

    /// <summary>One link, as it arrived.</summary>
    internal sealed record LinkCall(Guid Id, string RepoId, string ExternalId, string TargetType);

    /// <summary>One usage event, as it arrived.</summary>
    internal sealed record UsageCall(Guid Id, string Action);
}

/// <summary>
/// Entries with the fields these tests are about and defaults for the rest.
/// <para>
/// A builder rather than twenty-three positional arguments at each call site:
/// the DTO carries an attachment, a recurrence rule and a reminder, and a test
/// about <c>repo_ids</c> should not have to say anything about any of them.
/// </para>
/// </summary>
internal static class Entries
{
    internal static TaskItemDto Entry(
        string title,
        int order = 0,
        IReadOnlyList<string>? repoIds = null,
        string? importPlanId = null,
        string? importItemId = null,
        EntryStatus status = EntryStatus.Ready,
        Priority priority = Priority.Medium,
        Guid? id = null,
        string body = "",
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<EntryProjectionDto>? projections = null,
        int totalSubItems = 0) =>
        new(
            id ?? Guid.NewGuid(),
            title,
            body,
            EntryType.Task,
            priority,
            status,
            Area: null,
            Tags: tags ?? [],
            order,
            totalSubItems,
            CompletedSubItems: 0,
            Projections: projections ?? [],
            RepoIds: repoIds,
            ImportPlanId: importPlanId,
            ImportItemId: importItemId);
}
