using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The launcher for hosts that have no local machine to open a folder on: the web
/// harness serves a browser, so there is no editor on the other end of the
/// request. It fails with a message rather than being left unregistered, so the
/// button reports why instead of the container throwing on a missing service.
/// </summary>
public sealed class UnsupportedFolderEditorLauncher : IFolderEditorLauncher
{
    public Task OpenFolderAsync(string folderPath, CancellationToken cancellationToken = default) =>
        throw new FolderEditorLaunchException("Opening folders in VS Code is only available in the desktop app.");
}
