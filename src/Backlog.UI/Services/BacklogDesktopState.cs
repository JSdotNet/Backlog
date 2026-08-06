using Backlog.Domain;
using Backlog.Storage;

namespace Backlog.UI.Services;

public sealed class BacklogDesktopState
{
    private readonly IBacklogRepository _repository;
    private BacklogEntry? _entry;

    public BacklogDesktopState(IBacklogRepository repository)
    {
        _repository = repository;
    }

    public IReadOnlyList<EntryType> Types { get; } = Enum.GetValues<EntryType>();

    public IReadOnlyList<Priority> Priorities { get; } = Enum.GetValues<Priority>();

    public List<StatusFilterOption> StatusFilters { get; } =
    [
        new("All", string.Empty),
        new("Draft", "draft"),
        new("Ready", "ready"),
        new("In progress", "in_progress"),
        new("Done", "done"),
        new("Archived", "archived")
    ];

    public List<SummaryRow> FilteredEntries { get; private set; } = [];

    public List<SummaryRow> AllEntries { get; private set; } = [];

    public List<EntryStatus> AvailableStatusTargets { get; private set; } = [];

    public List<SubItemRow> SubItems { get; private set; } = [];

    public string SelectedStatusFilterWire { get; private set; } = string.Empty;

    public Guid? SelectedEntryId { get; private set; }

    public bool HasSelection => SelectedEntryId.HasValue;

    public bool HasEditor { get; private set; }

    public bool IsPersisted => _entry is not null;

    public string Title { get; set; } = string.Empty;

    public string ContentMd { get; set; } = string.Empty;

    public string TagsText { get; set; } = string.Empty;

    public EntryType SelectedType { get; set; } = EntryType.Task;

    public Priority SelectedPriority { get; set; } = Priority.Medium;

    public EntryStatus? SelectedStatusTarget { get; set; }

    public string NewSubItemTitle { get; set; } = string.Empty;

    public string StatusText => _entry is null ? "(unsaved)" : FormatStatus(_entry.Status);

    public string ProgressText => _entry is null
        ? "0/0"
        : $"{_entry.CompletedSubItemCount}/{_entry.TotalSubItemCount}";

    public async Task InitializeAsync()
    {
        await ReloadAsync();
    }

    public bool IsSelected(Guid id) => SelectedEntryId == id;

    public async Task SelectEntryAsync(Guid id)
    {
        var entry = await _repository.GetAsync(id);
        if (entry is null)
        {
            return;
        }

        SelectedEntryId = id;
        _entry = entry;
        PopulateEditorFromEntry(entry);
        RefreshPersistedDetails();
    }

    public void NewEntry()
    {
        SelectedEntryId = null;
        _entry = null;
        HasEditor = true;
        Title = string.Empty;
        ContentMd = string.Empty;
        TagsText = string.Empty;
        SelectedType = EntryType.Task;
        SelectedPriority = Priority.Medium;
        SelectedStatusTarget = null;
        NewSubItemTitle = string.Empty;
        AvailableStatusTargets = [];
        SubItems = [];
    }

    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            return;
        }

        var tags = ParseTags(TagsText);

        if (_entry is null)
        {
            _entry = new BacklogEntry(Title.Trim(), ContentMd ?? string.Empty, SelectedType, SelectedPriority, tags: tags);
        }
        else
        {
            _entry.Rename(Title.Trim());
            _entry.UpdateContent(ContentMd ?? string.Empty);
            _entry.ChangeType(SelectedType);
            _entry.ChangePriority(SelectedPriority);
            _entry.SetTags(tags);
        }

        await _repository.SaveAsync(_entry);
        SelectedEntryId = _entry.Id;
        await ReloadAsync();
        RefreshPersistedDetails();
    }

    public async Task DeleteSelectedAsync()
    {
        if (SelectedEntryId is not { } selectedId)
        {
            return;
        }

        await _repository.DeleteAsync(selectedId);
        SelectedEntryId = null;
        _entry = null;
        HasEditor = false;
        await ReloadAsync();
        AvailableStatusTargets = [];
        SubItems = [];
    }

    public void SetStatusFilter(string? wire)
    {
        SelectedStatusFilterWire = wire ?? string.Empty;
        ApplyFilter();
    }

    public async Task ApplyStatusAsync()
    {
        if (_entry is null || SelectedStatusTarget is not { } target || !_entry.CanChangeStatusTo(target))
        {
            return;
        }

        _entry.ChangeStatus(target);
        await PersistEntryChangeAsync();
    }

    public async Task AddSubItemAsync()
    {
        if (_entry is null || string.IsNullOrWhiteSpace(NewSubItemTitle))
        {
            return;
        }

        _entry.AddSubItem(NewSubItemTitle.Trim());
        NewSubItemTitle = string.Empty;
        await PersistEntryChangeAsync();
    }

    public async Task ToggleSubItemAsync(Guid subItemId)
    {
        if (_entry is null)
        {
            return;
        }

        _entry.ToggleSubItem(subItemId);
        await PersistEntryChangeAsync();
    }

    public async Task RemoveSubItemAsync(Guid subItemId)
    {
        if (_entry is null)
        {
            return;
        }

        _entry.RemoveSubItem(subItemId);
        await PersistEntryChangeAsync();
    }

    public async Task MoveSubItemUpAsync(Guid subItemId)
    {
        if (_entry is null)
        {
            return;
        }

        var item = _entry.SubItems.FirstOrDefault(x => x.Id == subItemId);
        if (item is null || item.Order <= 0)
        {
            return;
        }

        _entry.ReorderSubItem(subItemId, item.Order - 1);
        await PersistEntryChangeAsync();
    }

    public async Task MoveSubItemDownAsync(Guid subItemId)
    {
        if (_entry is null)
        {
            return;
        }

        var item = _entry.SubItems.FirstOrDefault(x => x.Id == subItemId);
        if (item is null || item.Order >= _entry.TotalSubItemCount - 1)
        {
            return;
        }

        _entry.ReorderSubItem(subItemId, item.Order + 1);
        await PersistEntryChangeAsync();
    }

    public string FormatStatus(EntryStatus status) => status switch
    {
        EntryStatus.Draft => "Draft",
        EntryStatus.Ready => "Ready",
        EntryStatus.InProgress => "In progress",
        EntryStatus.Done => "Done",
        EntryStatus.Archived => "Archived",
        _ => status.ToString()
    };

    public string FormatType(EntryType type) => type switch
    {
        EntryType.Prompt => "Prompt",
        EntryType.Task => "Task",
        EntryType.Idea => "Idea",
        EntryType.FollowUp => "Follow-up",
        _ => type.ToString()
    };

    public string FormatPriority(Priority priority) => priority.ToString();

    private async Task PersistEntryChangeAsync()
    {
        await _repository.SaveAsync(_entry!);
        await ReloadAsync();
        RefreshPersistedDetails();
    }

    private async Task ReloadAsync()
    {
        var summaries = await _repository.ListAsync();

        AllEntries = summaries
            .Select(x => new SummaryRow(
                x.Id,
                x.Title,
                x.Type,
                x.Status,
                x.Priority,
                $"{x.CompletedSubItems}/{x.TotalSubItems}"))
            .ToList();

        ApplyFilter();

        if (SelectedEntryId is { } selectedId && FilteredEntries.All(x => x.Id != selectedId))
        {
            SelectedEntryId = null;
        }
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SelectedStatusFilterWire))
        {
            FilteredEntries = [.. AllEntries];
            return;
        }

        FilteredEntries =
        [
            .. AllEntries.Where(x =>
                string.Equals(
                    x.StatusWire,
                    SelectedStatusFilterWire,
                    StringComparison.OrdinalIgnoreCase))
        ];
    }

    private void PopulateEditorFromEntry(BacklogEntry entry)
    {
        HasEditor = true;
        Title = entry.Title;
        ContentMd = entry.ContentMd;
        TagsText = string.Join(", ", entry.Tags);
        SelectedType = entry.Type;
        SelectedPriority = entry.Priority;
        SelectedStatusTarget = null;
        NewSubItemTitle = string.Empty;
    }

    private void RefreshPersistedDetails()
    {
        if (_entry is null)
        {
            AvailableStatusTargets = [];
            SubItems = [];
            return;
        }

        AvailableStatusTargets =
        [
            .. Enum.GetValues<EntryStatus>()
                .Where(target => _entry.CanChangeStatusTo(target))
        ];

        SubItems =
        [
            .. _entry.SubItems
                .OrderBy(x => x.Order)
                .Select(x => new SubItemRow(x.Id, x.Title, x.Status == SubItemStatus.Done, x.Order))
        ];
    }

    private static IReadOnlyList<string> ParseTags(string text)
    {
        return text
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}

public sealed record StatusFilterOption(string Label, string Wire);

public sealed record SummaryRow(
    Guid Id,
    string Title,
    string TypeWire,
    string StatusWire,
    string PriorityWire,
    string ProgressText)
{
    public string TypeLabel => TypeWire;

    public string StatusLabel => CapitalizeFirst(StatusWire.Replace('_', ' '));

    public string PriorityLabel => PriorityWire;

    private static string CapitalizeFirst(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}

public sealed record SubItemRow(Guid Id, string Title, bool IsDone, int Order);
