using System.Text.RegularExpressions;

using Backlog.Tests;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The <c>sync</c> field of devbook contract 25: where a block may state it, how a
/// unit resolves it, what the findings report, and the vocabulary pinned against
/// the rule text this repository vendors under <c>.agents/rules/</c> — the copy
/// <c>devbook:update</c> keeps, which is contract 25 while the fixtures
/// <c>DevbookRuleTextContractTests</c> reads are still contract 19.
/// </summary>
public sealed class DevbookSyncTests
{
    [Theory]
    [InlineData(DevbookFolder.Domain, ".devbook/domain/context-map.md", DevbookMetadataLevel.File, null, 1, DevbookSyncLevel.Folder)]
    [InlineData(DevbookFolder.Domain, "orders/context.md", DevbookMetadataLevel.File, "context", 1, DevbookSyncLevel.Context)]
    [InlineData(DevbookFolder.Domain, "orders/domain.md", DevbookMetadataLevel.File, "domain", 1, DevbookSyncLevel.Page)]
    [InlineData(DevbookFolder.Domain, "orders/domain.order.md", DevbookMetadataLevel.File, "domain", 1, DevbookSyncLevel.Page)]
    [InlineData(DevbookFolder.Domain, "orders/features.md", DevbookMetadataLevel.File, "features", 1, DevbookSyncLevel.Page)]
    [InlineData(DevbookFolder.Domain, "orders/actors.md", DevbookMetadataLevel.File, "actors", 1, DevbookSyncLevel.Page)]
    [InlineData(DevbookFolder.Domain, "orders/domain.md", DevbookMetadataLevel.Chapter, "aggregate", 2, DevbookSyncLevel.Unit)]
    [InlineData(DevbookFolder.Domain, "orders/domain.md", DevbookMetadataLevel.Chapter, "domain-service", 2, DevbookSyncLevel.Unit)]
    [InlineData(DevbookFolder.Domain, "orders/features.md", DevbookMetadataLevel.Chapter, "feature", 2, DevbookSyncLevel.Unit)]
    [InlineData(DevbookFolder.Domain, "orders/context.md", DevbookMetadataLevel.Chapter, "feature-flag", 2, DevbookSyncLevel.Unit)]
    [InlineData(DevbookFolder.Domain, "orders/context.md", DevbookMetadataLevel.Chapter, "setting", 2, DevbookSyncLevel.Unit)]
    [InlineData(DevbookFolder.Arc42, ".devbook/arc42/05-building-block-view.md", DevbookMetadataLevel.File, null, 1, DevbookSyncLevel.Folder)]
    [InlineData(DevbookFolder.Arc42, ".arc42/building-blocks/sync.md", DevbookMetadataLevel.File, null, 1, DevbookSyncLevel.Unit)]
    [InlineData(DevbookFolder.Design, "component-libraries.md", DevbookMetadataLevel.File, null, 1, DevbookSyncLevel.Folder)]
    [InlineData(DevbookFolder.Design, "component-libraries.md", DevbookMetadataLevel.Chapter, null, 2, DevbookSyncLevel.Unit)]
    public void A_block_on_one_of_the_four_levels_may_state_a_direction(
        DevbookFolder folder, string path, DevbookMetadataLevel level, string? type, int heading, DevbookSyncLevel expected)
    {
        var place = DevbookSync.Place(folder, path, level, type, heading);

        Assert.True(place.IsAllowed);
        Assert.Equal(expected, place.Level);
    }

    [Theory]
    [InlineData(DevbookFolder.Domain, "orders/domain.md", DevbookMetadataLevel.Chapter, "entity", 3, DevbookSyncRefusal.Owned)]
    [InlineData(DevbookFolder.Domain, "orders/domain.md", DevbookMetadataLevel.Chapter, "domain-event", 2, DevbookSyncRefusal.Owned)]
    [InlineData(DevbookFolder.Domain, "orders/features.md", DevbookMetadataLevel.Chapter, "sub-feature", 3, DevbookSyncRefusal.Owned)]
    [InlineData(DevbookFolder.Domain, "orders/domain.md", DevbookMetadataLevel.Chapter, "term", 3, DevbookSyncRefusal.Owned)]
    [InlineData(DevbookFolder.Domain, "orders/requirements.md", DevbookMetadataLevel.File, "requirements", 1, DevbookSyncRefusal.OwnedPage)]
    [InlineData(DevbookFolder.Domain, "orders/requirements.capture.md", DevbookMetadataLevel.File, "requirements", 1, DevbookSyncRefusal.OwnedPage)]
    [InlineData(DevbookFolder.Domain, "orders/domain.invariants.md", DevbookMetadataLevel.File, "invariants", 1, DevbookSyncRefusal.OwnedPage)]
    [InlineData(DevbookFolder.Domain, "orders/requirements.md", DevbookMetadataLevel.Chapter, "requirement", 2, DevbookSyncRefusal.OwnedPage)]
    [InlineData(DevbookFolder.Domain, "orders/model.md", DevbookMetadataLevel.File, "model", 1, DevbookSyncRefusal.NoLevel)]
    [InlineData(DevbookFolder.Domain, "context-map.md", DevbookMetadataLevel.Chapter, "bounded-context", 2, DevbookSyncRefusal.NoLevel)]
    [InlineData(DevbookFolder.Domain, "orders/domain.md", DevbookMetadataLevel.Chapter, "shared-value-objects", 2, DevbookSyncRefusal.NoLevel)]
    [InlineData(DevbookFolder.Arc42, "04-solution-strategy.md", DevbookMetadataLevel.File, null, 1, DevbookSyncRefusal.NoLevel)]
    [InlineData(DevbookFolder.Arc42, "building-blocks/sync.md", DevbookMetadataLevel.Chapter, null, 2, DevbookSyncRefusal.NoLevel)]
    [InlineData(DevbookFolder.Design, "component-libraries.md", DevbookMetadataLevel.Chapter, null, 3, DevbookSyncRefusal.NoLevel)]
    [InlineData(DevbookFolder.Design, "component-libraries.md", DevbookMetadataLevel.Chapter, "requirement", 3, DevbookSyncRefusal.Owned)]
    [InlineData(DevbookFolder.Design, "color-scheme.md", DevbookMetadataLevel.File, null, 1, DevbookSyncRefusal.NoLevel)]
    [InlineData(DevbookFolder.Tech, "technology-graph.md", DevbookMetadataLevel.File, null, 1, DevbookSyncRefusal.NoLevel)]
    [InlineData(DevbookFolder.Ai, "adoption-map.md", DevbookMetadataLevel.File, null, 1, DevbookSyncRefusal.NoLevel)]
    public void Every_other_block_refuses_one_and_says_why(
        DevbookFolder folder, string path, DevbookMetadataLevel level, string? type, int heading, DevbookSyncRefusal expected)
    {
        var place = DevbookSync.Place(folder, path, level, type, heading);

        Assert.False(place.IsAllowed);
        Assert.Equal(expected, place.Refusal);
    }

    [Fact]
    public void A_building_blocks_entry_point_is_no_building_block()
    {
        var place = DevbookSync.Place(DevbookFolder.Arc42, "building-blocks/README.md", DevbookMetadataLevel.File, null, 1, indexRoot: true);

        Assert.Equal(DevbookSyncRefusal.NoLevel, place.Refusal);
    }

    [Fact]
    public void The_nearest_statement_wins_and_a_value_outside_the_five_is_skipped()
    {
        var direction = DevbookSync.Resolve(
        [
            (DevbookSyncLevel.Unit, null),
            (DevbookSyncLevel.Page, "pushh"),
            (DevbookSyncLevel.Context, " Pull "),
            (DevbookSyncLevel.Folder, "off")
        ]);

        Assert.Equal(new DevbookSyncDirection("pull", DevbookSyncLevel.Context), direction);
        Assert.False(direction.IsDefault);
    }

    [Fact]
    public void Nothing_stated_is_report_from_no_level()
    {
        var direction = DevbookSync.Resolve([(DevbookSyncLevel.Unit, null), (DevbookSyncLevel.Folder, "")]);

        Assert.Equal(new DevbookSyncDirection("report", null), direction);
        Assert.True(direction.IsDefault);
    }

    [Fact]
    public void A_misplaced_or_misspelt_direction_is_an_error_finding()
    {
        Assert.Contains(For(DevbookFolder.Domain, "type: entity\nsync: push", "orders/domain.md"), Sync);
        Assert.Contains(For(DevbookFolder.Domain, "type: requirements\nsync: push", "orders/requirements.md", DevbookMetadataLevel.File), Sync);
        Assert.Contains(For(DevbookFolder.Domain, "type: aggregate\nsync: both", "orders/domain.md"), Sync);
        Assert.Contains(For(DevbookFolder.Tech, "sync: push", "technology-graph.md"), Sync);
        Assert.All(For(DevbookFolder.Domain, "type: entity\nsync: push", "orders/domain.md").Where(Sync), finding => Assert.Equal(DevbookFindingSeverity.Error, finding.Severity));
    }

    [Fact]
    public void A_direction_on_a_level_reports_nothing_and_a_bare_file_name_is_left_unjudged()
    {
        Assert.DoesNotContain(For(DevbookFolder.Domain, "type: aggregate\nsync: push", ".devbook/domain/orders/domain.md"), Sync);
        Assert.DoesNotContain(For(DevbookFolder.Arc42, "sync: pull", ".devbook/arc42/building-blocks/sync.md", DevbookMetadataLevel.File), Sync);

        // `sync.md` alone cannot say whether it is a building block.
        Assert.DoesNotContain(For(DevbookFolder.Arc42, "sync: pull", "sync.md", DevbookMetadataLevel.File), Sync);
    }

    [Fact]
    public void The_reader_knows_the_field_and_no_longer_reports_it_as_unrecognised()
    {
        var record = MetadataReader.Parse("type: aggregate\nsync: push");

        Assert.Equal("push", record.Sync);
        Assert.DoesNotContain("sync", record.Extra.Keys);
        Assert.False(MetadataReader.Parse("sync: off").IsEmpty);
    }

    [Fact]
    public void The_directions_and_the_default_are_the_rules()
    {
        var table = Section("## Sync direction", "A direction governs");
        var directions = Regex.Matches(table, @"^\| `([a-z]+)` \|", RegexOptions.Multiline).Select(match => match.Groups[1].Value);

        Assert.Equal(directions, DevbookSync.Directions);
        Assert.Equal(DevbookSync.Directions, DevbookSchema.SyncValues);
        Assert.Equal(DevbookSync.DefaultDirection, Capture(@"its folder, and `([a-z]+)` when none of them states one"));
    }

    [Fact]
    public void The_unit_types_the_owned_types_and_the_levels_files_are_the_rules()
    {
        Assert.Equal(Values(Row("| Unit |")).Take(DevbookSync.UnitTypes.Count), DevbookSync.UnitTypes);
        Assert.Equal(Values(Capture("on a chapter a unit owns — ([^—]+) —")), DevbookSync.OwnedTypes);

        Assert.Equal(
            Values(Row("| Folder |")),
            new[] { DevbookFolder.Domain, DevbookFolder.Arc42, DevbookFolder.Design }.Select(folder => Prefix(folder) + DevbookSync.FolderOverview(folder)));

        var pages = Values(Row("| Page |")).Where(value => value.EndsWith(".md", StringComparison.Ordinal)).Select(value => value[..^3]);
        Assert.Equal(pages, DevbookSync.PageBases);
    }

    private static string Prefix(DevbookFolder folder) => $"{folder.ToString().ToLowerInvariant()}/";

    private static bool Sync(DevbookMetadataFinding finding) => finding.Field == "sync";

    private static IReadOnlyList<DevbookMetadataFinding> For(
        DevbookFolder folder, string block, string fileName, DevbookMetadataLevel level = DevbookMetadataLevel.Chapter) =>
        DevbookMetadataFindings.For(folder, level, MetadataReader.Parse(block), fileName);

    private static readonly string Rule = File.ReadAllText(RepositoryRoot.File([".agents", "rules", "devbook-chapter-metadata.md"]))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Section(string from, string to)
    {
        var start = Rule.IndexOf(from, StringComparison.Ordinal);
        var end = Rule.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"The vendored rule no longer has '{from}' … '{to}'.");
        return Rule[start..end];
    }

    private static string Row(string prefix) =>
        Section("## Sync direction", "## Linking test cases").Split('\n').Single(line => line.StartsWith(prefix, StringComparison.Ordinal));

    private static string Capture(string pattern)
    {
        var match = Regex.Match(Rule, pattern);
        Assert.True(match.Success, $"The vendored rule no longer states: {pattern}");
        return match.Groups[1].Value;
    }

    private static IReadOnlyList<string> Values(string text) =>
        [.. Regex.Matches(text, "`([^`]+)`").Select(match => match.Groups[1].Value)];
}
