using Backlog.Desktop.UI.Inbox;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Ports;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Composes the Inbox pane's state for a test the way a host does, over an
/// in-memory stand-in for the module.
/// <para>
/// A stand-in rather than the real module, unlike <see cref="TasksTestHost"/>,
/// because what these tests are about is the pane — which rows a slice shows,
/// what a chip does, which acts a detail offers — and the module's own rules
/// are pinned in <c>Backlog.Modules.Inbox.UnitTests</c>. A fake also answers
/// the two questions a real module cannot be made to answer on cue: whether
/// the plan drafter is available, and what routing produced.
/// </para>
/// </summary>
internal static class InboxTestHost
{
    /// <summary>Registers the state and the fake behind it. The fake is
    /// registered as itself too, so a test can seed it and read it back.</summary>
    public static FakeInboxItems AddInboxState(IServiceCollection services, FakeInboxItems? inbox = null)
    {
        var fake = inbox ?? new FakeInboxItems();

        services.AddSingleton(fake);
        services.AddSingleton<IInboxItems>(fake);
        services.AddScoped<InboxDesktopState>();

        return fake;
    }

    /// <summary>The Capture module's delivery port, answered into the same
    /// fake. Home injects the runner hard and the runner's handler takes the
    /// delivery hard, so a host that renders Home composes one, the same as the
    /// application hosts do with <c>AddCaptureAdapters()</c>. Resolved lazily
    /// so it may be registered before or after <see cref="AddInboxState"/>.</summary>
    public static IServiceCollection AddCaptureDelivery(IServiceCollection services)
    {
        services.AddSingleton<FakeInboxCaptureDelivery>(sp => new FakeInboxCaptureDelivery(sp.GetRequiredService<FakeInboxItems>()));
        services.AddSingleton<ICaptureDelivery>(sp => sp.GetRequiredService<FakeInboxCaptureDelivery>());

        return services;
    }
}

/// <summary>
/// Capture's delivery over the fake Inbox, with the one rule the shell depends
/// on mirrored: an id delivered before is already known, so a second run over
/// the same feed adds nothing.
/// </summary>
internal sealed class FakeInboxCaptureDelivery(FakeInboxItems inbox) : ICaptureDelivery
{
    private readonly HashSet<Guid> _known = [];

    public List<CaptureItem> Delivered { get; } = [];

    public Task<CaptureDeliveryOutcome> DeliverAsync(CaptureItem item, CancellationToken cancellationToken = default)
    {
        if (!_known.Add(item.Id)) return Task.FromResult(CaptureDeliveryOutcome.AlreadyKnown);

        inbox.Seed(
            item.Title,
            channel: CaptureSourceKinds.Slug(item.Kind),
            sourceUrl: item.SourceUrl,
            bodyMd: item.BodyMd ?? string.Empty,
            capturedAt: item.CapturedAt);
        Delivered.Add(item);

        return Task.FromResult(CaptureDeliveryOutcome.Delivered);
    }
}

/// <summary>A source adapter a test arms by hand: whatever is in
/// <see cref="Entries"/> is what the channel has, run after run.</summary>
internal sealed class FakeCaptureSourceAdapter(CaptureSourceKind kind) : ICaptureSourceAdapter
{
    public CaptureSourceKind Kind => kind;

    public List<CapturedEntry> Entries { get; } = [];

    public List<string> Notes { get; } = [];

    public int Runs { get; private set; }

    public Task<CaptureSourceFindings> RunAsync(MonitoredSource source, CancellationToken cancellationToken = default)
    {
        Runs++;
        return Task.FromResult(new CaptureSourceFindings([.. Entries], [.. Notes]));
    }
}

/// <summary>
/// The module's port answered from memory, with the rules the pane depends on:
/// a capture is a text item in the unfiled inbox, routing marks the item
/// triaged with one task per repository, archiving is terminal, deleting a
/// list returns its items to the inbox, and names are unique per parent.
/// </summary>
internal sealed class FakeInboxItems : IInboxItems
{
    private readonly List<InboxItemDto> _items = [];
    private readonly List<InboxListDto> _lists = [];
    private readonly List<InboxGroupDto> _groups = [];

    /// <summary>What "Create plan" can do. Unavailable by default with a reason,
    /// which is the harness's own first-run state.</summary>
    public (bool Available, string? Reason) PlanDrafterAvailability { get; set; } =
        (false, "Configure Azure Foundry in Settings to create plans.");

    /// <summary>The clock captures are stamped with; advanced by a test that
    /// cares about order.</summary>
    public DateTimeOffset Now { get; set; } = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Called when an item is routed, before the result is returned —
    /// where a test stands in for the adapter that writes the backlog.</summary>
    public Action<InboxItemDto, IReadOnlyList<Guid>>? OnRouted { get; set; }

    /// <summary>How many entries "Create plan" pretends to import.</summary>
    public int PlanEntries { get; set; } = 2;

    /// <summary>The next capture's kind and URL, for a test seeding a video or
    /// an article through the capture field. Reset after one use.</summary>
    public (ContentKind Kind, string Slug, string? SourceUrl)? NextCapture { get; set; }

    public IReadOnlyList<InboxItemDto> Items => _items;

    public IReadOnlyList<InboxListDto> Lists => _lists;

    public IReadOnlyList<InboxGroupDto> Groups => _groups;

    public int EnsureDefaultOrganizerCalls { get; private set; }

    // --- Suggestions --------------------------------------------------------

    private readonly Dictionary<Guid, List<InboxSuggestionDto>> _suggestions = [];

    /// <summary>The suggestions the reader turned down, by item and key — what
    /// the module would have recorded on the item.</summary>
    public HashSet<(Guid Id, string Key)> Dismissed { get; } = [];

    /// <summary>How many times the pane asked for suggestions.</summary>
    public int SuggestCalls { get; private set; }

    /// <summary>What Classification would propose for the item, in order. The
    /// fake answers them for an open item, less what was turned down — the two
    /// rules the pane relies on; which suggestions exist is the module's
    /// business and has its own tests.</summary>
    public InboxSuggestionDto SeedSuggestion(
        Guid id,
        InboxSuggestionKind kind,
        string value,
        string reason = "Because.",
        string? unavailableReason = null)
    {
        var prefix = kind switch
        {
            InboxSuggestionKind.Tag => "tag",
            InboxSuggestionKind.Repository => "repository",
            _ => "destination"
        };
        var suggestion = new InboxSuggestionDto($"{prefix}:{value.ToLowerInvariant()}", kind, value, reason)
        {
            UnavailableReason = unavailableReason
        };

        if (!_suggestions.TryGetValue(id, out var list)) _suggestions[id] = list = [];
        list.Add(suggestion);
        return suggestion;
    }

    public Task<Result<IReadOnlyList<InboxSuggestionDto>>> SuggestAsync(Guid id, CancellationToken cancellationToken = default)
    {
        SuggestCalls++;

        if (Find(id) is not { } item) return Task.FromResult(Result.Failure<IReadOnlyList<InboxSuggestionDto>>(InboxErrors.ItemNotFound));

        IReadOnlyList<InboxSuggestionDto> offered =
            item.Status is InboxStatus.Unprocessed or InboxStatus.Deferred && _suggestions.TryGetValue(id, out var list)
                ? [.. list.Where(suggestion => !Dismissed.Contains((id, suggestion.Key)) && !Carries(item, suggestion))]
                : [];

        return Task.FromResult(Result.Success(offered));
    }

    /// <summary>Whether the item already has what the suggestion offers — the
    /// module never offers a tag or a repository the item carries.</summary>
    private static bool Carries(InboxItemDto item, InboxSuggestionDto suggestion) => suggestion.Kind switch
    {
        InboxSuggestionKind.Tag => item.Tags.Any(tag => string.Equals(tag.Name, suggestion.Value, StringComparison.OrdinalIgnoreCase)),
        InboxSuggestionKind.Repository => item.RepoIds.Contains(suggestion.Value, StringComparer.OrdinalIgnoreCase),
        _ => false
    };

    public Task<Result> DismissSuggestionAsync(Guid id, string key, CancellationToken cancellationToken = default)
    {
        if (Find(id) is null) return Task.FromResult(Result.Failure(InboxErrors.ItemNotFound));

        Dismissed.Add((id, key));
        return Task.FromResult(Result.Success());
    }

    public InboxItemDto Seed(
        string title,
        ContentKind kind = ContentKind.Text,
        string? slug = null,
        string channel = "manual",
        string? person = null,
        string? sourceUrl = null,
        string bodyMd = "",
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<string>? repoIds = null,
        Guid? listId = null,
        InboxStatus status = InboxStatus.Unprocessed,
        DateTimeOffset? capturedAt = null,
        DateOnly? deferredUntil = null)
    {
        var at = capturedAt ?? Now;
        var item = new InboxItemDto(
            Guid.NewGuid(),
            title,
            bodyMd,
            sourceUrl,
            at,
            at,
            status,
            kind,
            slug ?? InboxEnumMap.ToWire(kind),
            channel,
            person,
            [.. (tags ?? []).Select(tag => new InboxTagDto(tag, false))],
            repoIds ?? [],
            listId,
            null,
            deferredUntil);

        _items.Add(item);
        return item;
    }

    public InboxListDto SeedList(string name, Guid? groupId = null)
    {
        var list = new InboxListDto(Guid.NewGuid(), name, groupId, _lists.Count);
        _lists.Add(list);
        return list;
    }

    public InboxGroupDto SeedGroup(string name)
    {
        var group = new InboxGroupDto(Guid.NewGuid(), name, _groups.Count);
        _groups.Add(group);
        return group;
    }

    public InboxItemDto? Find(Guid id) => _items.FirstOrDefault(item => item.Id == id);

    /// <summary>Held open until the test lets go, for the tests about two
    /// reloads in flight at once. Asked per read, so each read can be released
    /// in whatever order the test wants; null is the ordinary case where the
    /// snapshot comes straight back.</summary>
    public Func<Task>? BeforeSnapshot { get; set; }

    public async Task<InboxSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (BeforeSnapshot is { } gate) await gate();

        return new InboxSnapshotDto([.. _items], [.. _lists], [.. _groups]);
    }

    /// <summary>A refusal to hand back from the next capture, for a test about
    /// what the pane does when the module says no. Reset after one use.</summary>
    public Error? NextCaptureError { get; set; }

    public Task<Result<InboxItemDto>> CaptureAsync(string title, string? notes = null, string channel = "manual", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title)) return Task.FromResult(Result.Failure<InboxItemDto>(InboxErrors.ItemNeedsTitle));

        if (NextCaptureError is { } error)
        {
            NextCaptureError = null;
            return Task.FromResult(Result.Failure<InboxItemDto>(error));
        }

        var next = NextCapture ?? (ContentKind.Text, "text", null);
        NextCapture = null;

        // The same trim the module applies: the notes are the body, and none is
        // an empty body rather than a blank one.
        var item = Seed(
            title.Trim(),
            next.Kind,
            next.Slug,
            channel,
            sourceUrl: next.SourceUrl,
            bodyMd: string.IsNullOrWhiteSpace(notes) ? string.Empty : notes.Trim());
        return Task.FromResult(Result.Success(item));
    }

    public Task<Result> SetTagsAsync(Guid id, IReadOnlyList<string> tags, CancellationToken cancellationToken = default) =>
        Update(id, item => item with { Tags = [.. tags.Select(tag => new InboxTagDto(tag, false))] });

    public Task<Result> AssignRepositoriesAsync(Guid id, IReadOnlyList<string> repoIds, CancellationToken cancellationToken = default) =>
        Update(id, item => item with { RepoIds = repoIds });

    /// <summary>Every rename asked of the store, in order: the pass on start
    /// that follows the registry's record is asserted against this.</summary>
    public List<(string OldId, string NewId)> Renames { get; } = [];

    public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default)
    {
        Renames.Add((oldId, newId));
        return Task.FromResult(Result.Success(0));
    }

    public Task<Result> MoveToListAsync(Guid id, Guid? listId, CancellationToken cancellationToken = default)
    {
        if (listId is { } target && _lists.All(list => list.Id != target))
        {
            return Task.FromResult(Result.Failure(InboxErrors.ListNotFound));
        }

        return Update(id, item => item with { ListId = listId });
    }

    public Task<Result> ArchiveAsync(Guid id, CancellationToken cancellationToken = default) =>
        Update(id, item => item.Status == InboxStatus.Archived
            ? throw new InvalidOperationException("Already archived.")
            : item with { Status = InboxStatus.Archived, DeferredUntil = null });

    // --- Relations ----------------------------------------------------------

    private readonly Dictionary<Guid, InboxRelationsDto> _relations = [];

    /// <summary>The backlog's open tasks, as "Link to task…" is offered them
    /// after the related ones. Which tasks exist is the adapter's business.</summary>
    public List<InboxTaskOptionDto> OpenTasks { get; } = [];

    /// <summary>How many times the pane asked for an item's relations.</summary>
    public int RelatedCalls { get; private set; }

    /// <summary>What the item relates to, as the module would have found it —
    /// which relations exist is the Relation Finder's business and has its own
    /// tests; the pane only draws and acts on them.</summary>
    public void SeedRelations(Guid id, IReadOnlyList<InboxRelatedItemDto>? items = null, IReadOnlyList<InboxRelatedTaskDto>? tasks = null) =>
        _relations[id] = new InboxRelationsDto(id, items ?? [], tasks ?? []);

    public Task<Result<InboxRelationsDto>> RelatedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        RelatedCalls++;

        if (Find(id) is null) return Task.FromResult(Result.Failure<InboxRelationsDto>(InboxErrors.ItemNotFound));

        var relations = _relations.TryGetValue(id, out var seeded) ? seeded : InboxRelationsDto.None(id);
        return Task.FromResult(Result.Success(relations with { OpenTasks = [.. OpenTasks] }));
    }

    /// <summary>The module's rules, restated: never itself, the other item must
    /// exist, and only an open item.</summary>
    public Task<Result> ArchiveAsDuplicateAsync(Guid id, Guid duplicateOf, CancellationToken cancellationToken = default)
    {
        if (id == duplicateOf) return Task.FromResult(Result.Failure(InboxErrors.DuplicateOfItself));
        if (Find(duplicateOf) is null) return Task.FromResult(Result.Failure(InboxErrors.DuplicateTargetNotFound));
        if (Find(duplicateOf)!.DuplicateOf == id) return Task.FromResult(Result.Failure(InboxErrors.DuplicateCircular));
        if (Find(id) is { } item && item.Status is not (InboxStatus.Unprocessed or InboxStatus.Deferred))
        {
            return Task.FromResult(Result.Failure(InboxErrors.InvalidTransition("Only an open item can be archived as a duplicate.")));
        }

        return Update(id, item => item with { Status = InboxStatus.Archived, DeferredUntil = null, DuplicateOf = duplicateOf });
    }

    /// <summary>Every link asked of the port, by item and task.</summary>
    public List<(Guid Id, Guid TaskId)> Links { get; } = [];

    /// <summary>The module's link, restated: the item routed to that one task,
    /// nothing created.</summary>
    public Task<Result> LinkToTaskAsync(Guid id, Guid taskId, CancellationToken cancellationToken = default)
    {
        Links.Add((id, taskId));

        if (Find(id) is { } item && (item.Routing is not null || item.Status is not (InboxStatus.Unprocessed or InboxStatus.Deferred)))
        {
            return Task.FromResult(Result.Failure(InboxErrors.InvalidTransition("Only an open item can be linked.")));
        }

        return Update(id, current => current with
        {
            Status = InboxStatus.Triaged,
            DeferredUntil = null,
            Routing = new InboxRoutingDto(RoutingDomain.Tasks, current.RepoIds, [taskId], Now)
        });
    }

    /// <summary>Every id deleted, in order.</summary>
    public List<Guid> Deleted { get; } = [];

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (Find(id) is not { } item) return Task.FromResult(Result.Failure(InboxErrors.ItemNotFound));

        _items.Remove(item);
        Deleted.Add(id);
        return Task.FromResult(Result.Success());
    }

    public Task<Result> DeferAsync(Guid id, DateOnly? until, CancellationToken cancellationToken = default)
    {
        if (Find(id) is { } item && (item.Routing is not null || item.Status == InboxStatus.Archived))
        {
            return Task.FromResult(Result.Failure(InboxErrors.InvalidTransition("Cannot defer a closed item.")));
        }

        return Update(id, current => current with { Status = InboxStatus.Deferred, DeferredUntil = until });
    }

    public Task<Result> ResurfaceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (Find(id) is { Status: not InboxStatus.Deferred })
        {
            return Task.FromResult(Result.Failure(InboxErrors.InvalidTransition("Only a deferred item can return.")));
        }

        return Update(id, current => current with { Status = InboxStatus.Unprocessed, DeferredUntil = null });
    }

    /// <summary>How many times the sweep ran, so a test can assert the pane ran
    /// it on open.</summary>
    public int ResurfaceDueCalls { get; private set; }

    /// <summary>The module's sweep, restated against <see cref="Now"/>'s date.</summary>
    public Task<Result<int>> ResurfaceDueAsync(CancellationToken cancellationToken = default)
    {
        ResurfaceDueCalls++;

        var today = DateOnly.FromDateTime(Now.DateTime);
        var moved = 0;
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i] is { Status: InboxStatus.Deferred, DeferredUntil: { } until } && until <= today)
            {
                _items[i] = _items[i] with { Status = InboxStatus.Unprocessed, DeferredUntil = null };
                moved++;
            }
        }

        return Task.FromResult(Result.Success(moved));
    }

    /// <summary>Items the next batch refuses, and with what — the way a test
    /// makes one item of a selection fail its command.</summary>
    public Dictionary<Guid, Error> Refuse { get; } = [];

    /// <summary>Every batch asked of the port, by act and the ids it named.</summary>
    public List<(string Act, IReadOnlyList<Guid> Ids)> Batches { get; } = [];

    public Task<InboxBatchResultDto> SetTagsAsync(IReadOnlyDictionary<Guid, IReadOnlyList<string>> tagsByItem, CancellationToken cancellationToken = default) =>
        Batch("tags", [.. tagsByItem.Keys], id => SetTagsAsync(id, tagsByItem[id], cancellationToken));

    public Task<InboxBatchResultDto> AssignRepositoriesAsync(IReadOnlyList<Guid> ids, IReadOnlyList<string> repoIds, CancellationToken cancellationToken = default) =>
        Batch("repositories", ids, id => AssignRepositoriesAsync(id, repoIds, cancellationToken));

    public Task<InboxBatchResultDto> MoveToListAsync(IReadOnlyList<Guid> ids, Guid? listId, CancellationToken cancellationToken = default) =>
        Batch("move", ids, id => MoveToListAsync(id, listId, cancellationToken));

    public Task<InboxBatchResultDto> ArchiveAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
        Batch("archive", ids, id => ArchiveAsync(id, cancellationToken));

    /// <summary>The module's batch, restated: the single-item act per id, in
    /// order, each answer sorted into changed or refused.</summary>
    private async Task<InboxBatchResultDto> Batch(string act, IReadOnlyList<Guid> ids, Func<Guid, Task<Result>> one)
    {
        Batches.Add((act, ids));

        var changed = new List<Guid>();
        var failed = new List<InboxBatchFailureDto>();

        foreach (var id in ids)
        {
            var result = Refuse.TryGetValue(id, out var error) ? Result.Failure(error) : await one(id);

            if (result.IsSuccess) changed.Add(id);
            else failed.Add(new InboxBatchFailureDto(id, result.Error));
        }

        return new InboxBatchResultDto(changed, failed);
    }

    public async Task<Result<InboxRoutedDto>> RouteToBacklogAsync(Guid id, CancellationToken cancellationToken = default)
    {
        SingleRouteCalls++;
        if (BeforeRoute is { } gate) await gate();

        if (Find(id) is not { } item) return Result.Failure<InboxRoutedDto>(InboxErrors.ItemNotFound);
        if (item.Routing is not null) return Result.Failure<InboxRoutedDto>(InboxErrors.InvalidTransition("Already routed."));

        var taskIds = Enumerable.Range(0, Math.Max(1, item.RepoIds.Count)).Select(_ => Guid.NewGuid()).ToList();
        OnRouted?.Invoke(item, taskIds);

        await Update(id, current => current with
        {
            Status = InboxStatus.Triaged,
            Routing = new InboxRoutingDto(RoutingDomain.Tasks, current.RepoIds, taskIds, Now)
        });

        return new InboxRoutedDto(id, taskIds);
    }

    /// <summary>What Tasks refuses the next batch's document with, if anything:
    /// every item the batch would have routed is then named with the module's
    /// batch-refused error and none is routed.</summary>
    public Error? RefuseBatch { get; set; }

    /// <summary>Items the adapter would leave out of a batch's document, and
    /// why: named with their own reason while the rest still go.</summary>
    public Dictionary<Guid, Error> LeaveOutOfBatch { get; } = [];

    /// <summary>Awaited at the start of every route, single or batch, so a test
    /// can hold one in flight. Null routes straight through.</summary>
    public Func<Task>? BeforeRoute { get; set; }

    /// <summary>How many single routes were asked of the port.</summary>
    public int SingleRouteCalls { get; private set; }

    /// <summary>Every batch routed, with the ids it named, the list it was
    /// named after, the plan tag it went under, and the panel's choices.</summary>
    public List<(IReadOnlyList<Guid> Ids, Guid? ListId, string PlanTag, InboxBatchRouteChoicesDto? Choices)> BatchRoutes { get; } = [];

    /// <summary>What the proposal says the items' own text states, by the item
    /// that waits — which dependencies exist is the module's business and has
    /// its own tests; the pane only draws and sends them.</summary>
    public List<ProposedDependency> ProposedDependencies { get; } = [];

    /// <summary>The ordering hints the proposal carries, for the same reason.
    /// A hint naming an item that is not going is left out, as the module
    /// never makes one.</summary>
    public List<OrderingHint> ProposedHints { get; } = [];

    /// <summary>Every proposal asked of the port, with the ids and the list.</summary>
    public List<(IReadOnlyList<Guid> Ids, Guid? ListId)> Proposals { get; } = [];

    /// <summary>The module's proposal, restated: the tag minted as the route
    /// would, decided items refused, the seeded dependencies among the rest.</summary>
    public Task<Result<InboxBatchProposalDto>> ProposeBatchAsync(
        IReadOnlyList<Guid> ids,
        Guid? listId = null,
        CancellationToken cancellationToken = default)
    {
        Proposals.Add((ids, listId));

        if (PlanTagFor(listId) is not { } tag) return Task.FromResult(Result.Failure<InboxBatchProposalDto>(InboxErrors.ListNotFound));

        var refused = new List<InboxBatchFailureDto>();
        var routable = new List<InboxItemDto>();

        foreach (var id in ids)
        {
            if (Find(id) is not { } item) refused.Add(new InboxBatchFailureDto(id, InboxErrors.ItemNotFound));
            else if (item.Routing is not null || item.Status == InboxStatus.Archived) refused.Add(new InboxBatchFailureDto(id, InboxErrors.InvalidTransition("Already routed.")));
            else routable.Add(item);
        }

        var going = routable.Select(item => item.Id).ToHashSet();

        return Task.FromResult(Result.Success(new InboxBatchProposalDto(
            tag,
            [.. routable.Select(item => new InboxBatchProposalItemDto(item.Id, item.Title, item.RepoIds))],
            [.. ProposedDependencies.Where(dependency => going.Contains(dependency.From))],
            refused,
            listId is { } named ? _items.Count(item => item.ListId == named && item.Status == InboxStatus.Deferred) : null)
        {
            Hints = [.. ProposedHints.Where(hint => hint.Items.All(going.Contains))],
        }));
    }

    /// <summary>A new tag named after the list or <c>inbox-batch</c>, or null
    /// for a list that is gone.</summary>
    private string? PlanTagFor(Guid? listId)
    {
        var stem = "inbox-batch";
        if (listId is { } named)
        {
            if (_lists.FirstOrDefault(list => list.Id == named) is not { } list) return null;
            stem = list.Name;
        }

        return "+" + InboxPlanTag.For(stem, Guid.NewGuid());
    }

    /// <summary>The module's batch route, restated: a new tag per batch named
    /// after the list or <c>inbox-batch</c> — or the proposal's, handed back —
    /// decided items refused up front, and the rest routed together to the
    /// repositories chosen, or — on <see cref="RefuseBatch"/> — not at all.</summary>
    public async Task<Result<InboxBatchRoutedDto>> RouteToBacklogAsync(
        IReadOnlyList<Guid> ids,
        Guid? listId = null,
        InboxBatchRouteChoicesDto? choices = null,
        CancellationToken cancellationToken = default)
    {
        if (PlanTagFor(listId) is not { } minted) return Result.Failure<InboxBatchRoutedDto>(InboxErrors.ListNotFound);

        var tag = choices?.PlanTag ?? minted;
        BatchRoutes.Add((ids, listId, tag, choices));
        if (BeforeRoute is { } gate) await gate();

        var failed = new List<InboxBatchFailureDto>();
        var routable = new List<InboxItemDto>();

        // The module's merge, restated: the items folded into a kept one are
        // not sent, and are archived as its duplicates once it has gone.
        var keptFor = new Dictionary<Guid, Guid>();
        foreach (var (kept, duplicates) in choices?.Merges ?? new Dictionary<Guid, IReadOnlyList<Guid>>())
        {
            foreach (var duplicate in duplicates.Where(duplicate => duplicate != kept)) keptFor.TryAdd(duplicate, kept);
        }

        foreach (var id in ids)
        {
            if (Find(id) is not { } item) failed.Add(new InboxBatchFailureDto(id, InboxErrors.ItemNotFound));
            else if (item.Routing is not null || item.Status == InboxStatus.Archived) failed.Add(new InboxBatchFailureDto(id, InboxErrors.InvalidTransition("Already routed.")));
            else if (LeaveOutOfBatch.TryGetValue(id, out var reason)) failed.Add(new InboxBatchFailureDto(id, reason));
            else if (!keptFor.ContainsKey(id)) routable.Add(item);
        }

        if (RefuseBatch is { } refusal)
        {
            failed.AddRange(routable.Select(item => new InboxBatchFailureDto(item.Id, InboxErrors.BatchRefused(refusal))));
            return new InboxBatchRoutedDto(tag, [], failed);
        }

        var routed = new List<InboxRoutedDto>();
        foreach (var item in routable)
        {
            var repos = choices?.Repositories is { } chosen && chosen.TryGetValue(item.Id, out var picked) ? picked : item.RepoIds;
            var taskIds = Enumerable.Range(0, Math.Max(1, repos.Count)).Select(_ => Guid.NewGuid()).ToList();
            OnRouted?.Invoke(item, taskIds);

            await Update(item.Id, current => current with
            {
                Status = InboxStatus.Triaged,
                Routing = new InboxRoutingDto(RoutingDomain.Tasks, repos, taskIds, Now)
            });

            routed.Add(new InboxRoutedDto(item.Id, taskIds));
        }

        var archived = new List<Guid>();
        foreach (var (duplicate, kept) in keptFor)
        {
            if (routed.All(entry => entry.InboxItemId != kept) || Find(duplicate) is null) continue;

            await Update(duplicate, current => current with { Status = InboxStatus.Archived, DeferredUntil = null, DuplicateOf = kept });
            archived.Add(duplicate);
        }

        return new InboxBatchRoutedDto(tag, routed, failed) { Archived = archived };
    }

    public async Task<Result<InboxRoutedDto>> CreatePlanAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!PlanDrafterAvailability.Available)
        {
            return Result.Failure<InboxRoutedDto>(InboxErrors.PlanNotConfigured(PlanDrafterAvailability.Reason));
        }

        if (Find(id) is not { } item) return Result.Failure<InboxRoutedDto>(InboxErrors.ItemNotFound);

        var taskIds = Enumerable.Range(0, PlanEntries).Select(_ => Guid.NewGuid()).ToList();
        OnRouted?.Invoke(item, taskIds);

        await Update(id, current => current with
        {
            Status = InboxStatus.Triaged,
            Routing = new InboxRoutingDto(RoutingDomain.Tasks, current.RepoIds, taskIds, Now)
        });

        return new InboxRoutedDto(id, taskIds);
    }

    public Task<Result<InboxListDto>> CreateListAsync(string name, Guid? groupId = null, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return Task.FromResult(Result.Failure<InboxListDto>(InboxErrors.ListNeedsName));
        if (_lists.Any(list => list.GroupId == groupId && string.Equals(list.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(Result.Failure<InboxListDto>(InboxErrors.ListDuplicateName));
        }

        return Task.FromResult(Result.Success(SeedList(trimmed, groupId)));
    }

    public Task<Result> RenameListAsync(Guid listId, string name, CancellationToken cancellationToken = default)
    {
        var index = _lists.FindIndex(list => list.Id == listId);
        if (index < 0) return Task.FromResult(Result.Failure(InboxErrors.ListNotFound));

        var trimmed = name.Trim();
        if (trimmed.Length == 0) return Task.FromResult(Result.Failure(InboxErrors.ListNeedsName));

        var current = _lists[index];
        if (_lists.Any(list => list.Id != listId && list.GroupId == current.GroupId && string.Equals(list.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(Result.Failure(InboxErrors.ListDuplicateName));
        }

        _lists[index] = current with { Name = trimmed };
        return Task.FromResult(Result.Success());
    }

    public Task<Result> DeleteListAsync(Guid listId, CancellationToken cancellationToken = default)
    {
        if (_lists.RemoveAll(list => list.Id == listId) == 0) return Task.FromResult(Result.Failure(InboxErrors.ListNotFound));

        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].ListId == listId) _items[i] = _items[i] with { ListId = null };
        }

        return Task.FromResult(Result.Success());
    }

    public Task<Result> MoveListToGroupAsync(Guid listId, Guid? groupId, CancellationToken cancellationToken = default)
    {
        var index = _lists.FindIndex(list => list.Id == listId);
        if (index < 0) return Task.FromResult(Result.Failure(InboxErrors.ListNotFound));
        if (groupId is { } target && _groups.All(group => group.Id != target)) return Task.FromResult(Result.Failure(InboxErrors.GroupNotFound));

        _lists[index] = _lists[index] with { GroupId = groupId };
        return Task.FromResult(Result.Success());
    }

    public Task<Result<InboxGroupDto>> CreateGroupAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return Task.FromResult(Result.Failure<InboxGroupDto>(InboxErrors.GroupNeedsName));
        if (_groups.Any(group => string.Equals(group.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(Result.Failure<InboxGroupDto>(InboxErrors.GroupDuplicateName));
        }

        return Task.FromResult(Result.Success(SeedGroup(trimmed)));
    }

    public Task<Result> RenameGroupAsync(Guid groupId, string name, CancellationToken cancellationToken = default)
    {
        var index = _groups.FindIndex(group => group.Id == groupId);
        if (index < 0) return Task.FromResult(Result.Failure(InboxErrors.GroupNotFound));

        var trimmed = name.Trim();
        if (trimmed.Length == 0) return Task.FromResult(Result.Failure(InboxErrors.GroupNeedsName));

        _groups[index] = _groups[index] with { Name = trimmed };
        return Task.FromResult(Result.Success());
    }

    public Task<Result> UngroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (_groups.RemoveAll(group => group.Id == groupId) == 0) return Task.FromResult(Result.Failure(InboxErrors.GroupNotFound));

        for (var i = 0; i < _lists.Count; i++)
        {
            if (_lists[i].GroupId == groupId) _lists[i] = _lists[i] with { GroupId = null };
        }

        return Task.FromResult(Result.Success());
    }

    // --- Attachments --------------------------------------------------------

    /// <summary>The bytes each downloaded attachment reads as, by id.</summary>
    public Dictionary<Guid, byte[]> AttachmentBytes { get; } = [];

    /// <summary>Every open and retry asked for, in order, by attachment id.</summary>
    public List<Guid> Opened { get; } = [];

    public List<Guid> Retried { get; } = [];

    /// <summary>What the next Retry answers: null downloads the file, an error
    /// keeps it failed with that reason. Reset after one use.</summary>
    public Error? NextRetryError { get; set; }

    /// <summary>Puts files on a seeded item, the way the intake would have.</summary>
    public InboxItemDto SeedAttachments(Guid itemId, params InboxAttachmentDto[] attachments)
    {
        var index = _items.FindIndex(item => item.Id == itemId);
        _items[index] = _items[index] with { Attachments = [.. _items[index].Attachments, .. attachments] };
        return _items[index];
    }

    public async Task<Result> RetryAttachmentAsync(Guid id, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        Retried.Add(attachmentId);

        var error = NextRetryError;
        NextRetryError = null;

        var updated = await Update(id, item => item with
        {
            Attachments = [.. item.Attachments.Select(attachment => attachment.Id != attachmentId
                ? attachment
                : attachment with { Downloaded = error is null, LastError = error?.Message })],
        });

        return updated.IsFailure || error is null ? updated : Result.Failure(error.Value);
    }

    public Task<Result<byte[]>> ReadAttachmentAsync(Guid id, Guid attachmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(AttachmentBytes.TryGetValue(attachmentId, out var bytes)
            ? Result.Success(bytes)
            : Result.Failure<byte[]>(InboxErrors.AttachmentNotDownloaded));

    public Task<Result> OpenAttachmentAsync(Guid id, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        Opened.Add(attachmentId);
        return Task.FromResult(Result.Success());
    }

    /// <summary>The module's seed, restated: the starter groups and lists, only
    /// when there are none of either.</summary>
    public Task EnsureDefaultOrganizerAsync(CancellationToken cancellationToken = default)
    {
        EnsureDefaultOrganizerCalls++;

        if (_lists.Count == 0 && _groups.Count == 0)
        {
            SeedGroup("Areas");
            SeedGroup("Projects");
            SeedGroup("Archive");
            SeedList("Resources");
            SeedList("Someday/Maybe");
            SeedList("Updates");
            SeedList("Wishlist");
        }

        return Task.CompletedTask;
    }

    private Task<Result> Update(Guid id, Func<InboxItemDto, InboxItemDto> change)
    {
        var index = _items.FindIndex(item => item.Id == id);
        if (index < 0) return Task.FromResult(Result.Failure(InboxErrors.ItemNotFound));

        _items[index] = change(_items[index]);
        return Task.FromResult(Result.Success());
    }
}
