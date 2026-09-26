using Backlog.Mobile.UI.Services;

namespace Backlog.Mobile.Services;

/// <summary>
/// <see cref="IAttachmentPicker"/> over the platform's own camera and pickers,
/// through MAUI Essentials: <see cref="MediaPicker"/> for the camera and the
/// photo picker, <see cref="FilePicker"/> for everything else.
/// </summary>
/// <remarks>
/// <para>
/// The pickers are activities, started and answered on the main thread, so
/// every call goes through <see cref="MainThread"/>. <c>CAMERA</c> is declared
/// in <c>AndroidManifest.xml</c>; MAUI raises the runtime prompt when the camera
/// is first asked for, and a refusal arrives as <see cref="PermissionException"/>.
/// </para>
/// <para>
/// Essentials hands back a <see cref="FileResult"/> without a size. On Android
/// a picked item is copied into the app's cache first, so its path is a real
/// file whose length can be read; when it is not, the stream is measured.
/// </para>
/// </remarks>
public sealed class AndroidAttachmentPicker : IAttachmentPicker
{
    private const string CameraDenied =
        "Camera access was denied. Allow it in Android settings and try again.";

    public ValueTask<bool> CanTakePhotoAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(MediaPicker.Default.IsCaptureSupported);

    public Task<AttachmentPick> TakePhotoAsync(CancellationToken cancellationToken = default) =>
        PickAsync(async () => MediaPicker.Default.CapturePhotoAsync() is { } photo ? [await photo] : []);

    public Task<AttachmentPick> PickPicturesAsync(CancellationToken cancellationToken = default) =>
        PickAsync(async () => await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { Title = "Attach pictures", SelectionLimit = 0 }));

    public Task<AttachmentPick> PickFilesAsync(CancellationToken cancellationToken = default) =>
        PickAsync(async () => await FilePicker.Default.PickMultipleAsync(new PickOptions { PickerTitle = "Attach files" }));

    private static async Task<AttachmentPick> PickAsync(Func<Task<IEnumerable<FileResult?>?>> pick)
    {
        try
        {
            var results = await MainThread.InvokeOnMainThreadAsync(pick);
            if (results is null) return AttachmentPick.None;

            var files = new List<PickedFile>();
            foreach (var result in results)
            {
                if (result is null) continue;
                files.Add(new PickedFile(result.FileName, result.ContentType, await SizeOfAsync(result), _ => result.OpenReadAsync()));
            }

            return AttachmentPick.Of(files);
        }
        catch (PermissionException)
        {
            return AttachmentPick.Failed(CameraDenied);
        }
        catch (FeatureNotSupportedException ex)
        {
            return AttachmentPick.Failed(ex.Message);
        }
        catch (OperationCanceledException)
        {
            return AttachmentPick.None;
        }
    }

    private static async Task<long> SizeOfAsync(FileResult result)
    {
        if (!string.IsNullOrEmpty(result.FullPath) && File.Exists(result.FullPath))
        {
            return new FileInfo(result.FullPath).Length;
        }

        await using var stream = await result.OpenReadAsync();
        if (stream.CanSeek) return stream.Length;

        long length = 0;
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0) length += read;

        return length;
    }
}
