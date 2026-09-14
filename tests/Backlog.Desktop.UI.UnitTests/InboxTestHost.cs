using Backlog.Desktop.UI.Inbox;
using Backlog.Infrastructure.GitHub;
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
        DateTimeOffset? capturedAt = null)
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
            null);

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

    public Task<Result<InboxItemDto>> CaptureAsync(string title, string channel = "manual", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title)) return Task.FromResult(Result.Failure<InboxItemDto>(InboxErrors.ItemNeedsTitle));

        var next = NextCapture ?? (ContentKind.Text, "text", null);
        NextCapture = null;

        var item = Seed(title.Trim(), next.Kind, next.Slug, channel, sourceUrl: next.SourceUrl);
        return Task.FromResult(Result.Success(item));
    }

    public Task<Result> SetTagsAsync(Guid id, IReadOnlyList<string> tags, CancellationToken cancellationToken = default) =>
        Update(id, item => item with { Tags = [.. tags.Select(tag => new InboxTagDto(tag, false))] });

    public Task<Result> AssignRepositoriesAsync(Guid id, IReadOnlyList<string> repoIds, CancellationToken cancellationToken = default) =>
        Update(id, item => item with { RepoIds = repoIds });

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
            : item with { Status = InboxStatus.Archived });

    public async Task<Result<InboxRoutedDto>> RouteToBacklogAsync(Guid id, CancellationToken cancellationToken = default)
    {
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
