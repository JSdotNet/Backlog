using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Backlog_Desktop.ViewModels;

/// <summary>UI wrapper for a single sub-item row.</summary>
public partial class SubItemViewModel : ObservableObject
{
    public SubItemViewModel(Guid id, string title, bool isDone, string? notes, int order)
    {
        Id = id;
        Title = title;
        IsDone = isDone;
        Notes = notes;
        Order = order;
    }

    public Guid Id { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial bool IsDone { get; set; }

    [ObservableProperty]
    public partial string? Notes { get; set; }

    [ObservableProperty]
    public partial int Order { get; set; }
}
