namespace Backlog.Mobile.UI.Services;

/// <summary>
/// How much room the phone has left where a folder lives, for a meeting recording to stop
/// cleanly before the disk is full rather than fail when it is
/// (<c>.devbook/domain/capture/features.md#meeting-recording</c>).
/// </summary>
public interface IFreeSpace
{
    /// <summary>The bytes this app may still write on the volume that holds
    /// <paramref name="folder"/>, or <c>null</c> when the host cannot tell — which is never a
    /// reason to refuse a recording.</summary>
    long? AvailableBytes(string folder);
}

/// <summary>
/// <see cref="IFreeSpace"/> over <see cref="DriveInfo"/>: the default, which the browser harness
/// and the tests run on. The Android head registers its own, over <c>StatFs</c>, which reads the
/// app's own share of the volume.
/// </summary>
public sealed class DriveFreeSpace : IFreeSpace
{
    public long? AvailableBytes(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
