using Backlog.Desktop.UI.Shell;
using Backlog.Modules.Devbook.Abstractions;

#if WINDOWS
using Microsoft.Maui.ApplicationModel;
using Windows.Storage.Pickers;
#endif

namespace Backlog.Desktop.Services;

/// <summary>
/// The Windows folder picker behind the Brain card's <b>Browse</b>.
/// <para>
/// The picker is a WinRT object that has to be told which window owns it — an
/// unpackaged desktop process has no core window for it to find on its own — and
/// it has to be opened on the UI thread, which a Blazor event handler in the
/// WebView is not guaranteed to be on. Both are why this is the head's own type
/// rather than anything a shared project could hold.
/// </para>
/// <para>
/// One per window (local ADR 0040 §5): it stands over the window whose Settings asked
/// for it — named by the window's <see cref="CurrentShellWindow"/> — rather than over
/// whichever window the app lists first, which with surfaces popped out beside the main
/// window is not always the one the person is looking at.
/// </para>
/// </summary>
public sealed class WindowsBrainFolderPicker(ShellWindows windows, CurrentShellWindow current) : IBrainFolderPicker
{
    public async Task<string?> PickFolderAsync(string? startFolder, CancellationToken cancellationToken = default)
    {
#if WINDOWS
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var picker = new FolderPicker
            {
                // The picker cannot be pointed at an arbitrary path; the documents
                // library is the nearest start to where notes usually live.
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                CommitButtonText = "Use this folder",
            };
            picker.FileTypeFilter.Add("*");

            var window = windows.WindowFor(current.Key)?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (window is null) return null;

            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

            var folder = await picker.PickSingleFolderAsync().AsTask(cancellationToken);
            return folder?.Path;
        });
#else
        await Task.CompletedTask;
        return null;
#endif
    }
}
