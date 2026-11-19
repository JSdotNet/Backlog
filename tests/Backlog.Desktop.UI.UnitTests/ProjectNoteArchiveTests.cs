using Backlog.Infrastructure.Brain;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Feedback;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Offering to move a finished plan's project note to the Brain's archive (local ADR
/// 0052 §5, <c>.devbook/domain/roadmap/features.md#archiving-a-finished-plans-project-note</c>),
/// over a real stored plan and a real Brain folder: only the gathering is scripted, so
/// a test says which plan is finished without writing its backlog.
/// </summary>
public sealed class ProjectNoteArchiveTests : IDisposable
{
    private const string NoteText = "# House move\r\n\r\n```meta\r\nroadmap: [house-move]\r\n```\r\n\r\nThe person's words.\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "project-note-archive-tests-" + Guid.NewGuid().ToString("N"));
    private readonly WorkspaceSettingsStore _settings;
    private readonly IRoadmapPlanning _planning;
    private readonly ScriptedRollup _rollup = new();
    private readonly ShellNavigationStore _store;
    private readonly BrainRoadmapProjectNotes _notes;
    private readonly ToastChannel _toasts = new();

    public ProjectNoteArchiveTests()
    {
        _settings = new WorkspaceSettingsStore(Path.Combine(_root, "storage"), Path.Combine(_root, "settings.json"));
        _planning = TasksTestHost.PlanningFor(_settings);
        _store = new ShellNavigationStore(Path.Combine(_root, "shell", "shell-navigation.json"));
        var brain = new BrainFolderSource(() => BrainRoot);
        _notes = new BrainRoadmapProjectNotes(brain, new BrainNoteLinkWriter(brain));
        Directory.CreateDirectory(BrainRoot);
    }

    private string BrainRoot => Path.Combine(_root, "brain");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Left for the OS to clear.
        }
    }

    private RoadmapProjectNoteArchive Archive() => new(_planning, _rollup, _notes, _store);

    private string WriteNote(string relativePath)
    {
        var path = Path.Combine([BrainRoot, .. relativePath.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, NoteText);
        return path;
    }

    private async Task<RoadmapItemDto> PlanAsync(params string[] knowledgeRefs)
    {
        var added = await _planning.AddItemAsync(
            "House move",
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 13),
            PlanningPriority.High,
            notes: "Kept as written.",
            tag: "house-move",
            knowledgeRefs: knowledgeRefs,
            cancellationToken: Ct);
        Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Message : null);
        return added.Value;
    }

    private static RoadmapGatheredLink Entry(bool done) =>
        new("task-1", "Pack the boxes", 3, RollupOrigin.Tag, done ? RoadmapProgress.Done : RoadmapProgress.InProgress);

    private static RoadmapGatheredLink BrainNote(string path, string? slug = null, RollupOrigin origin = RollupOrigin.Direct) =>
        new(Backlog.SharedKernel.BrainNoteAddress.Format(path, slug), "House move", null, origin);

    [Fact]
    public async Task A_finished_plan_lists_its_project_notes_once_each_and_nothing_else()
    {
        WriteNote("projects/house.md");
        var item = await PlanAsync("#brain:projects/house.md", "#brain:projects/house.md#budget");
        var open = await PlanAsync("#brain:projects/other.md");
        _rollup.Set(item.Id, new RoadmapItemRollupDto(
            [Entry(done: true)],
            [],
            [BrainNote("projects/house.md"), BrainNote("projects/house.md", "budget"), BrainNote("areas/health.md", origin: RollupOrigin.Tag)]));
        _rollup.Set(open.Id, new RoadmapItemRollupDto([Entry(done: false)], [], [BrainNote("projects/other.md")]));

        var finished = await Archive().FinishedAsync(Ct);

        var note = Assert.Single(finished);
        Assert.Equal(item.Id, note.ItemId);
        Assert.Equal("projects/house.md", note.NotePath);
        Assert.Equal("House move", note.PlanTitle);
    }

    [Fact]
    public async Task The_press_moves_the_file_unchanged_and_the_Knowledge_Ref_follows()
    {
        var source = WriteNote("projects/house.md");
        var before = await File.ReadAllBytesAsync(source, Ct);
        var item = await PlanAsync("#brain:projects/house.md#budget", "arc42/adr/0052.md");
        _rollup.Set(item.Id, new RoadmapItemRollupDto([Entry(done: true)], [], [BrainNote("projects/house.md", "budget")]));
        var archive = Archive();
        var note = Assert.Single(await archive.FinishedAsync(Ct));

        var archived = await archive.ArchiveAsync(note, Ct);

        Assert.True(archived.IsSuccess, archived.IsFailure ? archived.Error.Message : null);
        Assert.Equal("archive/house.md", archived.Value);
        Assert.False(File.Exists(source));
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(BrainRoot, "archive", "house.md"), Ct));

        var stored = Assert.Single((await _planning.GetPlanAsync(Ct)).Items);
        Assert.Equal(["#brain:archive/house.md#budget", "arc42/adr/0052.md"], stored.Knowledge);
        Assert.Equal(item.Start, stored.Start);
        Assert.Equal(item.End, stored.End);
        Assert.Equal(item.Notes, stored.Notes);
        Assert.Equal(item.Tag, stored.Tag);
        Assert.Equal(item.Priority, stored.Priority);
    }

    [Fact]
    public async Task A_refused_move_leaves_the_plan_as_it_was()
    {
        WriteNote("projects/house.md");
        WriteNote("archive/house.md");
        var item = await PlanAsync("#brain:projects/house.md");
        _rollup.Set(item.Id, new RoadmapItemRollupDto([Entry(done: true)], [], [BrainNote("projects/house.md")]));
        var archive = Archive();

        var archived = await archive.ArchiveAsync(Assert.Single(await archive.FinishedAsync(Ct)), Ct);

        Assert.True(archived.IsFailure);
        Assert.Equal(["#brain:projects/house.md"], Assert.Single((await _planning.GetPlanAsync(Ct)).Items).Knowledge);
    }

    [Fact]
    public async Task A_plan_that_finishes_while_the_app_is_open_is_offered_once()
    {
        WriteNote("projects/house.md");
        var item = await PlanAsync("#brain:projects/house.md");
        _rollup.Set(item.Id, new RoadmapItemRollupDto([Entry(done: false)], [], [BrainNote("projects/house.md")]));
        using var offers = new ProjectNoteArchiveOffers(Archive(), _planning, _toasts);
        await offers.StartAsync(Ct);
        await offers.CheckAsync(Ct);
        Assert.Empty(_toasts.Visible);

        _rollup.Set(item.Id, new RoadmapItemRollupDto([Entry(done: true)], [], [BrainNote("projects/house.md")]));
        await offers.CheckAsync(Ct);
        await offers.CheckAsync(Ct);

        var offer = Assert.Single(_toasts.Visible);
        Assert.Equal(ProjectNoteArchiveOffers.ActionLabel, offer.ActionLabel);
        Assert.True(offer.HasAction);
        Assert.Equal([item.Id.ToString("D")], _store.RoadmapArchiveOffered);
    }

    [Fact]
    public async Task Dismissing_the_offer_changes_nothing_and_it_is_not_offered_again()
    {
        var source = WriteNote("projects/house.md");
        var before = await File.ReadAllBytesAsync(source, Ct);
        var item = await PlanAsync("#brain:projects/house.md");
        _rollup.Set(item.Id, new RoadmapItemRollupDto([Entry(done: false)], [], [BrainNote("projects/house.md")]));
        using (var offers = new ProjectNoteArchiveOffers(Archive(), _planning, _toasts))
        {
            await offers.StartAsync(Ct);
            _rollup.Set(item.Id, new RoadmapItemRollupDto([Entry(done: true)], [], [BrainNote("projects/house.md")]));
            await offers.CheckAsync(Ct);
        }

        _toasts.Dismiss(Assert.Single(_toasts.Visible).Id);

        Assert.Equal(before, await File.ReadAllBytesAsync(source, Ct));
        Assert.Equal(["#brain:projects/house.md"], Assert.Single((await _planning.GetPlanAsync(Ct)).Items).Knowledge);

        // A restart, the plan reopened and finished again: still never offered twice.
        var restarted = new ShellNavigationStore(Path.Combine(_root, "shell", "shell-navigation.json"));
        _rollup.Set(item.Id, new RoadmapItemRollupDto([Entry(done: false)], [], [BrainNote("projects/house.md")]));
        using var again = new ProjectNoteArchiveOffers(new RoadmapProjectNoteArchive(_planning, _rollup, _notes, restarted), _planning, _toasts);
        await again.StartAsync(Ct);
        _rollup.Set(item.Id, new RoadmapItemRollupDto([Entry(done: true)], [], [BrainNote("projects/house.md")]));
        await again.CheckAsync(Ct);

        Assert.Empty(_toasts.Visible);
    }

    [Fact]
    public async Task A_plan_already_finished_when_the_app_opens_is_left_to_the_weekly_review()
    {
        WriteNote("projects/house.md");
        var item = await PlanAsync("#brain:projects/house.md");
        _rollup.Set(item.Id, new RoadmapItemRollupDto([Entry(done: true)], [], [BrainNote("projects/house.md")]));
        using var offers = new ProjectNoteArchiveOffers(Archive(), _planning, _toasts);

        await offers.StartAsync(Ct);
        await offers.CheckAsync(Ct);

        Assert.Empty(_toasts.Visible);
        Assert.Single(await Archive().FinishedAsync(Ct));
    }

    [Fact]
    public async Task An_old_plan_is_not_offered_when_the_archive_area_appears_later()
    {
        var conventions = Path.Combine(BrainRoot, "CONVENTIONS.md");
        File.WriteAllText(conventions, "## Areas\n\n| Area | Folder |\n|---|---|\n| Projects | projects |\n");
        WriteNote("projects/house.md");
        var item = await PlanAsync("#brain:projects/house.md");
        _rollup.Set(item.Id, new RoadmapItemRollupDto([Entry(done: true)], [], [BrainNote("projects/house.md")]));
        using var offers = new ProjectNoteArchiveOffers(Archive(), _planning, _toasts);
        await offers.StartAsync(Ct);

        File.Delete(conventions);
        await offers.CheckAsync(Ct);

        Assert.Empty(_toasts.Visible);
        Assert.Single(await Archive().FinishedAsync(Ct));
    }

    [Fact]
    public async Task Pressing_the_offer_archives_the_note_and_says_where_it_went()
    {
        WriteNote("projects/house.md");
        var item = await PlanAsync("#brain:projects/house.md");
        _rollup.Set(item.Id, new RoadmapItemRollupDto([Entry(done: false)], [], [BrainNote("projects/house.md")]));
        using var offers = new ProjectNoteArchiveOffers(Archive(), _planning, _toasts);
        await offers.StartAsync(Ct);
        _rollup.Set(item.Id, new RoadmapItemRollupDto([Entry(done: true)], [], [BrainNote("projects/house.md")]));
        await offers.CheckAsync(Ct);
        var offer = Assert.Single(_toasts.Visible);
        _toasts.Dismiss(offer.Id);

        await offer.Action!();

        Assert.True(File.Exists(Path.Combine(BrainRoot, "archive", "house.md")));
        Assert.Equal(["#brain:archive/house.md"], Assert.Single((await _planning.GetPlanAsync(Ct)).Items).Knowledge);
        Assert.Contains("archive/house.md", Assert.Single(_toasts.Visible).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Brain_with_no_archive_area_is_never_offered_the_move()
    {
        File.WriteAllText(Path.Combine(BrainRoot, "CONVENTIONS.md"), "## Areas\n\n| Area | Folder |\n|---|---|\n| Projects | projects |\n");
        WriteNote("projects/house.md");
        var item = await PlanAsync("#brain:projects/house.md");
        _rollup.Set(item.Id, new RoadmapItemRollupDto([Entry(done: true)], [], [BrainNote("projects/house.md")]));

        Assert.Empty(await Archive().FinishedAsync(Ct));
    }

    /// <summary>The gathering, said per item by the test.</summary>
    private sealed class ScriptedRollup : IRoadmapItemRollup
    {
        private readonly Dictionary<Guid, RoadmapItemRollupDto> _rollups = [];

        public void Set(Guid itemId, RoadmapItemRollupDto rollup) => _rollups[itemId] = rollup;

        public Task<RoadmapItemRollupDto> GatherAsync(RoadmapItemDto item, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rollups.GetValueOrDefault(item.Id, RoadmapItemRollupDto.Empty));

        public Task<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>> GatherPlanAsync(
            RoadmapPlanDto plan,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>>(
                plan.Items.ToDictionary(item => item.Id, item => _rollups.GetValueOrDefault(item.Id, RoadmapItemRollupDto.Empty)));
    }
}
