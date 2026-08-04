using System.Collections.ObjectModel;
using Backlog.Domain;
using Backlog.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Backlog_Desktop.ViewModels;

/// <summary>
/// Detail editor for a single backlog entry. New entries capture title/content/
/// type/priority/tags; once persisted, status transitions and sub-item editing
/// become available (they require the aggregate to enforce invariants).
/// </summary>
public partial class EntryEditViewModel : ObservableObject
{
    private readonly IBacklogRepository _repository;
    private readonly Func<Task> _onChanged;
    private BacklogEntry? _entry;

    public EntryEditViewModel(IBacklogRepository repository, Func<Task> onChanged, BacklogEntry? entry)
    {
        _repository = repository;
        _onChanged = onChanged;
        _entry = entry;

        Types = Enum.GetValues<EntryType>();
        Priorities = Enum.GetValues<Priority>();

        if (entry is null)
        {
            Title = string.Empty;
            ContentMd = string.Empty;
            TagsText = string.Empty;
            SelectedType = EntryType.Task;
            SelectedPriority = Priority.Medium;
        }
        else
        {
            Title = entry.Title;
            ContentMd = entry.ContentMd;
            TagsText = string.Join(", ", entry.Tags);
            SelectedType = entry.Type;
            SelectedPriority = entry.Priority;
        }

        RefreshFromEntry();
    }

    public IReadOnlyList<EntryType> Types { get; }

    public IReadOnlyList<Priority> Priorities { get; }

    public ObservableCollection<SubItemViewModel> SubItems { get; } = new();

    public ObservableCollection<EntryStatus> AvailableStatusTargets { get; } = new();

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string ContentMd { get; set; }

    [ObservableProperty]
    public partial string TagsText { get; set; }

    [ObservableProperty]
    public partial EntryType SelectedType { get; set; }

    [ObservableProperty]
    public partial Priority SelectedPriority { get; set; }

    [ObservableProperty]
    public partial EntryStatus? SelectedStatusTarget { get; set; }

    [ObservableProperty]
    public partial string NewSubItemTitle { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPersisted))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial bool HasEntry { get; set; }

    public bool IsPersisted => _entry is not null;

    public string StatusText => _entry is null ? "(unsaved)" : Format.Status(_entry.Status);

    public string ProgressText =>
        _entry is null ? "0/0" : $"{_entry.CompletedSubItemCount}/{_entry.TotalSubItemCount}";

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
            return;

        var tags = ParseTags(TagsText);

        if (_entry is null)
        {
            _entry = new BacklogEntry(Title.Trim(), ContentMd ?? string.Empty, SelectedType,
                SelectedPriority, repoIds: null, tags: tags);
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
        RefreshFromEntry();
        await _onChanged();
    }

    [RelayCommand]
    private async Task ApplyStatusAsync()
    {
        if (_entry is null || SelectedStatusTarget is not { } target)
            return;
        if (!_entry.CanChangeStatusTo(target))
            return;

        _entry.ChangeStatus(target);
        await _repository.SaveAsync(_entry);
        RefreshFromEntry();
        await _onChanged();
    }

    [RelayCommand]
    private async Task AddSubItemAsync()
    {
        if (_entry is null || string.IsNullOrWhiteSpace(NewSubItemTitle))
            return;

        _entry.AddSubItem(NewSubItemTitle.Trim());
        NewSubItemTitle = string.Empty;
        await PersistAndRefreshAsync();
    }

    [RelayCommand]
    private async Task ToggleSubItemAsync(SubItemViewModel? item)
    {
        if (_entry is null || item is null) return;
        _entry.ToggleSubItem(item.Id);
        await PersistAndRefreshAsync();
    }

    [RelayCommand]
    private async Task RemoveSubItemAsync(SubItemViewModel? item)
    {
        if (_entry is null || item is null) return;
        _entry.RemoveSubItem(item.Id);
        await PersistAndRefreshAsync();
    }

    [RelayCommand]
    private async Task MoveSubItemUpAsync(SubItemViewModel? item)
    {
        if (_entry is null || item is null || item.Order <= 0) return;
        _entry.ReorderSubItem(item.Id, item.Order - 1);
        await PersistAndRefreshAsync();
    }

    [RelayCommand]
    private async Task MoveSubItemDownAsync(SubItemViewModel? item)
    {
        if (_entry is null || item is null || item.Order >= _entry.TotalSubItemCount - 1) return;
        _entry.ReorderSubItem(item.Id, item.Order + 1);
        await PersistAndRefreshAsync();
    }

    private async Task PersistAndRefreshAsync()
    {
        await _repository.SaveAsync(_entry!);
        RefreshFromEntry();
        await _onChanged();
    }

    private void RefreshFromEntry()
    {
        HasEntry = _entry is not null;

        SubItems.Clear();
        AvailableStatusTargets.Clear();
        SelectedStatusTarget = null;

        if (_entry is not null)
        {
            foreach (var s in _entry.SubItems.OrderBy(s => s.Order))
                SubItems.Add(new SubItemViewModel(s.Id, s.Title, s.Status == SubItemStatus.Done, s.Notes, s.Order));

            foreach (var target in Enum.GetValues<EntryStatus>())
                if (_entry.CanChangeStatusTo(target))
                    AvailableStatusTargets.Add(target);
        }

        OnPropertyChanged(nameof(IsPersisted));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ProgressText));
    }

    private static List<string> ParseTags(string text) =>
        (text ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
}
