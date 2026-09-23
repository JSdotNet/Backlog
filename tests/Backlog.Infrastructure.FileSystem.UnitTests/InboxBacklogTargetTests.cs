using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The one place an inbox item becomes entry text. What these pin is that the
/// text the adapter writes is text Tasks' own parser reads back to the same
/// facts — title, tags, repository, body — and that the routing rules the port
/// promises (one entry per repository, the item's id as provenance) are what
/// the Tasks facade actually receives.
/// </summary>
public sealed class InboxBacklogTargetTests
{
    private static readonly Guid Item = Guid.Parse("0199a3f2-7c41-7d1a-9d0f-3d2c5e6f7a8b");

    [Fact]
    public void The_composed_text_parses_back_to_the_items_facts()
    {
        var request = new InboxRouteRequestDto(
            Item,
            "Ship the thing",
            "Some notes about it.",
            "https://example.com/spec.pdf",
            ["Deploy", "#infra"],
            ["JSdotNet/Backlog"]);

        var parsed = EntryTextParser.Parse(InboxBacklogTarget.Compose(request, "JSdotNet/Backlog"));

        Assert.Equal("Ship the thing", parsed.Title);
        Assert.Equal(EntryType.Task, parsed.Type);
        Assert.Equal(EntryStatus.Draft, parsed.Status);
        Assert.Equal(["deploy", "infra"], parsed.Tags);
        Assert.Equal(["JSdotNet/Backlog"], parsed.RepoIds);
        Assert.Contains("Some notes about it.", parsed.Body, StringComparison.Ordinal);
        Assert.Contains("Source: https://example.com/spec.pdf", parsed.Body, StringComparison.Ordinal);
        Assert.Empty(parsed.Unreadable ?? []);
    }

    /// <summary>The parser reads line two as the metadata line, so a title with
    /// a line break in it would put half the title where the tokens go and the
    /// tokens into the body. Every whitespace run becomes one space.</summary>
    [Fact]
    public void A_title_with_line_breaks_still_parses_back_as_one_title_line()
    {
        var request = new InboxRouteRequestDto(
            Item,
            "Ship the\nthing\r\n  before   Friday",
            "Notes.",
            null,
            ["deploy"],
            []);

        var parsed = EntryTextParser.Parse(InboxBacklogTarget.Compose(request, null));

        Assert.Equal("Ship the thing before Friday", parsed.Title);
        Assert.Equal(EntryType.Task, parsed.Type);
        Assert.Equal(EntryStatus.Draft, parsed.Status);
        Assert.Equal(["deploy"], parsed.Tags);
        Assert.Equal("Notes.", parsed.Body.Trim());
        Assert.Empty(parsed.Unreadable ?? []);
    }

    [Fact]
    public void Without_a_repository_no_repo_token_is_written()
    {
        var request = new InboxRouteRequestDto(Item, "Call the dentist", string.Empty, null, [], []);

        var text = InboxBacklogTarget.Compose(request, null);
        var parsed = EntryTextParser.Parse(text);

        Assert.DoesNotContain("repo:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Source:", text, StringComparison.Ordinal);
        Assert.Empty(parsed.RepoIds ?? []);
        Assert.Equal(string.Empty, parsed.Body.Trim());
    }

    [Fact]
    public async Task Each_repository_becomes_its_own_entry_in_rank_order_with_the_items_id()
    {
        var tasks = new RecordingTaskItems(existing: 3);
        var target = new InboxBacklogTarget(tasks);
        var request = new InboxRouteRequestDto(Item, "Ship the thing", string.Empty, null, [], ["JSdotNet/Backlog", "JSdotNet/Other"]);

        var result = await target.CreateTasksAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(tasks.Saves.Select(save => save.Id), result.Value);

        Assert.Equal([3, 4], tasks.Saves.Select(save => save.Order));
        Assert.All(tasks.Saves, save => Assert.Equal(Item.ToString("D"), save.SourceInboxId));
        Assert.Equal(
            ["JSdotNet/Backlog", "JSdotNet/Other"],
            tasks.Saves.Select(save => EntryTextParser.Parse(save.RawText).RepoIds!.Single()));
    }

    [Fact]
    public async Task No_repository_makes_exactly_one_entry()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        var request = new InboxRouteRequestDto(Item, "Call the dentist", string.Empty, null, [], []);

        var result = await new InboxBacklogTarget(tasks).CreateTasksAsync(request, TestContext.Current.CancellationToken);

        Assert.Single(result.Value);
        Assert.Equal(0, Assert.Single(tasks.Saves).Order);
    }

    [Fact]
    public async Task A_failure_part_way_names_what_was_already_created()
    {
        var tasks = new RecordingTaskItems(existing: 0) { FailOnSave = 2 };
        var request = new InboxRouteRequestDto(Item, "Ship it", string.Empty, null, [], ["a/one", "a/two"]);

        var result = await new InboxBacklogTarget(tasks).CreateTasksAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("entry.needs_title", result.Error.Code);
        Assert.Contains("1 of 2", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains(tasks.Saves[0].Id.ToString("D"), result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plan_is_imported_with_the_items_id_and_answers_with_every_entry()
    {
        var tasks = new RecordingTaskItems(existing: 0);

        var result = await new InboxBacklogTarget(tasks).ImportPlanAsync(
            "# One\n`prompt`\n\n# Two\n`prompt`\n", Item, allowedRepoIds: [], TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);

        var import = Assert.Single(tasks.Imports);
        Assert.Equal(Item.ToString("D"), import.SourceInboxId);
        Assert.Null(import.DefaultRepo);
    }

    /// <summary>The one thing about a drafted plan the adapter checks itself:
    /// Tasks' import registers a repository it has never heard of, so a model
    /// that writes one the item does not name must be stopped before Tasks is
    /// asked at all — no entries, and nothing for the registry to learn.</summary>
    [Fact]
    public async Task A_plan_naming_a_repository_the_item_does_not_have_is_refused_before_tasks_sees_it()
    {
        var tasks = new RecordingTaskItems(existing: 0);

        var result = await new InboxBacklogTarget(tasks).ImportPlanAsync(
            "# One\n`prompt` `repo:JSdotNet/Backlog`\n\n# Two\n`prompt` `repo:evil/x`\n",
            Item,
            allowedRepoIds: ["JSdotNet/Backlog"],
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("inbox.plan.unknown_repository", result.Error.Code);
        Assert.Contains("evil/x", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(tasks.Imports);
        Assert.Empty(tasks.Saves);
    }

    [Fact]
    public async Task A_plan_naming_only_the_items_repositories_is_imported_whatever_their_case()
    {
        var tasks = new RecordingTaskItems(existing: 0);

        var result = await new InboxBacklogTarget(tasks).ImportPlanAsync(
            "# One\n`prompt` `repo:jsdotnet/backlog`\n\n# Two\n`prompt` `repo:JSdotNet/Other`\n",
            Item,
            allowedRepoIds: ["JSdotNet/Backlog", "jsdotnet/other"],
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Single(tasks.Imports);
    }

    /// <summary>The Tasks facade, recording what it is asked and answering the
    /// way the real one would in shape: a saved entry with a fresh id, and an
    /// import result naming one entry per top-level heading.</summary>
    private sealed class RecordingTaskItems(int existing) : ITaskItems
    {
        public List<(Guid Id, string RawText, int Order, string? SourceInboxId)> Saves { get; } = [];

        public List<(string RawText, string? DefaultRepo, string? SourceInboxId)> Imports { get; } = [];

        /// <summary>Which save, counted from one, answers with a failure.</summary>
        public int? FailOnSave { get; init; }

        public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItemDto>>([.. Enumerable.Range(0, existing).Select(i => Entry(Guid.NewGuid(), $"Existing {i}"))]);

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

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(string rawText, string? defaultRepo = null, IReadOnlyDictionary<string, string>? repoMatches = null, string? sourceInboxId = null, bool layOutOnRoadmap = false, CancellationToken cancellationToken = default)
        {
            Imports.Add((rawText, defaultRepo, sourceInboxId));

            var entries = EntryTextParser.SplitSegments(rawText)
                .Select(EntryTextParser.Parse)
                .Select(parsed => Entry(Guid.NewGuid(), parsed.Title))
                .ToList();

            return Task.FromResult(Result.Success(new ImportPlanResultDto(entries.Count, 0, 0, 0, 0, entries)));
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Result<TaskItemDto>> LinkToIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(0));

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(0));

        private static TaskItemDto Entry(Guid id, string title) =>
            new(id, title, string.Empty, EntryType.Task, Priority.Medium, EntryStatus.Draft, null, [], 0, 0, 0, []);
    }
}
