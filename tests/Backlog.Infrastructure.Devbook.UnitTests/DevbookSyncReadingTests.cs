using Backlog.Infrastructure.Devbook.Chapters;
using Backlog.SharedKernel.Devbook;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The <c>sync</c> reading from the adapter's side, with no screen in the room: the
/// item path a direction is written to, which the Devbook UI project's
/// <c>Scope</c> extension hands its writer. The placement and the resolution rules
/// are <c>DevbookSyncWriteTests</c>' in the Desktop UI suite.
/// </summary>
public sealed class DevbookSyncReadingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-devbook-sync-reading-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public void A_chapter_is_addressed_by_slug_and_the_title_by_the_bare_path()
    {
        Write("orders/domain.md", """
            # Orders

            ```meta
            type: domain
            ```

            ## Order Line

            ```meta
            type: aggregate
            ```
            """);

        var reading = DevbookSyncReading.Read(_root, DevbookFolder.Domain, "orders/domain.md");

        Assert.Equal("orders/domain.md#order-line", reading.ItemPath("orders/domain.md", DevbookMetadataLevel.Chapter, "Order Line"));
        Assert.Equal("orders/domain.md", reading.ItemPath("orders/domain.md", DevbookMetadataLevel.Chapter, "Orders"));
        Assert.Equal("orders/domain.md", reading.ItemPath("orders/domain.md", DevbookMetadataLevel.File, null));
    }

    [Fact]
    public void A_heading_the_document_does_not_hold_is_addressed_by_the_bare_path()
    {
        Assert.Equal(
            "orders/domain.md",
            DevbookSyncReading.None.ItemPath("orders/domain.md", DevbookMetadataLevel.Chapter, "Order"));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
