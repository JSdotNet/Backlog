using Backlog.Mobile.UI.Services;

namespace Backlog.Mobile;

/// <summary>
/// <see cref="IFreeSpace"/> over <see cref="Android.OS.StatFs"/>, as the app update's download
/// check reads it: the bytes available to this app on the volume that holds the folder.
/// </summary>
public sealed class AndroidFreeSpace : IFreeSpace
{
    public long? AvailableBytes(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        try
        {
            // StatFs needs a path that exists; the recordings folder is made on the first part.
            Directory.CreateDirectory(folder);
            return new Android.OS.StatFs(folder).AvailableBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Java.Lang.Exception)
        {
            return null;
        }
    }
}
