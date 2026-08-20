using System.Text;
using System.Text.Json;

using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.DomainModels;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

public class JsonRoadmapPlanRepositoryTests : IDisposable
{
    private readonly string _dir;
    private readonly JsonRoadmapPlanRepository _plans;

    public JsonRoadmapPlanRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "roadmap-tests-" + Guid.NewGuid().ToString("N"));
        _plans = new JsonRoadmapPlanRepository(_dir);
    }

    private static PlannedWindow Window(int startDay, int endDay) =>
        PlannedWindow.Of(new DateOnly(2026, 1, startDay), new DateOnly(2026, 1, endDay));

    [Fact]
    public async Task AFirstRunLoadsAnEmptyPlan_RatherThanFailing()
    {
        var plan = await _plans.LoadAsync();

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void ThePlanLivesUnderTheStorageRoot_BesideTheBacklog()
    {
        Assert.Equal(Path.Combine(_dir, "_roadmap", "plan.json"), _plans.PlanPath);
        Assert.True(Directory.Exists(Path.Combine(_dir, "_roadmap")));
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsTheWholePlan()
    {
        var plan = RoadmapPlan.Empty();
        var design = plan.AddItem(
            "Design the sync service",
            Window(5, 9),
            PlanningPriority.High,
            RepositoryScope.Of(["backlog", "fincent"]),
            PlanningLane.Of("platform"),
            backlogEntryId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            notes: "Spike first.").Value;
        var build = plan.AddItem("Build it", Window(12, 23)).Value;
        var release = plan.AddMilestone("1.0", new DateOnly(2026, 2, 2), MilestoneKind.Release).Value;
        plan.AddDependency(build.Id, design.Id);
        plan.AddDependency(release.Id, build.Id);

        await _plans.SaveAsync(plan);
        var loaded = await _plans.LoadAsync();

        var loadedDesign = loaded.Items.Single(item => item.Id == design.Id);
        Assert.Equal("Design the sync service", loadedDesign.Title);
        Assert.Equal(new DateOnly(2026, 1, 5), loadedDesign.Window.Start);
        Assert.Equal(new DateOnly(2026, 1, 9), loadedDesign.Window.End);
        Assert.Equal(PlanningPriority.High, loadedDesign.Priority);
        Assert.Equal(["backlog", "fincent"], loadedDesign.Scope.Aliases);
        Assert.Equal("platform", loadedDesign.Lane.Name);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), loadedDesign.BacklogEntryId);
        Assert.Equal("Spike first.", loadedDesign.Notes);

        var loadedBuild = loaded.Items.Single(item => item.Id == build.Id);
        Assert.Equal([design.Id], loadedBuild.Dependencies.All);

        var loadedRelease = Assert.Single(loaded.Milestones);
        Assert.Equal(new DateOnly(2026, 2, 2), loadedRelease.On);
        Assert.Equal(MilestoneKind.Release, loadedRelease.Kind);
        Assert.Equal([build.Id], loadedRelease.Dependencies.All);
    }

    [Fact]
    public async Task DatesAreWrittenAsPlainDays_WithNoTimeAndNoOffset()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Anything", Window(5, 9));

        await _plans.SaveAsync(plan);
        var json = await File.ReadAllTextAsync(_plans.PlanPath);

        Assert.Contains("\"start\": \"2026-01-05\"", json);
        Assert.Contains("\"end\": \"2026-01-09\"", json);
        Assert.DoesNotContain("T00:00:00", json);
    }

    [Fact]
    public async Task EnumsAreWrittenAsWords_SoAReorderedEnumCannotReinterpretAPlan()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Critical work", Window(5, 9), PlanningPriority.Critical);
        plan.AddMilestone("Freeze", new DateOnly(2026, 2, 2), MilestoneKind.Freeze);

        await _plans.SaveAsync(plan);
        var json = await File.ReadAllTextAsync(_plans.PlanPath);

        Assert.Contains("\"priority\": \"critical\"", json);
        Assert.Contains("\"kind\": \"freeze\"", json);
    }

    [Fact]
    public async Task ThePlanIsHandEditable_AndAJsonSyntaxErrorNeverStopsTheAppOpening()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Something", Window(5, 9));
        await _plans.SaveAsync(plan);

        await File.WriteAllTextAsync(_plans.PlanPath, "{ \"items\": [ oops");

        var loaded = await _plans.LoadAsync();

        Assert.True(loaded.IsEmpty);
        // And the broken file is left exactly as it was, so the typo can be fixed
        // rather than having been overwritten with an empty plan.
        Assert.Contains("oops", await File.ReadAllTextAsync(_plans.PlanPath));
    }

    [Fact]
    public async Task AHandWrittenPlanIsRead()
    {
        var itemId = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new
        {
            version = 1,
            items = new[]
            {
                new
                {
                    id = itemId.ToString(),
                    title = "Typed by hand",
                    start = "2026-03-02",
                    end = "2026-03-13",
                    priority = "high",
                    repositories = new[] { "Backlog" },
                    lane = "platform",
                    dependsOn = Array.Empty<string>()
                }
            }
        });
        Directory.CreateDirectory(Path.GetDirectoryName(_plans.PlanPath)!);
        await File.WriteAllTextAsync(_plans.PlanPath, json);

        var loaded = await _plans.LoadAsync();

        var item = Assert.Single(loaded.Items);
        Assert.Equal("Typed by hand", item.Title);
        Assert.Equal(new DateOnly(2026, 3, 2), item.Window.Start);
        Assert.Equal(PlanningPriority.High, item.Priority);
        Assert.Equal(["backlog"], item.Scope.Aliases);
    }

    [Fact]
    public async Task ABlockWithNoUsableIdOrTitle_CostsOnlyItsOwnEntry()
    {
        var good = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new
        {
            version = 1,
            items = new object[]
            {
                new { id = "not-a-guid", title = "No id", start = "2026-03-02", end = "2026-03-02" },
                new { id = Guid.NewGuid().ToString(), title = "", start = "2026-03-02", end = "2026-03-02" },
                new { id = good.ToString(), title = "Fine", start = "2026-03-02", end = "2026-03-06" }
            }
        });
        Directory.CreateDirectory(Path.GetDirectoryName(_plans.PlanPath)!);
        await File.WriteAllTextAsync(_plans.PlanPath, json);

        var loaded = await _plans.LoadAsync();

        var item = Assert.Single(loaded.Items);
        Assert.Equal(good, item.Id);
    }

    [Fact]
    public async Task ThePlanIsWrittenWithNoByteOrderMark_SoOtherToolsCanReadIt()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Anything", Window(5, 9));

        await _plans.SaveAsync(plan);
        var bytes = await File.ReadAllBytesAsync(_plans.PlanPath);

        // A BOM is invisible to this app, which strips it on read — and three bytes of
        // garbage in front of the opening brace to jq, python's json.load and plenty of
        // editors. A file meant to be edited by hand has to be one they will accept.
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal((byte)'{', bytes[0]);
    }

    [Fact]
    public async Task APlanWrittenWithAByteOrderMarkStillLoads()
    {
        var plan = RoadmapPlan.Empty();
        var item = plan.AddItem("From an older build", Window(5, 9)).Value;
        await _plans.SaveAsync(plan);

        var json = await File.ReadAllTextAsync(_plans.PlanPath);
        await File.WriteAllTextAsync(_plans.PlanPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var loaded = await _plans.LoadAsync();

        Assert.Equal(item.Id, Assert.Single(loaded.Items).Id);
    }

    [Fact]
    public async Task APlanWideDateAndItsBandColoursRoundTrip()
    {
        // The colours are legacy — nothing writes them any more — but a plan that
        // already has them has to keep them until they have been carried over to the
        // repository they were chosen for.
        var plan = RoadmapPlan.Rehydrate([], [], BandColours.Of([new KeyValuePair<string, int>("backlog", 4)]));
        var freeze = plan.AddMilestone(
            "Freeze",
            new DateOnly(2026, 2, 2),
            MilestoneKind.Freeze,
            isPlanWide: true).Value;
        plan.AddMilestone("1.0", new DateOnly(2026, 3, 31));

        await _plans.SaveAsync(plan);
        var loaded = await _plans.LoadAsync();

        Assert.True(loaded.Milestones.Single(milestone => milestone.Id == freeze.Id).IsPlanWide);
        Assert.False(loaded.Milestones.Single(milestone => milestone.Title == "1.0").IsPlanWide);
        Assert.Equal(4, loaded.BandColours.For("backlog"));
    }

    [Fact]
    public async Task AnOrdinaryDateCostsNoLineInTheFile()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddMilestone("1.0", new DateOnly(2026, 3, 31));

        await _plans.SaveAsync(plan);
        var json = await File.ReadAllTextAsync(_plans.PlanPath);

        // The file is meant to be read by hand; a false on every milestone is a line
        // that says nothing.
        Assert.DoesNotContain("planWide", json);
    }

    [Fact]
    public async Task BandColoursAreStoredAsWhichOneRatherThanAsAColour()
    {
        var plan = RoadmapPlan.Rehydrate([], [], BandColours.Of([new KeyValuePair<string, int>("backlog", 3)]));
        plan.AddMilestone("1.0", new DateOnly(2026, 3, 31));

        await _plans.SaveAsync(plan);
        var json = await File.ReadAllTextAsync(_plans.PlanPath);

        Assert.Contains("\"backlog\": 3", json);

        // Which hue that is belongs to the stylesheet. A plan file naming one would be a
        // plan inventing a colour, which the design system does not allow it to do.
        Assert.DoesNotContain("#", json);
    }

    [Fact]
    public async Task AStoredColourOutsideTheSanctionedSetIsDroppedOnLoad()
    {
        var json = JsonSerializer.Serialize(new
        {
            version = 1,
            items = Array.Empty<object>(),
            milestones = new[]
            {
                new { id = Guid.NewGuid().ToString(), title = "1.0", on = "2026-03-31", kind = "release" }
            },
            bands = new Dictionary<string, int> { ["backlog"] = 9, ["fincent"] = 2 }
        });
        Directory.CreateDirectory(Path.GetDirectoryName(_plans.PlanPath)!);
        await File.WriteAllTextAsync(_plans.PlanPath, json);

        var loaded = await _plans.LoadAsync();

        Assert.Null(loaded.BandColours.For("backlog"));
        Assert.Equal(2, loaded.BandColours.For("fincent"));
    }

    [Fact]
    public async Task SavingLeavesNoTemporaryFileBehind()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Anything", Window(5, 9));

        await _plans.SaveAsync(plan);

        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "_roadmap"), "*.tmp"));
    }

    [Fact]
    public async Task PointingTheRootedRepositoryAtAnotherFolder_MovesToThatPlan()
    {
        var second = Path.Combine(Path.GetTempPath(), "roadmap-tests-" + Guid.NewGuid().ToString("N"));
        var root = _dir;
        var rooted = new RootedJsonRoadmapPlanRepository(() => root);

        var first = RoadmapPlan.Empty();
        first.AddItem("In the first folder", Window(5, 9));
        await rooted.SaveAsync(first);

        root = second;
        try
        {
            var loaded = await rooted.LoadAsync();

            Assert.True(loaded.IsEmpty);
            Assert.Equal(Path.Combine(second, "_roadmap", "plan.json"), rooted.PlanPath);
        }
        finally
        {
            if (Directory.Exists(second)) Directory.Delete(second, recursive: true);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }
}
