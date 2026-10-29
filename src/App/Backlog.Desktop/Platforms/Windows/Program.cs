using Backlog.Desktop.Services;

namespace Backlog.Desktop.WinUI;

/// <summary>
/// The head's entry point, in place of the one the XAML compiler generates
/// (<c>DISABLE_XAML_GENERATED_MAIN</c> in the project file), which it repeats but for
/// one step: a <c>backlog://</c> link opened while the app already runs is handed to that
/// instance, and this process ends before the XAML framework starts (local ADR 0042 §7).
/// Before, not in the <see cref="App"/> constructor, as the Windows App SDK's own
/// instancing guidance has it: by then the framework is up and has to be torn down again.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (BacklogLinkInstance.RedirectToRunningInstance()) return;

        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
