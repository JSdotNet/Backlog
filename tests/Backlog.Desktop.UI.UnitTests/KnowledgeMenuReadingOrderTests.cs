using System.Text.RegularExpressions;

using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Knowledge;

using Microsoft.Data.Sqlite;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The knowledge pane's left rail against the authored reading order.
///
/// <para>The rail used to enumerate the directory and sort it alphabetically, so
/// the order <c>_reading-order.json</c> declares for <c>.domain</c> and
/// <c>.design</c> was invisible to a reader — the one surface where they would
/// look for it. ADR 0004 made that file the authored half of the knowledge layer;
/// these hold the rail to it, and hold it to the ladder the ADR requires
/// underneath: a folder that declares nothing readable reads exactly as it did
/// before, whether or not a generated database is there at all.</para>
///
/// <para>The label half is deliberately timid. The database knows a chapter's
/// real title, which is what turns <c>Dev Pc Management</c> into
/// <c>Dev PC Management</c> — but inside a bounded context every document carries
/// the context's own H1, so adopting titles wholesale would replace six
/// distinguishable rows with six rows called <c>Inbox</c>. A title is therefore
/// taken only where it still tells one row from another.</para>
/// </summary>
public sealed class KnowledgeMenuReadingOrderTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    /// <summary>The contexts <c>.domain</c> declares, in the order it declares
    /// them — which is not the order any of them sort in.</summary>
    private static readonly string[] DomainContexts =
    [
        "inbox", "capture", "tasks", "roadmap", "second-brain", "productivity", "environment",
        "repository-management", "dev-pc-management", "sessions", "monitoring", "technology-stack"
    ];

    [Fact]
    public async Task Orders_the_domain_rail_by_the_authored_reading_order()
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

        WriteDomainReadingOrder(domain);

        var tree = await LoadAsync(repo, "domain");

        Assert.Equal(
            ["context-map.md", .. DomainContexts],
            tree.Children.Select(node => node.Path));
    }

    [Fact]
    public async Task Orders_the_design_rail_by_the_authored_reading_order()
    {
        var repo = TempDir();
        var design = Path.Combine(repo, ".design");
        Directory.CreateDirectory(design);

        string[] declared =
        [
            "design-principles.md", "color-scheme.md", "typography-and-layout.md", "interaction-guidelines.md",
            "content-editing.md", "accessibility.md", "component-libraries.md"
        ];

        File.WriteAllText(Path.Combine(design, "README.md"), "# Design Knowledge (`.design`)");
        foreach (var file in declared) File.WriteAllText(Path.Combine(design, file), "# Chapter");

        CopyCommittedReadingOrder(".design", design);

        var tree = await LoadAsync(repo, "design");

        Assert.Equal(
            ["README.md", .. declared],
            tree.Children.Select(node => node.Path));
    }

    /// <summary>
    /// Rung five of the ADR's ladder, and the reason the alphabetical sort stays
    /// where it is: <c>.arc42</c> declares <c>root: null</c> and an empty order on
    /// purpose, because its numbered chapters sort themselves and the two record
    /// folders belong at 09.5 and 11.5. A declaration this reader cannot use is
    /// the same answer as no declaration at all.
    ///
    /// <para>The labels ride that rung too, which is what the database is doing
    /// here. It titles the two record folders "Architecture Decision Records" and
    /// "Technical Debt Records" — truer than the rail's own labels, and not the
    /// rows this rail draws: a level holding its old sort holds its old ADR and
    /// TDR acronyms with it.</para>
    /// </summary>
    [Fact]
    public async Task An_empty_declaration_keeps_the_alphabetical_rail()
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

        WriteReadingOrder(arc42, """
            {
              "version": 1,
              "scope": ".arc42",
              "directories": {
                ".arc42": { "root": null, "order": [] }
              }
            }
            """);

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
        Assert.Equal(["ADR", "TDR"], tree.Children.Where(node => node.Kind == KnowledgeMenuNodeKind.Folder).Select(node => node.Label));
    }

    [Fact]
    public async Task A_malformed_declaration_keeps_the_alphabetical_rail()
    {
        var repo = TempDir();
        var design = Path.Combine(repo, ".design");
        Directory.CreateDirectory(design);
        File.WriteAllText(Path.Combine(design, "accessibility.md"), "# Accessibility");
        File.WriteAllText(Path.Combine(design, "README.md"), "# Design");
        File.WriteAllText(Path.Combine(design, "typography-and-layout.md"), "# Typography");
        WriteReadingOrder(design, "{ \"version\": 1, \"directories\": ");

        var tree = await LoadAsync(repo, "design");

        Assert.Equal(
            ["README.md", "accessibility.md", "typography-and-layout.md"],
            tree.Children.Select(node => node.Path));
    }

    [Fact]
    public async Task An_unknown_version_keeps_the_alphabetical_rail()
    {
        var repo = TempDir();
        var design = Path.Combine(repo, ".design");
        Directory.CreateDirectory(design);
        File.WriteAllText(Path.Combine(design, "accessibility.md"), "# Accessibility");
        File.WriteAllText(Path.Combine(design, "README.md"), "# Design");
        File.WriteAllText(Path.Combine(design, "typography-and-layout.md"), "# Typography");
        WriteReadingOrder(design, """
            {
              "version": 2,
              "scope": ".design",
              "directories": { ".design": { "root": "README.md", "order": ["typography-and-layout.md"] } }
            }
            """);

        var tree = await LoadAsync(repo, "design");

        Assert.Equal(
            ["README.md", "accessibility.md", "typography-and-layout.md"],
            tree.Children.Select(node => node.Path));
    }

    /// <summary>
    /// The two halves of a declaration that does not match the disk: a chapter
    /// nobody declared still has a row, after the declared ones and in the order
    /// it used to sort in, and a declared chapter that is not there is simply not
    /// drawn.
    /// </summary>
    [Fact]
    public async Task Undeclared_entries_follow_the_declared_ones_alphabetically()
    {
        var repo = TempDir();
        var design = Path.Combine(repo, ".design");
        Directory.CreateDirectory(design);
        File.WriteAllText(Path.Combine(design, "README.md"), "# Design");
        File.WriteAllText(Path.Combine(design, "design-principles.md"), "# Principles");
        File.WriteAllText(Path.Combine(design, "color-scheme.md"), "# Colour");
        File.WriteAllText(Path.Combine(design, "zebra-crossings.md"), "# Zebras");
        File.WriteAllText(Path.Combine(design, "accessibility.md"), "# Accessibility");

        WriteReadingOrder(design, """
            {
              "version": 1,
              "scope": ".design",
              "directories": {
                ".design": {
                  "root": "README.md",
                  "order": ["design-principles.md", "color-scheme.md", "content-editing.md"]
                }
              }
            }
            """);

        var tree = await LoadAsync(repo, "design");

        Assert.Equal(
            ["README.md", "design-principles.md", "color-scheme.md", "accessibility.md", "zebra-crossings.md"],
            tree.Children.Select(node => node.Path));
        Assert.DoesNotContain(tree.Children, node => node.Path == "content-editing.md");
    }

    /// <summary>
    /// The level the current reader cannot reach at all: a bounded context's own
    /// documents are declared under the nested <c>.domain/&lt;context&gt;</c> key,
    /// and they are the sequence the domain panel beside the rail already shows.
    /// </summary>
    [Fact]
    public async Task Orders_a_domain_context_by_its_nested_declaration()
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

        WriteDomainReadingOrder(domain);

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

        WriteDomainReadingOrder(domain);
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
    /// six rows called "Inbox" and a design README called "Design Knowledge
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

        WriteDomainReadingOrder(domain);
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
        File.WriteAllText(Path.Combine(design, "README.md"), "# Design Knowledge (`.design`)");
        File.WriteAllText(Path.Combine(design, "design-principles.md"), "# Design Principles");

        WriteReadingOrder(design, """
            {
              "version": 1,
              "scope": ".design",
              "directories": {
                ".design": { "root": "README.md", "order": ["design-principles.md"] }
              }
            }
            """);

        WriteDatabase(repo, ".design",
        [
            new OutlineRow("file", "README.md", ".design/README.md", "Design Knowledge (.design)", IsRoot: true),
            new OutlineRow("file", "design-principles.md", ".design/design-principles.md", "Design Principles", IsRoot: false)
        ]);

        var tree = await LoadAsync(repo, "design");

        Assert.Equal("README", tree.Children.Single(node => node.Path == "README.md").Label);
    }

    /// <summary>
    /// A fresh clone, and this worktree: no <c>_meta/</c> at all. Ordering still
    /// comes out of the committed declaration and every label is exactly the one
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

        WriteDomainReadingOrder(domain);

        var tree = await LoadAsync(repo, "domain");

        Assert.False(Directory.Exists(Path.Combine(repo, "_meta")));
        Assert.Equal(["context-map.md", "dev-pc-management", "monitoring"], tree.Children.Select(node => node.Path));
        Assert.Equal(["Context Map", "Dev Pc Management", "Monitoring"], tree.Children.Select(node => node.Label));
        Assert.DoesNotContain(Descendants(tree), node => node.Kind == KnowledgeMenuNodeKind.Message);
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

        WriteDomainReadingOrder(domain);

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
            Assert.DoesNotContain(Descendants(tree), node => node.Kind == KnowledgeMenuNodeKind.Message);
        }
        finally
        {
            foreach (var handle in locks) handle.Dispose();
        }
    }

    /// <summary>
    /// A chapter keeps its filename label even when its generated title is
    /// perfectly distinguishing, because an H1 is written to open a chapter and a
    /// rail row is not the place for a sentence. `.arc42/adr` is the case that
    /// makes it matter: it declares an order, so every guard above this one lets
    /// it through, and its titles run from 57 to 104 characters — "ADR 0008:
    /// Knowledge reads from a cached branch snapshot when there is no clone; only
    /// a clone is editable" where the rail draws "0008 Knowledge Reads From A
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

        WriteDomainReadingOrder(domain);
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

        WriteDomainReadingOrder(domain);
        WriteDatabase(repo, ".domain",
        [
            new OutlineRow("directory", "dev-pc-management", ".domain/dev-pc-management", "Dev PC Management", IsRoot: false),
            new OutlineRow("file", "domain.md", ".domain/dev-pc-management/domain.md", "Dev PC Management", IsRoot: true, Parent: ".domain/dev-pc-management")
        ]);

        // What a branch switch does: the modification time moves, the bytes do not.
        File.SetLastWriteTimeUtc(chapter, File.GetLastWriteTimeUtc(chapter).AddMinutes(5));

        var tree = await LoadAsync(repo, "domain");

        Assert.Equal("Dev Pc Management", tree.Children.Single(node => node.Path == "dev-pc-management").Label);
        Assert.DoesNotContain(Descendants(tree), node => node.Kind == KnowledgeMenuNodeKind.Message);
    }

    private static IEnumerable<KnowledgeMenuNode> Descendants(KnowledgeMenuNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private async Task<KnowledgeMenuNode> LoadAsync(string repo, string areaKey)
    {
        var settings = NewSettingsStore();
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        settings.SetRepositories(repositories);
        settings.SetCloneDirectory("backlog", repo);

        var tree = await new KnowledgeMenu(new KnowledgeFolderSource(settings))
            .LoadAsync([areaKey], cancellationToken: TestContext.Current.CancellationToken);

        var root = Assert.Single(tree.Roots);
        Assert.True(root.Available, root.Message);
        return root;
    }

    /// <summary>The repository's own <c>.domain</c> declaration, contexts and
    /// nested chapter order alike, so what these assert is the file that ships
    /// rather than a convenient version of it.</summary>
    private static void WriteDomainReadingOrder(string domainFolder) =>
        CopyCommittedReadingOrder(".domain", domainFolder);

    private static void CopyCommittedReadingOrder(string area, string folder) =>
        File.Copy(
            RepositoryRootFile(area, "_reading-order.json"),
            Path.Combine(folder, "_reading-order.json"),
            overwrite: true);

    private static string RepositoryRootFile(params string[] relativePath) =>
        Backlog.Tests.RepositoryRoot.File(relativePath);

    private static void WriteReadingOrder(string folder, string json) =>
        File.WriteAllText(Path.Combine(folder, "_reading-order.json"), json);

    /// <summary>One outline row of the generated database: what the writer
    /// resolved out of the authored order, with the title it read from the
    /// Markdown.</summary>
    private sealed record OutlineRow(string Type, string Name, string Path, string Title, bool IsRoot, string? Parent = null);

    /// <summary>
    /// A generated <c>_meta/knowledge.db</c> at the repository root describing
    /// <paramref name="scope"/>, built from the writer's own DDL in
    /// <c>tools/knowledge/knowledge-schema.mjs</c> — nothing on the C# side
    /// restates that schema, fixtures included.
    /// </summary>
    private static void WriteDatabase(string repositoryRoot, string scope, IReadOnlyList<OutlineRow> rows)
    {
        var databasePath = Path.Combine(repositoryRoot, "_meta", KnowledgeDatabaseLocation.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        connection.Open();
        Execute(connection, WriterSchema());
        Execute(connection, $"INSERT INTO meta (key, value) VALUES ('schemaVersion', '{KnowledgeDatabaseSchema.Version}')");
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

    private static string WriterSchema()
    {
        var source = File.ReadAllText(RepositoryRootFile("tools", "knowledge", "knowledge-schema.mjs"));
        var match = Regex.Match(source, @"export const KNOWLEDGE_SCHEMA = `(?<value>[^`]*)`", RegexOptions.Singleline);

        Assert.True(match.Success, "tools/knowledge/knowledge-schema.mjs no longer exports KNOWLEDGE_SCHEMA.");
        return match.Groups["value"].Value;
    }

    private GitHubSettingsStore NewSettingsStore() => new(Path.Combine(TempDir(), "github.json"));

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "knowledge-menu-order-tests", Guid.NewGuid().ToString("n"));
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
