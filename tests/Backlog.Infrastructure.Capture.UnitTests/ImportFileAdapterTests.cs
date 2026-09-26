using Backlog.Infrastructure.Capture.Import;
using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Capture.UnitTests;

/// <summary>
/// The import adapter reads the file a target names and hands the run every
/// item it could read, under the tool-qualified external id the run derives
/// the capture id from.
/// </summary>
public sealed class ImportFileAdapterTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static MonitoredSource Import(params string[] paths) => new(CaptureSourceKind.Import, Enabled: true, Targets: paths);

    [Fact]
    public async Task A_manifests_items_become_entries_with_their_facts()
    {
        var findings = await new ImportFileAdapter().RunAsync(
            Import(FixturePath("microsoft-todo-inbox-import.md")),
            TestContext.Current.CancellationToken);

        Assert.Equal("Import (microsoft-todo)", findings.Label);
        Assert.Empty(findings.Notes);
        Assert.Equal(4, findings.Entries.Count);

        var bike = findings.Entries[0];
        Assert.StartsWith("microsoft-todo:AAMkAGVmMDEzMTM4", bike.ExternalId);
        Assert.Equal("Try the new bike route to the office", bike.Title);
        Assert.Equal("https://example.org/routes/canal", bike.Url);
        Assert.Equal("The one past the canal, Sundays only.", bike.BodyMd);
        Assert.Equal(DateTimeOffset.Parse("2026-09-20T08:14:03.4460247Z"), bike.PublishedAt);
        Assert.Equal(["home"], bike.Facts!.Tags);

        var video = findings.Entries[3];
        Assert.Equal("youtube", video.Facts!.ContentKind);
        Assert.Equal("maria", video.Facts.Person);
        Assert.Equal("Someday/Maybe", video.Facts.List);
    }

    [Fact]
    public async Task A_broken_item_is_a_note_and_the_rest_still_come()
    {
        var findings = await new ImportFileAdapter().RunAsync(
            Import(FixturePath("broken-fence-inbox-import.md")),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, findings.Entries.Count);
        Assert.Contains("Item 2 \"Buy oat milk\"", Assert.Single(findings.Notes));
    }

    [Fact]
    public async Task A_file_that_is_not_there_is_a_note_not_a_throw()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"no-such-manifest-{Guid.NewGuid():N}.md");

        var findings = await new ImportFileAdapter().RunAsync(Import(missing), TestContext.Current.CancellationToken);

        Assert.Empty(findings.Entries);
        Assert.Null(findings.Label);
        Assert.Equal([$"{Path.GetFileName(missing)} is not there any more, so nothing was imported from it"], findings.Notes);
    }
}
