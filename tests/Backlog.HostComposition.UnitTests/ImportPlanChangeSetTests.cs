extern alias DesktopHarness;

using Backlog.Infrastructure.Sqlite.Progress;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// An import is one change set (local ADR 0061 §2), in the desktop harness's own
/// composition: the tasks it creates and the roadmap plan document it lays out land in
/// the journal under the one change set labelled "Import of +tag", so a restore takes
/// them back together.
/// </summary>
public sealed class ImportPlanChangeSetTests
{
    private const string Document =
        "# Imported plans on the roadmap\n`plan` `+journalplan` `id:the-item` `repo:backlog`\n\n"
        + "What the plan is about.\n\n"
        + "# First prompt\n`prompt` `+journalplan` `id:first` `effort:3`\n\n"
        + "# Second prompt\n`prompt` `+journalplan` `id:second` `after:first` `effort:2`\n";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_import_yields_one_change_set_holding_the_tasks_and_the_roadmap_plan()
    {
        using var harness = new Harness();
        using var scope = harness.Services.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<ITaskItems>();
        var databasePath = scope.ServiceProvider.GetRequiredService<RootedSqliteGoalRepository>().DatabasePath;

        var before = (await JournalRows.ReadAsync(databasePath, Ct)).Select(row => row.Sequence).DefaultIfEmpty(0).Max();

        var result = await tasks.ImportPlanAsync(Document, cancellationToken: Ct);
        var imported = Assert.IsType<Ok<ImportPlanResultDto>>(result.Value).Value;
        Assert.Equal(2, imported.Created);
        Assert.Equal(1, imported.Roadmap?.Created);

        var rows = (await JournalRows.ReadAsync(databasePath, Ct)).Where(row => row.Sequence > before).ToList();

        var changeSet = Assert.Single(rows.Select(row => row.ChangeSetId).Distinct());
        Assert.All(rows, row => Assert.Equal("Import of +journalplan", row.Label));
        Assert.Equal(2, rows.Count(row => row.DocumentKind == "task"));
        Assert.Single(rows, row => row.DocumentKind == "roadmap-plan");
        Assert.All(rows.Where(row => row.DocumentKind == "task"), row => Assert.Null(row.Before));
        Assert.NotEmpty(changeSet);
    }

    private sealed class Harness : IsolatedHarnessFactory<DesktopHarness::Program>
    {
        protected override void ConfigureHarness(IWebHostBuilder builder) =>
            builder.ConfigureServices(services => services.AddSingleton<IAppFeatureSettings>(new AllFeaturesOn()));
    }

    private sealed class AllFeaturesOn : IAppFeatureSettings
    {
        public event Action? Changed;

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "(replaced for this test)";

        public bool IsEnabled(string key) => true;

        public string? SetEnabled(string key, bool enabled)
        {
            Changed?.Invoke();
            return null;
        }
    }
}
