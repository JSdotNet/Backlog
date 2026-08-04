using System.Collections.ObjectModel;
using Backlog.Domain;
using Backlog.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Backlog_Desktop.ViewModels;

/// <summary>Display row for the master list, derived from a persisted summary.</summary>
public sealed class SummaryRowViewModel
{
    public SummaryRowViewModel(BacklogEntrySummary summary)
    {
        Id = summary.Id;
        Title = summary.Title;
        TypeLabel = summary.Type;
        StatusLabel = summary.Status.Replace('_', ' ');
        PriorityLabel = summary.Priority;
        ProgressText = $"{summary.CompletedSubItems}/{summary.TotalSubItems}";
        StatusWire = summary.Status;
        Subtitle = $"{TypeLabel} \u2022 {StatusLabel} \u2022 {PriorityLabel}   {ProgressText}";
    }

    public Guid Id { get; }
    public string Title { get; }
    public string TypeLabel { get; }
    public string StatusLabel { get; }
    public string PriorityLabel { get; }
    public string ProgressText { get; }
    public string StatusWire { get; }
    public string Subtitle { get; }
}

/// <summary>A selectable status filter for the master list.</summary>
public sealed record StatusFilterOption(string Label, string? Wire);

/// <summary>
/// Shell view model: owns the repository, the filtered master list, the current
/// selection, and the detail editor.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IBacklogRepository _repository;

    public MainViewModel(IBacklogRepository repository)
    {
        _repository = repository;

        StatusFilters = new List<StatusFilterOption>
        {
            new("All", null),
            new("Draft", "draft"),
            new("Ready", "ready"),
            new("In progress", "in_progress"),
            new("Done", "done"),
            new("Archived", "archived"),
        };
        SelectedStatusFilter = StatusFilters[0];
    }

    public ObservableCollection<SummaryRowViewModel> Entries { get; } = new();

    public IReadOnlyList<StatusFilterOption> StatusFilters { get; }

    [ObservableProperty]
    public partial StatusFilterOption SelectedStatusFilter { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial SummaryRowViewModel? SelectedEntry { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoDetail))]
    public partial EntryEditViewModel? Detail { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public bool HasSelection => SelectedEntry is not null;

    public bool HasNoDetail => Detail is null;

    public async Task InitializeAsync() => await ReloadAsync();

    partial void OnSelectedStatusFilterChanged(StatusFilterOption value)
    {
        _ = ReloadAsync();
    }

    async partial void OnSelectedEntryChanged(SummaryRowViewModel? value)
    {
        if (value is null)
            return;

        var entry = await _repository.GetAsync(value.Id);
        if (entry is not null)
            Detail = new EntryEditViewModel(_repository, OnDetailChangedAsync, entry);
    }

    [RelayCommand]
    private void NewEntry()
    {
        SelectedEntry = null;
        Detail = new EntryEditViewModel(_repository, OnDetailChangedAsync, entry: null);
    }

    [RelayCommand]
    private async Task DeleteEntryAsync()
    {
        if (SelectedEntry is null) return;
        await _repository.DeleteAsync(SelectedEntry.Id);
        Detail = null;
        SelectedEntry = null;
        await ReloadAsync();
    }

    // Called by the detail editor after any persisted change.
    private async Task OnDetailChangedAsync() => await ReloadAsync(preserveSelection: true);

    private async Task ReloadAsync(bool preserveSelection = false)
    {
        IsLoading = true;
        try
        {
            var selectedId = SelectedEntry?.Id;
            var summaries = await _repository.ListAsync();
            var filter = SelectedStatusFilter?.Wire;

            Entries.Clear();
            foreach (var s in summaries)
            {
                if (filter is not null && s.Status != filter) continue;
                Entries.Add(new SummaryRowViewModel(s));
            }

            if (preserveSelection && selectedId is { } id)
            {
                var match = Entries.FirstOrDefault(e => e.Id == id);
                if (match is not null)
                    SelectedEntry = match;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}
