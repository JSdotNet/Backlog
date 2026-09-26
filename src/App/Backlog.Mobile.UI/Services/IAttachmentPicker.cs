namespace Backlog.Mobile.UI.Services;

/// <summary>
/// Where a talk note's photos and files come from. Two implementations, one per
/// host, the way <see cref="ISpeechTranscriber"/> has two: the MAUI head asks
/// Android's camera and pickers, and the browser harness renders
/// <c>&lt;InputFile&gt;</c> elements Playwright can hand a fixture to.
/// <para>
/// Nothing here throws for the ordinary outcomes. A person who backs out of a
/// picker gets <see cref="AttachmentPick.None"/>, and a camera the platform
/// refused gets <see cref="AttachmentPick.Failed"/> with a sentence for the
/// screen.
/// </para>
/// </summary>
public interface IAttachmentPicker
{
    /// <summary>Whether this device has a camera the app may open. The button
    /// is left out when it does not.</summary>
    ValueTask<bool> CanTakePhotoAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the camera; one photo, or none if the person backed out.</summary>
    Task<AttachmentPick> TakePhotoAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the picture picker; as many as the person chose.</summary>
    Task<AttachmentPick> PickPicturesAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the file picker; as many as the person chose.</summary>
    Task<AttachmentPick> PickFilesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// One file the person chose. Read it before the next pick: a browser file is
/// only readable until the input it came from changes again.
/// </summary>
/// <param name="ContentType">What the platform said it is — possibly nothing,
/// or <c>application/octet-stream</c>; the extension decides then.</param>
public sealed record PickedFile(
    string Name,
    string? ContentType,
    long SizeBytes,
    Func<CancellationToken, Task<Stream>> OpenReadAsync);

/// <summary>What one pick produced: the files, or why there are none.</summary>
public sealed record AttachmentPick(IReadOnlyList<PickedFile> Files, string? Error = null)
{
    /// <summary>The person backed out, or chose nothing.</summary>
    public static AttachmentPick None { get; } = new([]);

    public static AttachmentPick Of(IReadOnlyList<PickedFile> files) => new(files);

    public static AttachmentPick Failed(string error) => new([], error);

    public bool IsError => Error is not null;
}
