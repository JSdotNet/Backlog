using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Backlog.Storage;
using Backlog_Desktop.ViewModels;

namespace Backlog_Desktop;

/// <summary>
/// Master/detail backlog page. Wires the <see cref="MainViewModel"/> to the local
/// file-backed repository and loads persisted entries on activation.
/// </summary>
public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }

    public MainPage()
    {
        InitializeComponent();
        var repository = new FileBacklogRepository();
        ViewModel = new MainViewModel(repository);
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    private void OnToggleSubItem(object sender, RoutedEventArgs e)
    {
        if (ResolveSubItem(sender) is { } item)
            ViewModel.Detail?.ToggleSubItemCommand.Execute(item);
    }

    private void OnRemoveSubItem(object sender, RoutedEventArgs e)
    {
        if (ResolveSubItem(sender) is { } item)
            ViewModel.Detail?.RemoveSubItemCommand.Execute(item);
    }

    private void OnMoveSubItemUp(object sender, RoutedEventArgs e)
    {
        if (ResolveSubItem(sender) is { } item)
            ViewModel.Detail?.MoveSubItemUpCommand.Execute(item);
    }

    private void OnMoveSubItemDown(object sender, RoutedEventArgs e)
    {
        if (ResolveSubItem(sender) is { } item)
            ViewModel.Detail?.MoveSubItemDownCommand.Execute(item);
    }

    private static SubItemViewModel? ResolveSubItem(object sender) =>
        (sender as FrameworkElement)?.DataContext as SubItemViewModel;

    public static Visibility BoolToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility NotNullToVisibility(object? value) =>
        value is not null ? Visibility.Visible : Visibility.Collapsed;
}
