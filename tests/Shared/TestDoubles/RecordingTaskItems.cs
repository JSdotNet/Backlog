using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Tests;

/// <summary>The Tasks facade, recording what it is asked and answering the
/// way the real one would in shape: a saved entry with a fresh id, and an
/// import result naming one entry per top-level heading.</summary>
internal sealed class RecordingTaskItems(int existing = 0) : ITaskItems
{
    public List<(Guid Id, string RawText, int Order, string? SourceInboxId)> Saves { get; } = [];

    public List<(string RawText, string? DefaultRepo, string? SourceInboxId, IReadOnlyDictionary<string, string>? SourceInboxIds)> Imports { get; } = [];

    /// <summary>What the import answers with instead of entries.</summary>
    public Error? FailImportWith { get; init; }

    /// <summary>Every entry an import answered with.</summary>
    public List<TaskItemDto> Created { get; } = [];

    /// <summary>An <c>id:</c> the import leaves out of its answer, as if it
    /// had made no entry for it.</summary>
    public string? LeaveOut { get; init; }

    /// <summary>Which save, counted from one, answers with a failure.</summary>
    public int? FailOnSave { get; init; }

    /// <summary>Entries the list answers with after the <c>existing</c> ones, by the id and
    /// title a test gave them.</summary>
    public List<(Guid Id, string Title)> Held { get; } = [];

    public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaskItemDto>>(
            [.. Enumerable.Range(0, existing).Select(i => Entry(Guid.NewGuid(), $"Existing {i}")), .. Held.Select(held => Entry(held.Id, held.Title))]);

    public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid? id, string rawText, int order, string? sourceInboxId = null, CancellationToken cancellationToken = default)
    {
        if (FailOnSave == Saves.Count + 1)
        {
            return Task.FromResult(Result.Failure<SavedTaskDto>(Error.Validation("entry.needs_title", "An entry needs a title line before it can be saved.")));
        }

        var created = Guid.NewGuid();
        Saves.Add((created, rawText, order, sourceInboxId));
        return Task.FromResult(Result.Success(new SavedTaskDto(Entry(created, EntryTextParser.Parse(rawText).Title))));
    }

    public Task<Result<ImportPlanResultDto>> ImportPlanAsync(string rawText, string? defaultRepo = null, IReadOnlyDictionary<string, string>? repoMatches = null, string? sourceInboxId = null, IReadOnlyDictionary<string, string>? sourceInboxIds = null, bool layOutOnRoadmap = false, CancellationToken cancellationToken = default)
    {
        Imports.Add((rawText, defaultRepo, sourceInboxId, sourceInboxIds));

        if (FailImportWith is { } error) return Task.FromResult(Result.Failure<ImportPlanResultDto>(error));

        // Answered in reverse, so a caller that matched entries to items by
        // position rather than by id would be caught.
        var entries = EntryTextParser.SplitSegments(rawText)
            .Select(EntryTextParser.Parse)
            .Where(parsed => parsed.ImportItemId is null || parsed.ImportItemId != LeaveOut)
            .Select(parsed => Entry(Guid.NewGuid(), parsed.Title) with { ImportItemId = parsed.ImportItemId, RepoIds = parsed.RepoIds })
            .Reverse()
            .ToList();
        Created.AddRange(entries);

        return Task.FromResult(Result.Success(new ImportPlanResultDto(entries.Count, 0, 0, 0, 0, entries)));
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<Result<TaskItemDto>> LinkToIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<TaskItemDto>> UnlinkFromIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<TaskItemDto>> SetDevbookReferencesAsync(Guid id, IReadOnlyList<string> references, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(0));

    public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(0));

    public Task<Result<int>> RefileRepositoryTasksAsync(string repositoryId, string? moveToId, bool drop, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(0));

    private static TaskItemDto Entry(Guid id, string title) =>
        new(id, title, string.Empty, EntryType.Task, Priority.Medium, EntryStatus.Draft, null, [], 0, 0, 0, []);
}
