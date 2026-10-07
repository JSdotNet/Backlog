namespace Backlog.Infrastructure.Devbook.Chapters;

/// <summary>
/// How a panel's chapter comes off disk: <see cref="File.ReadAllTextAsync(string, CancellationToken)"/>,
/// and nothing else in any host.
/// <para>
/// A seam, and only a seam. A panel starts a chapter read on every parameter
/// set, so reads overlap whenever the selection moves while a file is still
/// being read, and they can finish out of order. On a fast disk they almost never
/// do, which is exactly why a test needs to hold one: registering a subclass is
/// how it makes the older read finish last on purpose. No host registers one, so
/// a panel falls back to <see cref="Disk"/>.
/// </para>
/// </summary>
internal class DevbookChapterFileReader
{
    internal static DevbookChapterFileReader Disk { get; } = new();

    internal virtual Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(path, cancellationToken);
}
