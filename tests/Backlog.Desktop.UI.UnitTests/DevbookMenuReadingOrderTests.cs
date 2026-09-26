using System.Text.RegularExpressions;

using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Devbook;

using Microsoft.Data.Sqlite;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Devbook pane's left rail against the folder convention's reading order.
///
/// <para>The rail used to enumerate the directory and sort it alphabetically, so
/// the order the convention gives <c>.domain</c>, <c>.design</c> and
/// <c>.tech</c> was invisible to a reader — the one surface where they would look
/// for it. The order is derived from names alone (<c>DevbookReadingConvention</c>;
/// local ADR 0016 retired <c>_reading-order.json</c>, and a stray one is ignored);
/// these hold the rail to it, and hold it to the ladder ADR 0004 requires
/// underneath: a directory the convention says nothing about reads exactly as it
/// did before, whether or not a generated database is there at all.</para>
///
/// <para>The label half is deliberately timid. The database knows a chapter's
/// real title, which is what turns <c>Dev Pc Management</c> into
/// <c>Dev PC Management</c> — but inside a bounded context every document carries
/// the context's own H1, so adopting titles wholesale would replace six
/// distinguishable rows with six rows called <c>Inbox</c>. A title is therefore
/// taken only where it still tells one row from another.</para>
/// </summary>
public sealed class DevbookMenuReadingOrderTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    /// <summary>The contexts of this repository's domain folder, in the order a
    /// retired <c>_reading-order.json</c> used to declare them — which is not the
    /// order the convention reads them in.</summary>
    private static readonly string[] DomainContexts =
    [
        "inbox", "capture", "tasks", "roadmap", "devbook", "productivity", "environment",
        "repository-management", "dev-pc-management", "sessions", "monitoring", "technology-stack"
    ];

    [Fact]
    public async Task Orders_the_domain_rail_by_the_convention_and_ignores_a_stray_reading_order_file()
    {
        var repo = TempDir();
        var domain = Path.Combine(repo, ".domain");
        Directory.CreateDirectory(domain);
        File.WriteAllText(Path.Combine(domain, "context-map.md"), "# Context map");
        foreach (var context in DomainContexts)
        {
            Directory.CreateDirectory(Path.Combine(domain, context));
            File.WriteAllText(Path.Combine(domain, context, "domain.md"), $"# {context}");
        }

        WriteStrayReadingOrder(domain, ".domain", "context-map.md", DomainContexts);

        var tree = await LoadAsync(repo, "domain");

        Assert.Equal(
            ["context-map.md", .. DomainContexts.Order(StringComparer.Ordinal)],
            tree.Children.Select(node => node.Path));
    }

    [Fact]
    public async Task Orders_the_design_rail_by_the_convention()
    {
        var repo = TempDir();
        var design = Path.Combine(repo, ".design");
        Directory.CreateDirectory(design);

        string[] files =
        [
            "content-editing.md", "accessibility.md", "component-libraries.md", "design-principles.md",
            "color-scheme.md", "typography-and-layout.md", "interaction-guidelines.md"
        ];

        File.WriteAllText(Path.Combine(design, "README.md"), "# Design Devbook (`.design`)");
        foreach (var file in files) File.WriteAllText(Path.Combine(design, file), "# Chapter");

        var tree = await LoadAsync(repo, "design");

        // The convention's prescribed guidelines in its sequence, then the
        // repository's own chapter.
        Assert.Equal(
            [
                "README.md", "design-principles.md", "color-scheme.md", "typography-and-layout.md",
                "interaction-guidelines.md", "accessibility.md", "component-libraries.md", "content-editing.md"
            ],
            tree.Children.Select(node => node.Path));
    }

    [Fact]
    public async Task Orders_the_technology_rail_with_shared_first_and_tooling_last()
    {
        var repo = TempDir();
        var tech = Path.Combine(repo, ".tech");
        Directory.CreateDirectory(tech);
        foreach (var file in new[] { "tooling.md", "desktop.md", "technology-graph.md", "shared.md", "cloud.md" })
        {
            File.WriteAllText(Path.Combine(tech, file), "# Layer");
        }

        var tree = await LoadAsync(repo, "tech");

        Assert.Equal(
            ["technology-graph.md", "shared.md", "cloud.md", "desktop.md", "tooling.md"],
            tree.Children.Select(node => node.Path));
    }

    /// <summary>
    /// Rung five of the ADR's ladder, and the reason the alphabetical sort stays
    /// where it is: the convention has no entry for <c>.arc42</c>, because its
    /// numbered chapters sort themselves and the two record folders belong at
    /// 09.5 and 11.5. A directory the convention says nothing about is the rail's
    /// own sort — and a stray <c>_reading-order.json</c> changes none of it.
    ///
    /// <para>The labels ride that rung too, which is what the database is doing
    /// here. It titles the two record folders "Architecture Decision Records" and
    /// "Technical Debt Records" — truer than the rail's own labels, and not the
    /// rows this rail draws: a level holding its old sort holds its old ADR and
    /// TDR acronyms with it.</para>
    /// </summary>
    [Fact]
    public async Task A_directory_without_a_convention_keeps_the_alphabetical_rail()
    {
        var repo = TempDir();
        var arc42 = Path.Combine(repo, ".arc42");
        Directory.CreateDirectory(Path.Combine(arc42, "adr"));
        Directory.CreateDirectory(Path.Combine(arc42, "tdr"));
        File.WriteAllText(Path.Combine(arc42, "09-architecture-decisions.md"), "# Decisions");
        File.WriteAllText(Path.Combine(arc42, "10-quality-requirements.md"), "# Quality");
        File.WriteAllText(Path.Combine(arc42, "11-risks-and-technical-debt.md"), "# Risks");
        File.WriteAllText(Path.Combine(arc42, "12-glossary.md"), "# Glossary");
        File.WriteAllText(Path.Combine(arc42, "adr", "README.md"), "# Architecture Decision Records");
        File.WriteAllText(Path.Combine(arc42, "tdr", "README.md"), "# Technical Debt Records");

        WriteStrayReadingOrder(arc42, ".arc42", "12-glossary.md", ["tdr", "adr"]);

        WriteDatabase(repo, ".arc42",
        [
            new OutlineRow("directory", "adr", ".arc42/adr", "Architecture Decision Records", IsRoot: false),
            new OutlineRow("file", "README.md", ".arc42/adr/README.md", "Architecture Decision Records", IsRoot: true, Parent: ".arc42/adr"),
            new OutlineRow("directory", "tdr", ".arc42/tdr", "Technical Debt Records", IsRoot: false),
            new OutlineRow("file", "README.md", ".arc42/tdr/README.md", "Technical Debt Records", IsRoot: true, Parent: ".arc42/tdr")
        ]);

        var tree = await LoadAsync(repo, "arc42");

        Assert.Equal(
            ["09-architecture-decisions.md", "adr", "10-quality-requirements.md", "11-risks-and-technical-debt.md", "tdr", "12-glossary.md"],
            tree.Children.Select(node => node.Path));
        Assert.Equal(["ADR", "TDR"], tree.Children.Where(node => node.Kind == DevbookMenuNodeKind.Folder).Select(node => node.Label));
    }

    /// <summary>
    /// A record folder has no convention entry, so it keeps the rail's own sort
    /// — but it still opens with its root document, as the database outline
    /// has it (the README declares <c>index: root</c>). With no database the
    /// README is found by name. Either way the README reads first, and the
    /// records and the nested guidelines folder keep their order behind it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_record_folder_opens_with_its_readme(bool withDatabase)
    {
        var repo = TempDir();
        WriteArc42(repo);

        if (withDatabase) WriteArc42Database(repo);

        var tree = await LoadAsync(repo, "arc42");

        var adr = Assert.Single(tree.Children, node => node.Label == "ADR");
        Assert.Equal(
            ["adr/README.md", "adr/0001-desktop-stack.md", "adr/0002-sqlite-store.md", "adr/guidelines"],
            adr.Children.Select(node => node.Path));

        var guidelines = Assert.Single(adr.Children, node => node.Path == "adr/guidelines");
        Assert.Equal(
            ["adr/guidelines/README.md", "adr/guidelines/0001-adopt-dotnet-10.md"],
            guidelines.Children.Select(node => node.Path));

        var tdr = Assert.Single(tree.Children, node => node.Label == "TDR");
        Assert.Equal(["tdr/README.md", "tdr/0001-first-debt.md"], tdr.Children.Select(node => node.Path));

    }

    /// <summary>
    /// The top level of arc42 is the same rung: its root document is the
    /// numbered introduction, which already sorts first, so pinning it moves
    /// nothing — the record folders stay at 09.5 and 11.5 and keep their ADR
    /// and TDR labels even with a database that titles them.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pinning_the_root_leaves_the_arc42_top_level_as_it_was(bool withDatabase)
    {
        var repo = TempDir();
        WriteArc42(repo);

        if (withDatabase) WriteArc42Database(repo);

        var tree = await LoadAsync(repo, "arc42");

        Assert.Equal(
            ["01-introduction-and-goals.md", "09-architecture-decisions.md", "adr", "10-quality-requirements.md", "11-risks-and-technical-debt.md", "tdr"],
            tree.Children.Select(node => node.Path));
        Assert.Equal(["ADR", "TDR"], tree.Children.Where(node => node.Kind == DevbookMenuNodeKind.Folder).Select(node => node.Label));
    }

    /// <summary>
    /// The database is the authority where it knows the directory: a README it
    /// does not mark as root — the file never declared <c>index: root</c> — is
    /// not pinned by name, so the rail and the outline agree.
    /// </summary>
    [Fact]
    public async Task A_readme_the_outline_does_not_mark_as_root_keeps_its_sorted_place()
    {
        var repo = TempDir();
        WriteArc42(repo);
        WriteArc42Database(repo, readmesAreRoots: false);

        var tree = await LoadAsync(repo, "arc42");

        var tdr = Assert.Single(tree.Children, node => node.Label == "TDR");
        Assert.Equal(["tdr/0001-first-debt.md", "tdr/README.md"], tdr.Children.Select(node => node.Path));
    }

    /// <summary>An arc42 folder shaped like this repository's: numbered chapters
    /// with the introduction as root, and two record folders each opened by a
    /// README, one of them holding a nested record folder of its own.</summary>
    private static string WriteArc42(string repo)
    {
        var arc42 = Path.Combine(repo, ".arc42");
        Directory.CreateDirectory(Path.Combine(arc42, "adr", "guidelines"));
        Directory.CreateDirectory(Path.Combine(arc42, "tdr"));

        File.WriteAllText(Path.Combine(arc42, "01-introduction-and-goals.md"), "# 01. Introduction and Goals\n\n```meta\nindex: root\n```\n");
        File.WriteAllText(Path.Combine(arc42, "09-architecture-decisions.md"), "# 09. Decisions");
        File.WriteAllText(Path.Combine(arc42, "10-quality-requirements.md"), "# 10. Quality");
        File.WriteAllText(Path.Combine(arc42, "11-risks-and-technical-debt.md"), "# 11. Risks");
        File.WriteAllText(Path.Combine(arc42, "adr", "README.md"), "# Architecture Decision Records\n\n```meta\nindex: root\n```\n");
        File.WriteAllText(Path.Combine(arc42, "adr", "0001-desktop-stack.md"), "# ADR 0001: Desktop stack");
        File.WriteAllText(Path.Combine(arc42, "adr", "0002-sqlite-store.md"), "# ADR 0002: SQLite store");
        File.WriteAllText(Path.Combine(arc42, "adr", "guidelines", "README.md"), "# Guidelines\n\n```meta\nindex: root\n```\n");
        File.WriteAllText(Path.Combine(arc42, "adr", "guidelines", "0001-adopt-dotnet-10.md"), "# 0001: Adopt .NET 10");
        File.WriteAllText(Path.Combine(arc42, "tdr", "README.md"), "# Technical Debt Records\n\n```meta\nindex: root\n```\n");
        File.WriteAllText(Path.Combine(arc42, "tdr", "0001-first-debt.md"), "# TDR 0001: First debt");
        return arc42;
    }

    /// <summary>The outline the builder writes for <see cref="WriteArc42"/>: each
    /// README marked root, in the order the convention gives the database.</summary>
    private static void WriteArc42Database(string repo, bool readmesAreRoots = true) =>
        WriteDatabase(repo, ".arc42",
        [
            new OutlineRow("file", "01-introduction-and-goals.md", ".arc42/01-introduction-and-goals.md", "01. Introduction and Goals", IsRoot: true),
            new OutlineRow("file", "09-architecture-decisions.md", ".arc42/09-architecture-decisions.md", "09. Decisions", IsRoot: false),
            new OutlineRow("file", "10-quality-requirements.md", ".arc42/10-quality-requirements.md", "10. Quality", IsRoot: false),
            new OutlineRow("file", "11-risks-and-technical-debt.md", ".arc42/11-risks-and-technical-debt.md", "11. Risks", IsRoot: false),
            new OutlineRow("directory", "adr", ".arc42/adr", "Architecture Decision Records", IsRoot: false),
            new OutlineRow("file", "README.md", ".arc42/adr/README.md", "Architecture Decision Records", IsRoot: readmesAreRoots, Parent: ".arc42/adr"),
            new OutlineRow("file", "0001-desktop-stack.md", ".arc42/adr/0001-desktop-stack.md", "ADR 0001: Desktop stack", IsRoot: false, Parent: ".arc42/adr"),
            new OutlineRow("file", "0002-sqlite-store.md", ".arc42/adr/0002-sqlite-store.md", "ADR 0002: SQLite store", IsRoot: false, Parent: ".arc42/adr"),
            new OutlineRow("directory", "guidelines", ".arc42/adr/guidelines", "Guidelines", IsRoot: false, Parent: ".arc42/adr"),
            new OutlineRow("file", "README.md", ".arc42/adr/guidelines/README.md", "Guidelines", IsRoot: readmesAreRoots, Parent: ".arc42/adr/guidelines"),
            new OutlineRow("file", "0001-adopt-dotnet-10.md", ".arc42/adr/guidelines/0001-adopt-dotnet-10.md", "0001: Adopt .NET 10", IsRoot: false, Parent: ".arc42/adr/guidelines"),
            new OutlineRow("directory", "tdr", ".arc42/tdr", "Technical Debt Records", IsRoot: false),
            new OutlineRow("file", "README.md", ".arc42/tdr/README.md", "Technical Debt Records", IsRoot: readmesAreRoots, Parent: ".arc42/tdr"),
            new OutlineRow("file", "0001-first-debt.md", ".arc42/tdr/0001-first-debt.md", "TDR 0001: First debt", IsRoot: false, Parent: ".arc42/tdr")
        ]);

    [Fact]
    public async Task A_stray_reading_order_file_orders_nothing()
    {
        var repo = TempDir();
        var design = Path.Combine(repo, ".design");
        Directory.CreateDirectory(design);
        File.WriteAllText(Path.Combine(design, "accessibility.md"), "# Accessibility");
        File.WriteAllText(Path.Combine(design, "README.md"), "# Design");
        File.WriteAllText(Path.Combine(design, "zebra-crossings.md"), "# Zebras");
        WriteStrayReadingOrder(design, ".design", "zebra-crossings.md", ["accessibility.md", "README.md"]);

        var tree = await LoadAsync(repo, "design");

        Assert.Equal(
            ["README.md", "accessibility.md", "zebra-crossings.md"],
            tree.Children.Select(node => node.Path));
        Assert.DoesNotContain(tree.Children, node => node.Path.StartsWith('_'));
    }

    /// <summary>
    /// A chapter the convention does not prescribe still has a row, after the
    /// prescribed ones and by name, and a prescribed chapter that is not there is
    /// simply not drawn.
    /// </summary>
    [Fact]
    public async Task Unprescribed_entries_follow_the_prescribed_ones_by_name()
    {
        var repo = TempDir();
        var design = Path.Combine(repo, ".design");
        Directory.CreateDirectory(design);
        File.WriteAllText(Path.Combine(design, "README.md"), "# Design");
        File.WriteAllText(Path.Combine(design, "design-principles.md"), "# Principles");
        File.WriteAllText(Path.Combine(design, "color-scheme.md"), "# Colour");
        File.WriteAllText(Path.Combine(design, "zebra-crossings.md"), "# Zebras");
        File.WriteAllText(Path.Combine(design, "accessibility.md"), "# Accessibility");

        var tree = await LoadAsync(repo, "design");

        Assert.Equal(
            ["README.md", "design-principles.md", "color-scheme.md", "accessibility.md", "zebra-crossings.md"],
            tree.Children.Select(node => node.Path));
        Assert.DoesNotContain(tree.Children, node => node.Path == "content-editing.md");
    }

    /// <summary>
    /// The nested level: a bounded context's own documents read in the
    /// convention's <c>domain/*</c> sequence, which is the sequence the domain
    /// panel beside the rail already shows.
    /// </summary>
    [Fact]
    public async Task Orders_a_domain_context_by_the_bounded_context_convention()
    {
        var repo = TempDir();
        var domain = Path.Combine(repo, ".domain");
        var inbox = Path.Combine(domain, "inbox");
        Directory.CreateDirectory(inbox);
        File.WriteAllText(Path.Combine(domain, "context-map.md"), "# Context map");
        foreach (var file in new[] { "domain.md", "features.md", "model.md", "flow.md", "dependencies.md", "naming.md" })
        {
            File.WriteAllText(Path.Combine(inbox, file), "# Inbox");
        }

        var tree = await LoadAsync(repo, "domain");

        var context = Assert.Single(tree.Children, node => node.Path == "inbox");
        Assert.Equal(
            ["inbox/domain.md", "inbox/features.md", "inbox/model.md", "inbox/flow.md", "inbox/dependencies.md", "inbox/naming.md"],
            context.Children.Select(node => node.Path));
    }

    /// <summary>
    /// The second symptom: the rail humanised the folder name, so the context
    /// whose chapters all say "Dev PC Management" was labelled "Dev Pc
    /// Management". The database holds the real title, and here it is worth
    /// having because it distinguishes nothing from nothing — no sibling shares
    /// it.
    /// </summary>
    [Fact]
    public async Task Labels_a_context_with_the_outline_title_where_it_distinguishes_the_row()
    {
        var repo = TempDir();
        var domain = Path.Combine(repo, ".domain");
        Directory.CreateDirectory(Path.Combine(domain, "dev-pc-management"));
        Directory.CreateDirectory(Path.Combine(domain, "monitoring"));
        File.WriteAllText(Path.Combine(domain, "context-map.md"), "# Context map");
        File.WriteAllText(Path.Combine(domain, "dev-pc-management", "domain.md"), "# Dev PC Management");
        File.WriteAllText(Path.Combine(domain, "monitoring", "domain.md"), "# Monitoring & Dashboard");

        WriteDatabase(repo, ".domain",
        [
            new OutlineRow("directory", "dev-pc-management", ".domain/dev-pc-management", "Dev PC Management", IsRoot: false),
            new OutlineRow("file", "domain.md", ".domain/dev-pc-management/domain.md", "Dev PC Management", IsRoot: true, Parent: ".domain/dev-pc-management"),
            new OutlineRow("directory", "monitoring", ".domain/monitoring", "Monitoring & Dashboard", IsRoot: false),
            new OutlineRow("file", "domain.md", ".domain/monitoring/domain.md", "Monitoring & Dashboard", IsRoot: true, Parent: ".domain/monitoring")
        ]);

        var tree = await LoadAsync(repo, "domain");

        Assert.Equal("Dev PC Management", tree.Children.Single(node => node.Path == "dev-pc-management").Label);
        Assert.Equal("Monitoring & Dashboard", tree.Children.Single(node => node.Path == "monitoring").Label);
    }

    /// <summary>
    /// And the reason the adoption is conditional. Every document in a bounded
    /// context carries the context's H1, and the folder's root document describes
    /// the folder rather than itself — so a rail that took every title would show
    /// six rows called "Inbox" and a design README called "Design Devbook
    /// (.design)". The filename is what tells those rows apart, so the filename
    /// stays.
    /// </summary>
    [Fact]
    public async Task Keeps_the_file_label_where_the_outline_title_would_not_distinguish_the_row()
    {
        var repo = TempDir();
        var domain = Path.Combine(repo, ".domain");
        var inbox = Path.Combine(domain, "inbox");
        Directory.CreateDirectory(inbox);
        File.WriteAllText(Path.Combine(domain, "context-map.md"), "# Context map");

        string[] chapters = ["domain.md", "features.md", "model.md", "flow.md", "dependencies.md", "naming.md"];
        foreach (var file in chapters) File.WriteAllText(Path.Combine(inbox, file), "# Inbox");

        WriteDatabase(repo, ".domain",
        [
            new OutlineRow("directory", "inbox", ".domain/inbox", "Inbox", IsRoot: false),
            .. chapters.Select(file => new OutlineRow(
                "file", file, $".domain/inbox/{file}", "Inbox", IsRoot: file == "domain.md", Parent: ".domain/inbox"))
        ]);

        var tree = await LoadAsync(repo, "domain");

        var context = Assert.Single(tree.Children, node => node.Path == "inbox");
        Assert.Equal("Inbox", context.Label);
        Assert.Equal(
            ["Domain", "Features", "Model", "Flow", "Dependencies", "Naming"],
            context.Children.Select(node => node.Label));

        // The property behind all three guards: whatever a level adopts, its rows
        // still tell each other apart.
        Assert.Equal(
            context.Children.Select(node => node.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            context.Children.Count);
    }

    [Fact]
    public async Task Keeps_the_readme_label_rather_than_the_folder_title()
    {
        var repo = TempDir();
        var design = Path.Combine(repo, ".design");
        Directory.CreateDirectory(design);
        File.WriteAllText(Path.Combine(design, "README.md"), "# Design Devbook (`.design`)");
        File.WriteAllText(Path.Combine(design, "design-principles.md"), "# Design Principles");

        WriteDatabase(repo, ".design",
        [
            new OutlineRow("file", "README.md", ".design/README.md", "Design Devbook (.design)", IsRoot: true),
            new OutlineRow("file", "design-principles.md", ".design/design-principles.md", "Design Principles", IsRoot: false)
        ]);

        var tree = await LoadAsync(repo, "design");

        Assert.Equal("README", tree.Children.Single(node => node.Path == "README.md").Label);
    }

    /// <summary>
    /// A fresh clone, and this worktree: no <c>_meta/</c> at all. Ordering still
    /// comes out of the convention and every label is exactly the one
    /// the rail drew before there was an index to read.
    /// </summary>
    [Fact]
    public async Task Without_any_generated_index_the_order_holds_and_the_labels_do_not_move()
    {
        var repo = TempDir();
        var domain = Path.Combine(repo, ".domain");
        Directory.CreateDirectory(Path.Combine(domain, "dev-pc-management"));
        Directory.CreateDirectory(Path.Combine(domain, "monitoring"));
        File.WriteAllText(Path.Combine(domain, "context-map.md"), "# Context map");
        File.WriteAllText(Path.Combine(domain, "dev-pc-management", "domain.md"), "# Dev PC Management");
        File.WriteAllText(Path.Combine(domain, "monitoring", "domain.md"), "# Monitoring & Dashboard");

        var tree = await LoadAsync(repo, "domain");

        Assert.False(Directory.Exists(Path.Combine(repo, "_meta")));
        Assert.Equal(["context-map.md", "dev-pc-management", "monitoring"], tree.Children.Select(node => node.Path));
        Assert.Equal(["Context Map", "Dev Pc Management", "Monitoring"], tree.Children.Select(node => node.Label));
        Assert.DoesNotContain(Descendants(tree), node => node.Kind == DevbookMenuNodeKind.Message);
    }

    /// <summary>
    /// A tab click costs one folder walk, not a parse of the corpus behind it.
    /// The chapters here are held open exclusively, so a rail that read a single
    /// one of them to find a title or an order could not build at all.
    /// </summary>
    [Fact]
    public async Task Builds_the_rail_without_opening_a_markdown_file()
    {
        if (!OperatingSystem.IsWindows()) return; // only Windows enforces the share mode below

        var repo = TempDir();
        var domain = Path.Combine(repo, ".domain");
        var inbox = Path.Combine(domain, "inbox");
        Directory.CreateDirectory(inbox);
        File.WriteAllText(Path.Combine(domain, "context-map.md"), "# Context map");

        string[] chapters = ["domain.md", "features.md", "model.md"];
        foreach (var file in chapters) File.WriteAllText(Path.Combine(inbox, file), "# Inbox");

        var locks = chapters
            .Select(file => File.Open(Path.Combine(inbox, file), FileMode.Open, FileAccess.Read, FileShare.None))
            .ToList();

        try
        {
            var tree = await LoadAsync(repo, "domain");

            var context = Assert.Single(tree.Children, node => node.Path == "inbox");
            Assert.Equal(
                ["inbox/domain.md", "inbox/features.md", "inbox/model.md"],
                context.Children.Select(node => node.Path));
            Assert.DoesNotContain(Descendants(tree), node => node.Kind == DevbookMenuNodeKind.Message);
        }
        finally
        {
            foreach (var handle in locks) handle.Dispose();
        }
    }

    /// <summary>
    /// A chapter keeps its filename label even when its generated title is
    /// perfectly distinguishing, because an H1 is written to open a chapter and a
    /// rail row is not the place for a sentence. A bounded context is ordered by
    /// the convention, so every guard above this one lets its chapters through,
    /// and record titles show how long an H1 runs — from 57 to 104 characters in
    /// `.arc42/adr`, "ADR 0008:
    /// Devbook reads from a cached branch snapshot when there is no clone; only
    /// a clone is editable" where the rail draws "0008 Devbook Reads From A
    /// Branch Snapshot When There Is No Clone". Only a directory takes its label
    /// from the outline, which is where the two labels the issue names live.
    /// </summary>
    [Fact]
    public async Task Keeps_a_chapter_label_even_where_the_outline_title_distinguishes_it()
    {
        var repo = TempDir();
        var domain = Path.Combine(repo, ".domain");
        var inbox = Path.Combine(domain, "inbox");
        Directory.CreateDirectory(inbox);
        File.WriteAllText(Path.Combine(domain, "context-map.md"), "# Context map");
        File.WriteAllText(Path.Combine(inbox, "domain.md"), "# Inbox & Triage");
        File.WriteAllText(Path.Combine(inbox, "features.md"), "# Inbox: capture, triage and promote an entry");
        File.WriteAllText(Path.Combine(inbox, "model.md"), "# Inbox model, entities and invariants");

        WriteDatabase(repo, ".domain",
        [
            new OutlineRow("directory", "inbox", ".domain/inbox", "Inbox & Triage", IsRoot: false),
            new OutlineRow("file", "domain.md", ".domain/inbox/domain.md", "Inbox & Triage", IsRoot: true, Parent: ".domain/inbox"),
            new OutlineRow("file", "features.md", ".domain/inbox/features.md", "Inbox: capture, triage and promote an entry", IsRoot: false, Parent: ".domain/inbox"),
            new OutlineRow("file", "model.md", ".domain/inbox/model.md", "Inbox model, entities and invariants", IsRoot: false, Parent: ".domain/inbox")
        ]);

        var tree = await LoadAsync(repo, "domain");
        var context = Assert.Single(tree.Children, node => node.Path == "inbox");

        // The directory row does take its title: that is the whole point of
        // reading the outline at all.
        Assert.Equal("Inbox & Triage", context.Label);

        // Its chapters do not, however well their titles would have told them apart.
        Assert.Equal(["Domain", "Features", "Model"], context.Children.Select(node => node.Label));
    }

    /// <summary>
    /// Deciding a label costs a <c>stat</c> and never a read. The database's drift
    /// check earns its exactness by hashing a file whose modification time moved
    /// but whose length did not — correct for a panel, which ADR 0004 lets spend
    /// one file's parse to serve current content, and wrong for the rail, which
    /// asks the question once per row it draws. A branch switch moves every
    /// modification time in the corpus, so the exact check would turn one tab
    /// click into a hash of every chapter on the thread that is drawing.
    ///
    /// <para>So the rail takes the coarse answer: a chapter whose <c>stat</c> no
    /// longer matches the row is treated as drifted rather than read to find out,
    /// and it keeps its filename label. Here the file's content is untouched — its
    /// hash still matches — so a rail that hashed would adopt the generated title,
    /// and this asserts it does not.</para>
    /// </summary>
    [Fact]
    public async Task Keeps_the_file_label_when_a_chapter_was_touched_rather_than_hashing_it()
    {
        var repo = TempDir();
        var domain = Path.Combine(repo, ".domain");
        var context = Path.Combine(domain, "dev-pc-management");
        Directory.CreateDirectory(context);
        File.WriteAllText(Path.Combine(domain, "context-map.md"), "# Context map");

        var chapter = Path.Combine(context, "domain.md");
        File.WriteAllText(chapter, "# Dev PC Management");

        WriteDatabase(repo, ".domain",
        [
            new OutlineRow("directory", "dev-pc-management", ".domain/dev-pc-management", "Dev PC Management", IsRoot: false),
            new OutlineRow("file", "domain.md", ".domain/dev-pc-management/domain.md", "Dev PC Management", IsRoot: true, Parent: ".domain/dev-pc-management")
        ]);

        // What a branch switch does: the modification time moves, the bytes do not.
        File.SetLastWriteTimeUtc(chapter, File.GetLastWriteTimeUtc(chapter).AddMinutes(5));

        var tree = await LoadAsync(repo, "domain");

        Assert.Equal("Dev Pc Management", tree.Children.Single(node => node.Path == "dev-pc-management").Label);
        Assert.DoesNotContain(Descendants(tree), node => node.Kind == DevbookMenuNodeKind.Message);
    }

    private static IEnumerable<DevbookMenuNode> Descendants(DevbookMenuNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private async Task<DevbookMenuNode> LoadAsync(string repo, string areaKey)
    {
        var settings = NewSettingsStore();
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        settings.SetRepositories(repositories);
        settings.SetCloneDirectory("backlog", repo);

        var tree = await new DevbookMenu(new DevbookFolderSource(settings))
            .LoadAsync([areaKey], cancellationToken: TestContext.Current.CancellationToken);

        var root = Assert.Single(tree.Roots);
        Assert.True(root.Available, root.Message);
        return root;
    }

    /// <summary>A well-formed <c>_reading-order.json</c> in the shape the retired
    /// files had, declaring an order the convention disagrees with — so a rail that
    /// still read one would show it.</summary>
    private static void WriteStrayReadingOrder(string folder, string scope, string root, IReadOnlyList<string> order) =>
        File.WriteAllText(
            Path.Combine(folder, "_reading-order.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                version = 1,
                scope,
                directories = new Dictionary<string, object> { [scope] = new { root, order } }
            }));

    /// <summary>One outline row of the generated database: what the writer
    /// derived by the convention, with the title it read from the
    /// Markdown.</summary>
    private sealed record OutlineRow(string Type, string Name, string Path, string Title, bool IsRoot, string? Parent = null);

    /// <summary>
    /// A generated devbook database for the repository, describing
    /// <paramref name="scope"/>, built from the writer's own DDL in
    /// <c>tools/devbook/devbook-schema.sql</c> — nothing on the C# side
    /// restates that schema, fixtures included.
    /// </summary>
    private static void WriteDatabase(string repositoryRoot, string scope, IReadOnlyList<OutlineRow> rows)
    {
        var databasePath = DevbookDatabaseLocation.ForRepositoryRoot(repositoryRoot)!;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        connection.Open();
        Execute(connection, WriterSchema());
        Execute(connection, $"INSERT INTO meta (key, value) VALUES ('schemaVersion', '{DevbookDatabaseSchema.Version}')");
        Execute(connection, "INSERT INTO meta (key, value) VALUES ('generatedAt', '2026-09-08T01:23:45.000Z')");

        var ids = new Dictionary<string, long>(StringComparer.Ordinal);
        var ordinal = 0;

        foreach (var row in rows)
        {
            var parent = row.Parent is not null && ids.TryGetValue(row.Parent, out var parentId)
                ? parentId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "NULL";

            Execute(connection, $"""
                INSERT INTO outline_entry (scope, parent_id, ordinal, type, name, path, title, status, kind, is_root)
                VALUES ('{scope}', {parent}, {ordinal++}, '{row.Type}', '{row.Name}', '{row.Path}',
                        '{row.Title.Replace("'", "''")}', 'active', 'domain', {(row.IsRoot ? 1 : 0)})
                """);

            ids[row.Path] = Scalar(connection, "SELECT last_insert_rowid()");

            if (row.Type != "file") continue;

            // The drift facts, so the rail trusts the row instead of treating the
            // chapter as one the writer never saw.
            var file = new FileInfo(Path.Combine(repositoryRoot, row.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file.FullName)));
            var mtime = (long)Math.Round(
                (double)(new DateTimeOffset(file.LastWriteTimeUtc).UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / TimeSpan.TicksPerMillisecond,
                MidpointRounding.AwayFromZero);

            Execute(connection, $"""
                INSERT INTO chapter (path, folder, slug, level, title, status, line, text, search_text, content_hash, source_hash, size, mtime)
                VALUES ('{row.Path}', 'domain', 'slug', 1, '{row.Title.Replace("'", "''")}', 'active', 1, 'text', 'text', 'aa', '{hash}', {file.Length}, {mtime})
                """);
        }

        SqliteConnection.ClearPool(connection);
        connection.Close();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string WriterSchema() => DevbookDatabaseSchema.Ddl;

    private GitHubSettingsStore NewSettingsStore() => new(Path.Combine(TempDir(), "github.json"));

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "devbook-menu-order-tests", Guid.NewGuid().ToString("n"));
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
