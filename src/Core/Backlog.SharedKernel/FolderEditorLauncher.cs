namespace Backlog.SharedKernel;

/// <summary>
/// Opens a folder on this machine in the user's code editor.
/// <para>
/// The port is in the shared kernel for the same reason
/// <see cref="IAppFeatureSettings"/> is: more than one context asks the
/// question and none of them owns the answer. "Show me this folder in an
/// editor" is true of a knowledge chapter, a repository clone and a session
/// worktree alike — the folder is the only thing the caller has to name, and
/// nothing about the operation is knowledge-shaped. Which editor is installed
/// and how it is started is local-machine detail, so the implementations sit in
/// the file-system adapter; this is only the request and the failure.
/// </para>
/// </summary>
public interface IFolderEditorLauncher
{
    Task OpenFolderAsync(string folderPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// The folder was not opened, and the message says why in terms a user can act
/// on. Deliberately domain-neutral: a caller that wants to report the failure in
/// its own vocabulary catches this and rethrows its own, which keeps the
/// launcher from having to know who asked.
/// </summary>
public sealed class FolderEditorLaunchException : Exception
{
    public FolderEditorLaunchException(string message) : base(message) { }

    public FolderEditorLaunchException(string message, Exception innerException) : base(message, innerException) { }
}
