using Backlog.Infrastructure.GitHub;

using Microsoft.Data.Sqlite;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Domain menu with the New domain view switch (<c>devbook-domain-view</c>)
/// on: a bounded context listed by unit, as local ADR 0040 decides — its kinds in
/// the reading order, each split file nested under its kind and titled by its
/// heading, the files that are a tab or a button on another page drawing no row.
///
/// <para>Checked against two real contexts' file names. Fincent's
/// <c>.devbook/domain/financieel/</c> is the ADR's worked example of a split
/// context: thirteen domain splits each with an invariants subpage, Features with
/// no base file, a demo, and two <c>_</c> files. Backlog's own <c>tasks/</c> is
/// the unsplit context the switch must not disturb.</para>
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class DevbookMenuDomainUnitsTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    /// <summary>The files of Fincent's <c>.devbook/domain/financieel/</c>, by name
    /// as they stand (read from <c>D:\Repos\Fincent</c>, never edited), with the
    /// <c>assets</c> folder beside them.</summary>
    private static readonly string[] Financieel =
    [
        "_backlog.md", "_decisions.md", "actors.md", "context.md", "demo.html", "dependencies.md",
        "domain.afspraakcategorieen.invariants.md", "domain.afspraakcategorieen.md",
        "domain.afspraken.invariants.md", "domain.afspraken.md",
        "domain.bankbestanden.invariants.md", "domain.bankbestanden.md",
        "domain.banktransacties.invariants.md", "domain.banktransacties.md",
        "domain.betaalinstructies.invariants.md", "domain.betaalinstructies.md",
        "domain.boekingsservice.invariants.md", "domain.boekingsservice.md",
        "domain.budgetregels.invariants.md", "domain.budgetregels.md",
        "domain.geldstromen.invariants.md", "domain.geldstromen.md",
        "domain.invariants.md",
        "domain.klantrekening.invariants.md", "domain.klantrekening.md",
        "domain.machtigingsverzoeken.invariants.md", "domain.machtigingsverzoeken.md",
        "domain.md",
        "domain.pastoewijzing.invariants.md", "domain.pastoewijzing.md",
        "domain.rekeningen.invariants.md", "domain.rekeningen.md",
        "domain.vierogen.invariants.md", "domain.vierogen.md",
        "features.afspraakcategorieen.md", "features.afspraken-beheren.md", "features.afspraken.md",
        "features.bankafschriften.md", "features.bankbestanden-importeren-en-afwikkelen.md",
        "features.banktransacties-matchen-en-boeken.md", "features.beheer-bng-passen.md",
        "features.betaalrun-en-vul-run.md", "features.bulk-acties.md", "features.financieel-overzicht.md",
        "features.geld-overmaken.md", "features.geldstromen-beheren.md", "features.grootboek.md",
        "features.machtigingen.md", "features.matching-en-signalering.md", "features.pastoewijzing.md",
        "features.rekeningen-en-reserveringen.md", "features.uitgaande-geldbewegingen-autoriseren.md",
        "features.vierogen.md", "features.voorkomens-en-signaleringen-volgen.md",
        "flow.bankrekening-vrijgeven.md", "flow.externe-betaling-uitvoeren.md", "flow.geld-eruit.md",
        "flow.geld-ontvangen.md", "flow.geld-verdelen.md", "flow.lopende-maand.md",
        "flow.maatschappelijke-pas.md", "flow.md", "flow.onvoldoende-saldo.md",
        "flow.overzichtsregel.md", "flow.termijn-herstellen.md",
        "import.md",
        "model.afspraken.md", "model.bankbestanden.md", "model.banktransacties.md",
        "model.betaalinstructies.md", "model.budgetregels.md", "model.domain-services.md",
        "model.geldstromen.md", "model.machtigingsverzoeken.md", "model.md", "model.rekeningen.md",
        "model.vierogen.md",
        "requirements.afspraakcategorieen.md", "requirements.afspraken-beheren.md", "requirements.afspraken.md",
        "requirements.bankafschriften.md", "requirements.bankbestanden-importeren-en-afwikkelen.md",
        "requirements.banktransacties-matchen-en-boeken.md", "requirements.beheer-bng-passen.md",
        "requirements.betaalrun-en-vul-run.md", "requirements.financieel-overzicht.md",
        "requirements.geld-overmaken.md", "requirements.geldstromen-beheren.md", "requirements.machtigingen.md",
        "requirements.md", "requirements.pastoewijzing.md", "requirements.rekeningen-en-reserveringen.md",
        "requirements.uitgaande-geldbewegingen-autoriseren.md", "requirements.vierogen.md",
        "requirements.voorkomens-en-signaleringen-volgen.md"
    ];

    /// <summary>The files of Backlog's own <c>.devbook/domain/tasks/</c>: one of
    /// each kind, none split, and the <c>_archify</c> and <c>assets</c> folders.</summary>
    private static readonly string[] Tasks =
    [
        "context.md", "dependencies.md", "domain.invariants.md", "domain.md", "features.md",
        "flow.md", "model.md", "requirements.md"
    ];

    [Fact]
    public async Task Lists_a_split_context_by_kind_in_reading_order()
    {
        var repo = Repository("financieel", Financieel, "assets");

        var context = await LoadContextAsync(repo, "financieel", domainView: true);

        Assert.Equal("financieel/context.md", context.Path);
        Assert.Equal(
            ["Domain", "Actors", "Features", "Requirements", "Model", "Flow", "Import"],
            context.Children.Select(node => node.Label));
        Assert.Equal(
            ["financieel/domain.md", "financieel/actors.md", "financieel/features", "financieel/requirements.md", "financieel/model.md", "financieel/flow.md", "financieel/import.md"],
            context.Children.Select(node => node.Path));
    }

    [Fact]
    public async Task Nests_each_split_file_under_its_kind_labelled_by_its_name_without_a_database()
    {
        var repo = Repository("financieel", Financieel, "assets");

        var context = await LoadContextAsync(repo, "financieel", domainView: true);

        var domain = Child(context, "Domain");
        Assert.Equal(DevbookMenuNodeKind.File, domain.Kind);
        Assert.Equal(
            [
                "afspraakcategorieen", "afspraken", "bankbestanden", "banktransacties", "betaalinstructies",
                "boekingsservice", "budgetregels", "geldstromen", "klantrekening", "machtigingsverzoeken",
                "pastoewijzing", "rekeningen", "vierogen"
            ],
            domain.Children.Select(node => node.Path["financieel/domain.".Length..^".md".Length]));
        Assert.All(domain.Children, node => Assert.Empty(node.Children));
        Assert.Equal("Afspraakcategorieen", domain.Children[0].Label);

        Assert.Equal(20, Child(context, "Features").Children.Count);
        Assert.Equal("Afspraken beheren", Child(context, "Features").Children.Single(node => node.Path == "financieel/features.afspraken-beheren.md").Label);
        Assert.Equal(17, Child(context, "Requirements").Children.Count);
        Assert.Equal(10, Child(context, "Model").Children.Count);
        Assert.Equal("Domain services", Child(context, "Model").Children.Single(node => node.Path == "financieel/model.domain-services.md").Label);
        Assert.Equal(10, Child(context, "Flow").Children.Count);
        Assert.Empty(Child(context, "Actors").Children);
    }

    [Fact]
    public async Task Draws_no_row_for_invariants_dependencies_demo_or_underscore_files()
    {
        var repo = Repository("financieel", Financieel, "assets");

        var context = await LoadContextAsync(repo, "financieel", domainView: true);
        var paths = Descendants(context).Select(node => node.Path).ToList();

        Assert.DoesNotContain(paths, path => path.EndsWith(".invariants.md", StringComparison.Ordinal));
        Assert.DoesNotContain("financieel/dependencies.md", paths);
        Assert.DoesNotContain(paths, path => path.EndsWith(".html", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, path => path.Contains("/_", StringComparison.Ordinal));
        Assert.DoesNotContain(Descendants(context), node => node.Kind == DevbookMenuNodeKind.Message);
    }

    /// <summary>Fincent's Features has no <c>features.md</c>: its row is a folder
    /// row — no chapter behind it, so a click only opens and closes it.</summary>
    [Fact]
    public async Task A_kind_with_no_base_file_is_a_row_that_only_opens_its_children()
    {
        var repo = Repository("financieel", Financieel, "assets");

        var features = Child(await LoadContextAsync(repo, "financieel", domainView: true), "Features");

        Assert.Equal(DevbookMenuNodeKind.Folder, features.Kind);
        Assert.False(features.Path.EndsWith(".md", StringComparison.Ordinal));
        Assert.True(features.HasChildren);
        Assert.All(features.Children, node => Assert.Equal(DevbookMenuNodeKind.File, node.Kind));
    }

    [Fact]
    public async Task A_further_dot_nests_one_level_deeper()
    {
        var repo = Repository("financieel",
        [
            "context.md", "features.md", "features.producten.md", "features.producten.budgetbeheer.md",
            "features.producten.budgetbeheer.limieten.md", "features.diensten.aanvragen.md"
        ]);

        var features = Child(await LoadContextAsync(repo, "financieel", domainView: true), "Features");

        Assert.Equal(["Diensten", "Producten"], features.Children.Select(node => node.Label));

        // A deeper split whose parent has no file of its own still nests under a
        // row for that parent, which only opens it.
        var diensten = features.Children[0];
        Assert.Equal(DevbookMenuNodeKind.Folder, diensten.Kind);
        Assert.Equal("financieel/features.diensten.aanvragen.md", Assert.Single(diensten.Children).Path);

        var producten = features.Children[1];
        Assert.Equal("financieel/features.producten.md", producten.Path);
        var budgetbeheer = Assert.Single(producten.Children);
        Assert.Equal("financieel/features.producten.budgetbeheer.md", budgetbeheer.Path);
        Assert.Equal("financieel/features.producten.budgetbeheer.limieten.md", Assert.Single(budgetbeheer.Children).Path);
    }

    [Fact]
    public async Task Labels_a_split_row_by_its_heading_from_the_database()
    {
        var repo = Repository("financieel", ["context.md", "domain.md", "domain.vierogen.md", "domain.vierogen.invariants.md"]);
        File.WriteAllText(Path.Combine(repo, ".domain", "financieel", "domain.vierogen.md"), "# Vier-ogenprincipe");

        DevbookMenuTestDatabase.Write(repo, ".domain",
        [
            new DevbookMenuOutlineRow("directory", "financieel", ".domain/financieel", "Financieel", IsRoot: false),
            new DevbookMenuOutlineRow("file", "context.md", ".domain/financieel/context.md", "Financieel", IsRoot: true, Parent: ".domain/financieel"),
            new DevbookMenuOutlineRow("file", "domain.md", ".domain/financieel/domain.md", "Domain", IsRoot: false, Parent: ".domain/financieel"),
            new DevbookMenuOutlineRow("file", "domain.vierogen.md", ".domain/financieel/domain.vierogen.md", "Vier-ogenprincipe", IsRoot: false, Parent: ".domain/financieel")
        ]);

        var domain = Child(await LoadContextAsync(repo, "financieel", domainView: true), "Domain");

        Assert.Equal("Vier-ogenprincipe", Assert.Single(domain.Children).Label);
    }

    [Fact]
    public async Task Lists_an_unsplit_context_as_its_kinds()
    {
        var repo = Repository("tasks", Tasks, "assets", "_archify");

        var context = await LoadContextAsync(repo, "tasks", domainView: true);

        Assert.Equal(["Domain", "Features", "Requirements", "Model", "Flow"], context.Children.Select(node => node.Label));
        Assert.Equal(
            ["tasks/domain.md", "tasks/features.md", "tasks/requirements.md", "tasks/model.md", "tasks/flow.md"],
            context.Children.Select(node => node.Path));
        Assert.All(context.Children, node => Assert.Empty(node.Children));
        Assert.All(context.Children, node => Assert.Null(node.Count));
    }

    /// <summary>The board's counts beside each kind row: Fincent's Domain 13,
    /// Features 20, Requirements 17 (the board's 18 counts <c>requirements.md</c>
    /// itself), Model 10, Flows 10, and none on Actoren, which holds no split.</summary>
    [Fact]
    public async Task A_kind_row_counts_the_rows_under_it()
    {
        var repo = Repository("financieel", Financieel, "assets");

        var context = await LoadContextAsync(repo, "financieel", domainView: true);

        Assert.Equal(
            new (string, int?)[] { ("Domain", 13), ("Actors", null), ("Features", 20), ("Requirements", 17), ("Model", 10), ("Flow", 10), ("Import", null) },
            context.Children.Select(node => (node.Label, node.Count)));
        Assert.All(Descendants(context).Except(context.Children), node => Assert.Null(node.Count));
    }

    [Fact]
    public async Task A_kind_row_is_labelled_by_its_base_file_s_heading_from_the_database()
    {
        var repo = Repository("financieel", ["context.md", "actors.md", "domain.md", "flow.md", "flow.geld-eruit.md", "model.md"]);

        DevbookMenuTestDatabase.Write(repo, ".domain",
        [
            new DevbookMenuOutlineRow("directory", "financieel", ".domain/financieel", "Financieel", IsRoot: false),
            new DevbookMenuOutlineRow("file", "context.md", ".domain/financieel/context.md", "Financieel", IsRoot: true, Parent: ".domain/financieel"),
            new DevbookMenuOutlineRow("file", "actors.md", ".domain/financieel/actors.md", "Actoren", IsRoot: false, Parent: ".domain/financieel"),
            new DevbookMenuOutlineRow("file", "domain.md", ".domain/financieel/domain.md", "Domain", IsRoot: false, Parent: ".domain/financieel"),
            new DevbookMenuOutlineRow("file", "flow.md", ".domain/financieel/flow.md", "Flows", IsRoot: false, Parent: ".domain/financieel"),
            new DevbookMenuOutlineRow("file", "flow.geld-eruit.md", ".domain/financieel/flow.geld-eruit.md", "Geld eruit", IsRoot: false, Parent: ".domain/financieel"),
            // Backlog's own chapters all carry the context's heading: the row keeps its kind.
            new DevbookMenuOutlineRow("file", "model.md", ".domain/financieel/model.md", "Financieel", IsRoot: false, Parent: ".domain/financieel")
        ]);

        var context = await LoadContextAsync(repo, "financieel", domainView: true);

        Assert.Equal(["Domain", "Actoren", "Model", "Flows"], context.Children.Select(node => node.Label));
        Assert.Equal("Geld eruit", Assert.Single(Child(context, "Flows").Children).Label);
    }

    /// <summary>Backlog's <c>monitoring/</c> is "Monitoring &amp; Dashboard", and
    /// every chapter in it carries that heading: its rows keep their kinds' names
    /// even after <c>context.md</c> is edited past the last build.</summary>
    [Fact]
    public async Task A_heading_that_repeats_the_context_s_title_keeps_the_kind_s_name()
    {
        var repo = Repository("monitoring", ["context.md", "features.md", "model.md"]);

        DevbookMenuTestDatabase.Write(repo, ".domain",
        [
            new DevbookMenuOutlineRow("directory", "monitoring", ".domain/monitoring", "Monitoring & Dashboard", IsRoot: false),
            new DevbookMenuOutlineRow("file", "context.md", ".domain/monitoring/context.md", "Monitoring & Dashboard", IsRoot: true, Parent: ".domain/monitoring"),
            new DevbookMenuOutlineRow("file", "features.md", ".domain/monitoring/features.md", "Monitoring & Dashboard", IsRoot: false, Parent: ".domain/monitoring"),
            new DevbookMenuOutlineRow("file", "model.md", ".domain/monitoring/model.md", "Monitoring & Dashboard", IsRoot: false, Parent: ".domain/monitoring")
        ]);
        File.SetLastWriteTimeUtc(Path.Combine(repo, ".domain", "monitoring", "context.md"), DateTime.UtcNow.AddHours(1));

        var context = await LoadContextAsync(repo, "monitoring", domainView: true);

        Assert.Equal(["Features", "Model"], context.Children.Select(node => node.Label));
    }

    /// <summary>ADR 0040's file table gives the images folder no row, and the
    /// board draws none: the chapters show its images, the menu does not.</summary>
    [Fact]
    public async Task The_assets_folder_draws_no_row_but_another_folder_does()
    {
        var repo = Repository("financieel", ["context.md", "domain.md"], "assets", "archief");

        var context = await LoadContextAsync(repo, "financieel", domainView: true);

        Assert.Equal(["financieel/domain.md", "financieel/archief"], context.Children.Select(node => node.Path));
    }

    [Fact]
    public async Task With_the_switch_off_a_split_context_is_todays_flat_list()
    {
        var repo = Repository("financieel", Financieel, "assets");

        var on = await LoadContextAsync(repo, "financieel", domainView: true);
        var off = await LoadContextAsync(repo, "financieel", domainView: false);
        var unswitched = await LoadContextAsync(repo, "financieel", domainView: null);

        // Every Markdown file but the context's own and the `_` ones, one row each.
        var expected = Financieel
            .Where(name => name.EndsWith(".md", StringComparison.Ordinal) && !name.StartsWith('_') && name != "context.md")
            .Select(name => "financieel/" + name)
            .Append("financieel/assets")
            .Order(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(expected, off.Children.Select(node => node.Path).Order(StringComparer.OrdinalIgnoreCase));
        Assert.All(off.Children, node => Assert.Empty(node.Children));
        Assert.Contains(off.Children, node => node.Path == "financieel/dependencies.md");
        Assert.Equal(off.Children.Select(node => (node.Path, node.Label)), unswitched.Children.Select(node => (node.Path, node.Label)));
        Assert.NotEqual(off.Children.Count, on.Children.Count);
    }

    [Fact]
    public async Task With_the_switch_off_an_unsplit_context_is_todays_list()
    {
        var repo = Repository("tasks", Tasks, "assets", "_archify");

        var off = await LoadContextAsync(repo, "tasks", domainView: false);

        Assert.Equal(
            ["tasks/domain.md", "tasks/domain.invariants.md", "tasks/features.md", "tasks/model.md", "tasks/flow.md", "tasks/dependencies.md", "tasks/assets", "tasks/requirements.md"],
            off.Children.Select(node => node.Path));
    }

    [Theory]
    [InlineData("financieel/domain.afspraken.invariants.md", "financieel/domain.afspraken.md")]
    [InlineData("financieel/domain.invariants.md", "financieel/domain.md")]
    [InlineData("financieel/dependencies.md", "financieel/context.md|financieel")]
    [InlineData("financieel/domain.afspraken.md", "")]
    [InlineData("context-map.md", "")]
    public void Names_the_page_a_file_without_a_row_shows_on(string path, string hosts)
    {
        Assert.Equal(hosts.Split('|', StringSplitOptions.RemoveEmptyEntries), DevbookMenu.DomainUnitHosts(path));
    }

    private static DevbookMenuNode Child(DevbookMenuNode node, string label) =>
        Assert.Single(node.Children, child => child.Label == label);

    private static IEnumerable<DevbookMenuNode> Descendants(DevbookMenuNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    /// <summary>A repository whose Domain folder holds a context map and one
    /// context of <paramref name="files"/>, each with a heading, and
    /// <paramref name="folders"/> beside them, each with one asset.</summary>
    private string Repository(string context, IReadOnlyList<string> files, params string[] folders)
    {
        var repo = TempDir();
        var domain = Path.Combine(repo, ".domain");
        var directory = Path.Combine(domain, context);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(domain, "context-map.md"), "# Context map");

        foreach (var file in files) File.WriteAllText(Path.Combine(directory, file), $"# {file}");
        foreach (var folder in folders)
        {
            Directory.CreateDirectory(Path.Combine(directory, folder));
            File.WriteAllBytes(Path.Combine(directory, folder, "01-overview.png"), [0x89, 0x50]);
        }

        return repo;
    }

    /// <param name="domainView">The switch; <see langword="null"/> for a host
    /// that composes no feature settings at all.</param>
    private async Task<DevbookMenuNode> LoadContextAsync(string repo, string context, bool? domainView)
    {
        var settings = new GitHubSettingsStore(Path.Combine(TempDir(), "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        settings.SetRepositories(repositories);
        settings.SetCloneDirectory("backlog", repo);

        AppFeatureSettingsStore? features = null;
        if (domainView is { } enabled)
        {
            features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(TempDir(), "features.json"));
            Assert.Null(features.SetEnabled(DevbookFeatures.DomainView, enabled));
        }

        var tree = await new DevbookMenu(new DevbookFolderSource(settings), features)
            .LoadAsync(["domain"], cancellationToken: TestContext.Current.CancellationToken);

        var root = Assert.Single(tree.Roots);
        Assert.True(root.Available, root.Message);
        return Assert.Single(root.Children, node => node.Key == context);
    }

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "devbook-menu-domain-units-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
