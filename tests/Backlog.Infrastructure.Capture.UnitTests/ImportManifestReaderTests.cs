using Backlog.Infrastructure.Capture.Import;

namespace Backlog.Infrastructure.Capture.UnitTests;

/// <summary>
/// Reading an inbox import manifest (local ADR 0017). The fixtures are shaped
/// on Microsoft Graph's <c>todoTask</c> — its base64 <c>id</c>, a
/// <c>createdDateTime</c> with seven fractional digits, a linked resource's
/// <c>webUrl</c>, a category as a tag — because that is what the generating
/// skill writes from. A manifest is often edited by hand, so the broken cases
/// are the ordinary ones: each has to come back as a note naming the item, and
/// never as a throw.
/// </summary>
public sealed class ImportManifestReaderTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void A_generated_manifest_reads_every_item_with_its_facts()
    {
        var reading = ImportManifestReader.Read(Fixture("microsoft-todo-inbox-import.md"));

        Assert.Equal("microsoft-todo", reading.Tool);
        Assert.Empty(reading.Problems);
        Assert.Equal(4, reading.Items.Count);

        var bike = reading.Items[0];
        Assert.Equal("Try the new bike route to the office", bike.Title);
        Assert.StartsWith("AAMkAGVmMDEzMTM4", bike.ExternalId);
        Assert.EndsWith("hRW5AAA=", bike.ExternalId);
        Assert.Equal(DateTimeOffset.Parse("2026-09-20T08:14:03.4460247Z"), bike.CapturedAt);
        Assert.Equal("https://example.org/routes/canal", bike.Url);
        Assert.Equal(["home"], bike.Tags);
        Assert.Null(bike.List);
        Assert.Null(bike.Kind);
        Assert.Equal("The one past the canal, Sundays only.", bike.Notes);

        var milk = reading.Items[1];
        Assert.Equal("Errands", milk.List);
        Assert.Equal(
            "- [ ] the barista one\n- [x] check the price at the corner shop\n\nDue 2026-09-27 in Microsoft To Do.",
            milk.Notes);

        // An item with nothing under its fence has no notes rather than an
        // empty body.
        Assert.Null(reading.Items[2].Notes);

        var video = reading.Items[3];
        Assert.Equal("youtube", video.Kind);
        Assert.Equal("maria", video.Person);
        Assert.Equal("Someday/Maybe", video.List);
    }

    [Fact]
    public void A_manifest_edited_by_hand_still_reads()
    {
        var reading = ImportManifestReader.Read(Fixture("hand-edited-inbox-import.md"));

        // A blank line before the front matter, prose before the first item, a
        // blank line and an unknown key inside a fence, a key typed with a
        // capital: none of them is a problem.
        Assert.Equal("microsoft-todo", reading.Tool);
        Assert.Empty(reading.Problems);
        Assert.Equal(2, reading.Items.Count);

        var milk = reading.Items[0];
        Assert.Equal("Buy oat milk (the barista one)", milk.Title);
        Assert.Equal("errands", milk.List);
        Assert.Equal("## What to check\n- the price at the corner shop", milk.Notes);

        // Written without an external_id, so it gets the fallback the skill
        // would have written: the hash of its title and captured_at as typed.
        var plumber = reading.Items[1];
        Assert.Equal("title-sha256:f8787ceb67dbe7f8", plumber.ExternalId);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 17, 5, 0, TimeSpan.Zero), plumber.CapturedAt);
        Assert.Equal(TimeSpan.Zero, plumber.CapturedAt.Offset);
        Assert.Equal(["home", "house"], plumber.Tags);
    }

    [Fact]
    public void A_broken_meta_fence_skips_that_item_and_says_which()
    {
        var reading = ImportManifestReader.Read(Fixture("broken-fence-inbox-import.md"));

        Assert.Equal(
            ["Try the new bike route to the office", "Read the Graph delta query write-up"],
            reading.Items.Select(item => item.Title));

        var problem = Assert.Single(reading.Problems);
        Assert.Equal("Item 2 \"Buy oat milk\": its meta block is never closed — add a line with just ``` after its last key", problem);
    }

    /// <summary>Pinned: the generating skill computes the same fallback, and
    /// a change here would give every hand-written item a new id — and bring
    /// it in again on the next import.</summary>
    [Fact]
    public void The_fallback_id_is_the_skills()
    {
        Assert.Equal("title-sha256:43f5ab8d3eacd533", ImportManifestReader.FallbackId("Buy oat milk", "2026-09-22T17:02:41Z"));
    }

    [Theory]
    [InlineData("", "The file is empty — a manifest starts with a --- line, then schema: 1 and a tool: line, then --- again")]
    [InlineData("# Buy oat milk\n", "The file does not start with front matter — its first line should be ---, then schema: 1 and a tool: line, then --- again")]
    [InlineData("---\nschema: 1\ntool: microsoft-todo\n# Buy oat milk\n", "The front matter is never closed — add a line with just --- after its tool: line")]
    [InlineData("---\ntool: microsoft-todo\n---\n", "The front matter has no schema — add the line schema: 1")]
    [InlineData("---\nschema: 2\ntool: microsoft-todo\n---\n", "The front matter says schema: 2, and this version of Backlog reads schema 1 only")]
    [InlineData("---\nschema: 1\n---\n", "The front matter names no tool — add a line like tool: microsoft-todo")]
    [InlineData("---\nschema: 1\ntool: Microsoft To Do\n---\n", "The front matter's tool \"Microsoft To Do\" should be a slug like microsoft-todo — lower-case letters, digits and hyphens")]
    public void Front_matter_that_cannot_be_read_refuses_the_file_in_one_note(string text, string problem)
    {
        var reading = ImportManifestReader.Read(text);

        Assert.Null(reading.Tool);
        Assert.Empty(reading.Items);
        Assert.Equal([problem], reading.Problems);
    }

    [Theory]
    [InlineData("#\n```meta\ncaptured_at: 2026-09-20T08:14:00Z\n```\n", "Item 1 has no title — write one after the #")]
    [InlineData("# Buy oat milk\nOat, not soy.\n", "Item 1 \"Buy oat milk\" has no meta block — put a ```meta line right under the title, with external_id and captured_at inside, then a ``` line")]
    [InlineData("# Buy oat milk\n```meta\nexternal_id: a\n```\n", "Item 1 \"Buy oat milk\" has no captured_at — add one like captured_at: 2026-09-20T08:14:00Z")]
    [InlineData("# Buy oat milk\n```meta\ncaptured_at: yesterday\n```\n", "Item 1 \"Buy oat milk\": captured_at \"yesterday\" is not a date and time — write it like 2026-09-20T08:14:00Z")]
    [InlineData("# Buy oat milk\n```meta\ncaptured_at 2026-09-20\n```\n", "Item 1 \"Buy oat milk\": the line \"captured_at 2026-09-20\" in its meta block is not a key: value pair")]
    [InlineData("# Buy oat milk\n```meta\nlist: A\nlist: B\ncaptured_at: 2026-09-20T08:14:00Z\n```\n", "Item 1 \"Buy oat milk\" gives list twice — keep one")]
    public void An_item_that_cannot_be_read_is_skipped_with_a_proofreading_note(string item, string problem)
    {
        var reading = ImportManifestReader.Read("---\nschema: 1\ntool: microsoft-todo\n---\n\n" + item + "\n# Call the plumber\n```meta\nexternal_id: p\ncaptured_at: 2026-09-21T17:05:00Z\n```\n");

        Assert.Equal([problem], reading.Problems);

        // The item after it is not taken down with it.
        Assert.Equal("Call the plumber", Assert.Single(reading.Items).Title);
    }

    [Fact]
    public void A_second_item_with_the_same_external_id_is_skipped_as_the_firsts_twin()
    {
        var reading = ImportManifestReader.Read(
            "---\nschema: 1\ntool: t\n---\n# A\n```meta\nexternal_id: 1\ncaptured_at: 2026-09-20T08:14:00Z\n```\n# B\n```meta\nexternal_id: 1\ncaptured_at: 2026-09-20T08:14:00Z\n```\n");

        Assert.Equal("A", Assert.Single(reading.Items).Title);
        Assert.Equal(["Item 2 \"B\" has the same external_id as item 1, so only item 1 was imported"], reading.Problems);
    }

    [Fact]
    public void A_url_that_is_not_a_web_address_costs_the_item_its_link_not_the_item()
    {
        var reading = ImportManifestReader.Read(
            "---\nschema: 1\ntool: t\n---\n# A\n```meta\nexternal_id: 1\ncaptured_at: 2026-09-20T08:14:00Z\nurl: example.org/a\n```\n");

        Assert.Null(Assert.Single(reading.Items).Url);
        Assert.Equal(["Item 1 \"A\": url \"example.org/a\" is not a web address, so it came in without its link — a url starts with https://"], reading.Problems);
    }
}
