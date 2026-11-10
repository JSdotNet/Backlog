using Backlog.Infrastructure.FileSystem.Events;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Events' task target over Tasks' published <see cref="ITaskItems"/>
/// (<c>.devbook/domain/events/requirements.md#prep-tasks</c>): a prep task is filed with no
/// repository, so it lands under the Brain scope, and the text Tasks receives says nothing about
/// the event — and nothing a title holds becomes a step, a second entry or a fence.
/// </summary>
public sealed class EventsTaskTargetTests
{
    [Fact]
    public void A_prep_task_parses_back_to_its_title_alone()
    {
        var parsed = EntryTextParser.Parse(EventsTaskTarget.Compose("Book hotel in Antwerp"));

        Assert.Equal("Book hotel in Antwerp", parsed.Title);
        Assert.Equal(EntryType.Task, parsed.Type);
        Assert.Empty(parsed.RepoIds ?? []);
        Assert.Empty(parsed.Tags);
        Assert.Equal(string.Empty, parsed.Body.Trim());
        Assert.Empty(parsed.Unreadable ?? []);
    }

    [Theory]
    [InlineData("Book hotel\n## in Antwerp")]
    [InlineData("Book hotel\n- [ ] in Antwerp")]
    [InlineData("Book hotel\n```\nin Antwerp")]
    [InlineData("Book hotel\n# in Antwerp")]
    public void Nothing_a_title_holds_becomes_structure(string title)
    {
        var text = EventsTaskTarget.Compose(title);

        Assert.Single(EntryTextParser.SplitSegments(text));
        Assert.Equal(0, EntryTextParser.CountSubItems(text));
        Assert.DoesNotContain('\n', EntryTextParser.Parse(text).Title);
        Assert.Equal(string.Empty, EntryTextParser.Parse(text).Body.Trim());
    }

    [Fact]
    public async Task Filing_appends_after_the_backlog_with_no_repository_and_answers_the_id()
    {
        var tasks = new RecordingTasks(existing: 4);
        var target = new EventsTaskTarget(tasks, new Signal());

        var filed = await target.FileAsync("Book hotel in Antwerp", TestContext.Current.CancellationToken);

        var save = Assert.Single(tasks.Saves);
        Assert.Equal(save.Id.ToString("D"), filed.Value);
        Assert.Null(save.EntryId);
        Assert.Equal(4, save.Order);
        Assert.Null(save.SourceInboxId);
        Assert.DoesNotContain("repo:", save.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_save_is_answered_as_it_came()
    {
        var tasks = new RecordingTasks(existing: 0) { Refuse = true };
        var target = new EventsTaskTarget(tasks, new Signal());

        var filed = await target.FileAsync("Book hotel", TestContext.Current.CancellationToken);

        Assert.Equal("entry.needs_title", filed.Error.Code);
    }

    [Fact]
    public async Task A_lookup_answers_the_held_tasks_with_their_status_in_words()
    {
        var tasks = new RecordingTasks(existing: 0);
        var hotel = tasks.Hold("Book hotel", EntryStatus.InProgress);
        var budget = tasks.Hold("Ask about training budget", EntryStatus.Done);
        var target = new EventsTaskTarget(tasks, new Signal());

        var found = await target.LookupAsync(
            [hotel.ToString("D"), budget.ToString("D").ToUpperInvariant(), Guid.NewGuid().ToString("D"), "not-a-task"],
            TestContext.Current.CancellationToken);

        Assert.Equal(2, found.Count);
        Assert.Equal(new EventTaskFactsDto(hotel.ToString("D"), "Book hotel", "In progress"), found[hotel.ToString("D")]);
        Assert.Equal("Done", found[budget.ToString("D").ToUpperInvariant()].Status);
    }

    [Fact]
    public void A_task_written_anywhere_on_this_machine_is_heard()
    {
        var signal = new Signal();
        var target = new EventsTaskTarget(new RecordingTasks(existing: 0), signal);
        var heard = 0;
        void Listen() => heard++;

        target.Changed += Listen;
        signal.Raise();
        target.Changed -= Listen;
        signal.Raise();

        Assert.Equal(1, heard);
    }

    private sealed class Signal : ITaskChangeSignal
    {
        public event Action? Changed;

        public void Raise() => Changed?.Invoke();

        public IDisposable Suppress() => throw new NotSupportedException();
    }

    /// <summary>The Tasks facade at the boundary: what it was asked to save, and the entries it holds.</summary>
    private sealed class RecordingTasks(int existing) : ITaskItems
    {
        private readonly List<TaskItemDto> _held = [.. Enumerable.Range(0, existing).Select(i => Entry(Guid.NewGuid(), $"Existing {i}", EntryStatus.Ready))];

        public List<(Guid Id, Guid? EntryId, string RawText, int Order, string? SourceInboxId)> Saves { get; } = [];

        public bool Refuse { get; init; }

        public Guid Hold(string title, EntryStatus status)
        {
            var id = Guid.NewGuid();
            _held.Add(Entry(id, title, status));
            return id;
        }

        public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItemDto>>([.. _held]);

        public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid? id, string rawText, int order, string? sourceInboxId = null, CancellationToken cancellationToken = default)
        {
            if (Refuse) return Task.FromResult(Result.Failure<SavedTaskDto>(Error.Validation("entry.needs_title", "An entry needs a title line before it can be saved.")));

            var created = Guid.NewGuid();
            Saves.Add((created, id, rawText, order, sourceInboxId));
            return Task.FromResult(Result.Success(new SavedTaskDto(Entry(created, EntryTextParser.Parse(rawText).Title, EntryStatus.Draft))));
        }

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(string rawText, string? defaultRepo = null, IReadOnlyDictionary<string, string>? repoMatches = null, string? sourceInboxId = null, IReadOnlyDictionary<string, string>? sourceInboxIds = null, bool layOutOnRoadmap = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<TaskItemDto>> LinkToIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<TaskItemDto>> UnlinkFromIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<TaskItemDto>> SetDevbookReferencesAsync(Guid id, IReadOnlyList<string> references, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> RefileRepositoryTasksAsync(string repositoryId, string? moveToId, bool drop, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static TaskItemDto Entry(Guid id, string title, EntryStatus status) =>
            new(id, title, string.Empty, EntryType.Task, Priority.Medium, status, null, [], 0, 0, 0, []);
    }
}
