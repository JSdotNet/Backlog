using System.Text;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A picker whose next answer the test sets, counting the reads a file gets —
/// which is how a test proves a refused file was never copied.
/// </summary>
internal sealed class TestAttachmentPicker : IAttachmentPicker
{
    public AttachmentPick Next { get; set; } = AttachmentPick.None;

    public int Reads { get; private set; }

    public ValueTask<bool> CanTakePhotoAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);

    public Task<AttachmentPick> TakePhotoAsync(CancellationToken cancellationToken = default) => Task.FromResult(Next);

    public Task<AttachmentPick> PickPicturesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Next);

    public Task<AttachmentPick> PickFilesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Next);

    /// <summary>A file with these bytes, as a picker would report it.</summary>
    public PickedFile File(string name, string? contentType, byte[] bytes, long? claimedSize = null) =>
        new(name, contentType, claimedSize ?? bytes.Length, _ =>
        {
            Reads++;
            return Task.FromResult<Stream>(new MemoryStream(bytes));
        });

    public PickedFile Text(string name, string contentType, string text) =>
        File(name, contentType, Encoding.UTF8.GetBytes(text));
}

/// <summary>A folder of its own under the temp directory, for one test's files.</summary>
internal static class TestFolders
{
    public static string Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-mobile-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
