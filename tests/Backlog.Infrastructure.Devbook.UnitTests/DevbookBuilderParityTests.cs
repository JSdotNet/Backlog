using System.Diagnostics;
using System.Globalization;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The app's builder against the Node writer, table by table, on this
/// repository's own <c>.devbook/</c>.
///
/// <para>Local ADR 0015 made the desktop app the writer of the database it reads
/// and kept <c>tools/devbook/build-database.mjs</c> as the reference: it imports
/// the devbook plugin's own generator, so when a plugin release changes how a
/// chapter parses, that script's output changes and this test fails. That is the
/// only thing keeping the two parsers in step, so it compares every table either
/// writer fills except <c>meta</c> (timestamps, and a fingerprint only the app
/// records) and <c>problem</c> (the Node writer carries the generator's lint, the
/// app deliberately does not) — every column, every row, and the value types, so
/// an integer written as a real is a difference too.</para>
///
/// <para>It needs Node 22.5 or later on the path, for <c>node:sqlite</c>; the .NET
/// job in CI installs it. Without Node this fails rather than skipping, because a
/// skipped comparison is a green build that compared nothing.</para>
/// </summary>
public class DevbookBuilderParityTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "backlog-devbook-parity", Guid.NewGuid().ToString("N"));

    public DevbookBuilderParityTests() => Directory.CreateDirectory(_scratch);

    [Fact]
    public void The_app_builds_the_same_database_as_the_node_writer()
    {
        var repository = RepositoryRoot.Root.FullName;
        var fromNode = Path.Combine(_scratch, "node.db");
        var fromApp = Path.Combine(_scratch, "app.db");

        RunNodeWriter(repository, fromNode);
        Assert.True(DevbookDatabaseBuilder.Build(repository, fromApp, TestContext.Current.CancellationToken));

        var differences = CompareDatabases(fromNode, fromApp);

        Assert.True(
            differences.Count == 0,
            $"The C# builder and {GeneratorName} disagree ({differences.Count} difference(s), first 20 shown):\n"
            + string.Join('\n', differences.Take(20)));

        // And the comparison compared something.
        using var app = Open(fromApp);
        Assert.True(Rows(app, "chapter", "id").Count > 500, "this repository's corpus came out nearly empty");
    }

    /// <summary>
    /// The same comparison on a small corpus built to hold what this repository's
    /// own does not: every reading-order rule of the convention — a missing root,
    /// <c>index: root</c> and <c>index: exclude</c>, a <c>number</c> field, split
    /// files and an invariants subpage, a stray <c>_reading-order.json</c>, a
    /// repeated slug and a heading inside a fence — and devbook contract 38's
    /// metadata: a <c>tech/</c> and an <c>ai/</c> <c>rating</c>, which the
    /// <c>status</c> columns hold, no status anywhere else, no <c>sync</c>, and no
    /// <c>annotation</c> fence, so every open-note count is zero; and journeys
    /// whose parts a requirement's <c>Proved by:</c> cases point at, which derive
    /// its <c>e2e</c> <c>tests</c> entries where a spec implements them. The Node writer
    /// builds it with this repository's installed generator.
    /// </summary>
    [Fact]
    public void The_app_matches_the_node_writer_on_the_convention_s_edge_cases()
    {
        var fixture = Path.Combine(_scratch, "fixture");
        foreach (var (path, text) in EdgeCases)
        {
            var file = Path.Combine(fixture, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }

        var fromNode = Path.Combine(_scratch, "fixture-node.db");
        var fromApp = Path.Combine(_scratch, "fixture-app.db");

        RunNodeWriter(fixture, fromNode, generator: Path.Combine(RepositoryRoot.Root.FullName, ".devbook", "_tools", "devbook-meta"));
        Assert.True(DevbookDatabaseBuilder.Build(fixture, fromApp, TestContext.Current.CancellationToken));

        var differences = CompareDatabases(fromNode, fromApp);

        Assert.True(
            differences.Count == 0,
            $"The C# builder and {GeneratorName} disagree on the fixture ({differences.Count} difference(s), first 20 shown):\n"
            + string.Join('\n', differences.Take(20)));

        // And the fixture exercised what it is for.
        using var app = Open(fromApp);
        var chapters = Rows(app, "chapter", "id");
        Assert.All(chapters, row => Assert.Contains("open_annotations=INTEGER:0", row, StringComparison.Ordinal));
        Assert.Contains(Rows(app, "outline_entry", "id"), row => row.Contains("name=TEXT:domain.invariants.md", StringComparison.Ordinal));
        Assert.All(chapters, row => Assert.Contains("sync=NULL", row, StringComparison.Ordinal));
        Assert.All(Rows(app, "node", "id"), row => Assert.Contains("sync=NULL", row, StringComparison.Ordinal));

        // A rating is the one status a row carries, in `tech/` and `ai/` alone.
        Assert.Contains(chapters, row => row.Contains("path=TEXT:.devbook/tech/shared.md", StringComparison.Ordinal) && row.Contains("status=TEXT:adopted", StringComparison.Ordinal));
        Assert.Contains(Rows(app, "node", "id"), row => row.StartsWith("id=TEXT:.devbook/ai/01-verify.md#review |", StringComparison.Ordinal) && row.Contains("status=TEXT:trial", StringComparison.Ordinal));
        Assert.Contains(Rows(app, "outline_entry", "id"), row => row.Contains("path=TEXT:.devbook/tech/shared.md", StringComparison.Ordinal) && row.Contains("status=TEXT:adopted", StringComparison.Ordinal));
        Assert.All(
            Rows(app, "outline_entry WHERE path NOT LIKE '.devbook/tech/%' AND path NOT LIKE '.devbook/ai/%'", "id"),
            row => Assert.Contains("status=NULL", row, StringComparison.Ordinal));

        // The C4 model: the included person, the unnamed instances numbered apart
        // under their unnamed node, the repeated relationship numbered, the
        // duplicate view key read once, the embed under its section and the one in
        // another case, and both fields — the entry naming nothing drawing nothing.
        var nodes = Rows(app, "node", "id");
        Assert.Contains(nodes, row => row.StartsWith("id=TEXT:c4:shopper |", StringComparison.Ordinal) && row.Contains("path=TEXT:.devbook/arc42/c4/people.dsl", StringComparison.Ordinal));
        Assert.Contains(nodes, row => row.StartsWith("id=TEXT:c4:live.~cloud.~shop-web-instance-2 |", StringComparison.Ordinal) && row.Contains("label=TEXT:Web", StringComparison.Ordinal));
        Assert.Single(nodes, row => row.StartsWith("id=TEXT:c4-view:context |", StringComparison.Ordinal));
        var edges = Rows(app, "edge", "id").ToList();
        Assert.Contains(edges, row => row.StartsWith("id=TEXT:relationship:c4:shop.web->c4:shop.db#2 |", StringComparison.Ordinal));
        Assert.Contains(edges, row => row.StartsWith("id=TEXT:embeds:.devbook/arc42/05-building-blocks.md#containers->c4-view:containers |", StringComparison.Ordinal));
        Assert.Contains(edges, row => row.StartsWith("id=TEXT:embeds:.devbook/arc42/05-building-blocks.md#containers->c4-view:context |", StringComparison.Ordinal));
        Assert.DoesNotContain(edges, row => row.Contains("c4-view:checkout", StringComparison.Ordinal) && row.Contains("type=TEXT:embeds", StringComparison.Ordinal));
        Assert.Contains(edges, row => row.StartsWith("id=TEXT:c4:.devbook/arc42/05-building-blocks.md->c4:shop.web |", StringComparison.Ordinal));
        Assert.Contains(edges, row => row.StartsWith("id=TEXT:c4-touches:.devbook/domain/zeta/c4.md->c4:pay |", StringComparison.Ordinal));
        Assert.Equal(2, edges.Count(row => row.Contains("type=TEXT:c4 |", StringComparison.Ordinal)));

        // An `ai/` chapter's `feeds` (contract 35): the labelled entries draw an
        // edge to the reference before the colon, one outside the folders an
        // external node too; an entry with no label, an empty label (a BOM is
        // JavaScript whitespace), whitespace in the reference, or no reference
        // draws nothing. U+0085 is no JavaScript whitespace, so that one draws.
        Assert.Contains(edges, row => row.StartsWith("id=TEXT:feeds:.devbook/ai/01-verify.md#review->.devbook/ai/01-verify.md#merge |", StringComparison.Ordinal));
        Assert.Contains(edges, row => row.StartsWith("id=TEXT:feeds:.devbook/ai/01-verify.md#review->docs/runbook.md#ship |", StringComparison.Ordinal));
        Assert.Contains(edges, row => row.StartsWith("id=TEXT:feeds:.devbook/ai/01-verify.md#review->docs/nel.md#a\u0085b |", StringComparison.Ordinal));
        Assert.Equal(3, edges.Count(row => row.Contains("type=TEXT:feeds", StringComparison.Ordinal)));
        Assert.Contains(nodes, row => row.StartsWith("id=TEXT:docs/runbook.md#ship |", StringComparison.Ordinal));

        var demos = Rows(app, "demo", "path");
        Assert.Equal(4, demos.Count);
        Assert.Contains(demos, row => row.Contains("page_path=TEXT:.devbook/domain/zeta/context.md", StringComparison.Ordinal));
        Assert.Contains(demos, row => row.Contains("page_path=TEXT:.devbook/domain/alpha/features.checkout.md", StringComparison.Ordinal));

        var links = Rows(app, "demo_link", "chapter_path, line, ordinal");
        Assert.Equal(4, links.Count);
        Assert.Contains(links, row => row.Contains("address=TEXT:walkthrough/pay-declined/2?flags=retry,wallet", StringComparison.Ordinal));
        Assert.Contains(links, row => row.Contains("demo_path=TEXT:.devbook/domain/alpha/gone.demo.html", StringComparison.Ordinal));

        // A requirement's `tests`: the authored entry, then one per part with a
        // spec, once however many cases point at it; the part with none derives
        // nothing, and an entry already authored is not repeated. A pointer into
        // another context resolves through `../`, a CRLF spec's header counts,
        // and a requirement with none authored gets the derived ones alone.
        Assert.Equal(
            [
                "integration:dotnet:Alpha.Tests.Pay",
                "e2e:playwright:tests/e2e/checkout.spec.ts#Pay by card",
                "e2e:playwright:tests/e2e/checkout.spec.ts#See the receipt",
                "e2e:playwright:tests/e2e/delivery.spec.ts#Track the parcel"
            ],
            Tests(app, ".devbook/domain/alpha/requirements.md#pay-for-an-order"));
        Assert.Equal(
            ["e2e:playwright:tests/e2e/checkout.spec.ts#Pay by card", "unit:dotnet:Alpha.Tests.Refund"],
            Tests(app, ".devbook/domain/alpha/requirements.md#refund-an-order"));
        Assert.Equal(
            ["e2e:playwright:tests/e2e/delivery.spec.ts#Track the parcel"],
            Tests(app, ".devbook/domain/beta/requirements.md#track-a-parcel"));

        // A header past the tenth line, and specs in a dot-folder or under
        // node_modules, name no journey.
        var tests = Rows(app, "node_attribute WHERE name = 'tests'", "rowid");
        Assert.DoesNotContain(tests, row => row.Contains("returns.spec.ts", StringComparison.Ordinal));
        Assert.DoesNotContain(tests, row => row.Contains("refund.spec.ts", StringComparison.Ordinal));
        Assert.DoesNotContain(tests, row => row.Contains("#Send it back", StringComparison.Ordinal) || row.Contains("#Ask for a refund", StringComparison.Ordinal));

        Assert.DoesNotContain(Rows(app, "outline_entry", "id"), row => row.Contains(".html", StringComparison.Ordinal));
        Assert.Single(Rows(app, "problem", "rowid"), row => row.Contains("orphan.demo.html", StringComparison.Ordinal));

        // And the reader answers a page's demos: by name first, then by field.
        using var database = DevbookDatabase.TryOpen(fromApp);
        Assert.NotNull(database);
        Assert.Equal(
            [
                new DevbookPageDemoRow(".devbook/domain/alpha/features.checkout.demo.html", null, null, true),
                new DevbookPageDemoRow(".devbook/domain/alpha/features.checkout.demo.html", "alpha", "pay", true),
                new DevbookPageDemoRow(".devbook/domain/alpha/flows.demo.html", "alpha", "walkthrough/pay-declined/2?flags=retry,wallet", true),
                new DevbookPageDemoRow(".devbook/domain/alpha/gone.demo.html", "alpha", null, false),
                new DevbookPageDemoRow(".devbook/domain/alpha/flows.demo.html", "declined", null, true)
            ],
            database.Demos(".devbook/domain/alpha/features.checkout.md"));
        Assert.Equal([new DevbookPageDemoRow(".devbook/domain/zeta/demo.html", null, null, true)], database.Demos(".devbook/domain/zeta/context.md"));
    }

    /// <summary>
    /// The same comparison on a Brain (local ADR 0026), read under its own
    /// convention: the areas its <c>CONVENTIONS.md</c> declares — one called
    /// <c>design</c>, which must get none of the devbook design folder's rules —
    /// in the order its table numbers them, then the folders it does not declare
    /// that hold a note — mixed case, one only deeply nested — by name, but not an
    /// ignored folder (one also declared), a dot-folder, a folder whose only note
    /// sits in a dot sub-folder or a folder with no note at all; the
    /// <c>para</c>, <c>repos</c> and <c>tasks</c> fields, <c>repo:</c> links in
    /// <c>related</c> and in prose (one inside a fence, one twice), a reference
    /// into an area that resolves to nothing, one outside every area, a repeated
    /// slug and the reading-order rules. The Node writer reads
    /// it with <c>--brain</c>, through this repository's installed generator.
    /// <para>
    /// The fixture is plain files on purpose. Over a Brain in a synced folder —
    /// OneDrive's placeholders, every one a reparse point — the two writers differ
    /// and are meant to: the C# builder walks placeholders and indexes a cloud-only
    /// note by its name (<c>DevbookBuildFiles.Entries</c> with placeholders), while
    /// <c>tools/devbook/brain.mjs</c> and the generator it imports skip every reparse
    /// point, as Node's <c>Dirent</c> reports them, and index nothing there. The
    /// generator is <c>devbook:update</c>'s to change, and Node cannot tell a
    /// cloud-only file without opening it, which downloads it. <c>.devbook/tech/tooling.md</c>
    /// states the difference.
    /// </para>
    /// </summary>
    [Fact]
    public void The_app_matches_the_node_writer_on_a_brain()
    {
        var brain = Path.Combine(_scratch, "brain");
        foreach (var (path, text) in BrainCases)
        {
            var file = Path.Combine(brain, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }

        var fromNode = Path.Combine(_scratch, "brain-node.db");
        var fromApp = Path.Combine(_scratch, "brain-app.db");

        RunNodeWriter(brain, fromNode, generator: Path.Combine(RepositoryRoot.Root.FullName, ".devbook", "_tools", "devbook-meta"), brain: true);
        Assert.True(DevbookDatabaseBuilder.Build(brain, fromApp, BrainCorpusProfile.Instance, TestContext.Current.CancellationToken));

        var differences = CompareDatabases(fromNode, fromApp);

        Assert.True(
            differences.Count == 0,
            $"The C# builder and {GeneratorName} --brain disagree on the Brain fixture ({differences.Count} difference(s), first 20 shown):\n"
            + string.Join('\n', differences.Take(20)));

        // And the fixture exercised what it is for.
        using var app = Open(fromApp);
        Assert.Equal(
            ["name=TEXT:_inbox", "name=TEXT:work", "name=TEXT:design", "name=TEXT:projects", "name=TEXT:alpha", "name=TEXT:kitap", "name=TEXT:loose", "name=TEXT:Meetings", "name=TEXT:_drafts", "name=TEXT:ılık"],
            Rows(app, "outline_entry WHERE scope = '.' AND parent_id IS NULL", "ordinal").Select(row => row.Split(" | ")[5]));
        Assert.Contains(Rows(app, "node", "id"), row => row.Contains("path=TEXT:loose/not-an-area.md", StringComparison.Ordinal));
        Assert.Contains(Rows(app, "node", "id"), row => row.Contains("path=TEXT:Meetings/2026/10/standup.md", StringComparison.Ordinal));
        foreach (var hidden in new[] { "_templates/", "attic/", ".obsidian/", "dotted/", "empty/" })
        {
            Assert.DoesNotContain(Rows(app, "node", "id"), row => row.Contains("path=TEXT:" + hidden, StringComparison.Ordinal));
        }
        Assert.Contains(Rows(app, "node_attribute", "node_id, name, value"), row => row.Contains("name=TEXT:para | value=TEXT:work", StringComparison.Ordinal));
        Assert.Contains(Rows(app, "node_attribute", "node_id, name, value"), row => row.Contains("name=TEXT:tasks", StringComparison.Ordinal));
        Assert.Contains(Rows(app, "edge", "id"), row => row.Contains("type=TEXT:repo-link", StringComparison.Ordinal));
        Assert.Contains(Rows(app, "edge", "id"), row => row.Contains("target=TEXT:repo:backlog/.devbook/arc42/05-building-block-view.md#devbook", StringComparison.Ordinal));
        Assert.DoesNotContain(Rows(app, "node", "id"), row => row.Contains("id=TEXT:repo:", StringComparison.Ordinal));
        Assert.Contains(Rows(app, "node", "id"), row => row.Contains("type=TEXT:external", StringComparison.Ordinal));
        Assert.All(Rows(app, "chapter", "id"), row => Assert.Contains("open_annotations=INTEGER:0", row, StringComparison.Ordinal));
        Assert.All(Rows(app, "node", "id"), row => Assert.Contains("status=NULL", row, StringComparison.Ordinal));
    }

    private static readonly (string Path, string Text)[] BrainCases =
    [
        ("README.md", "# My Brain\n\n```meta\nindex: root\n```\n"),
        ("CONVENTIONS.md", """
            # Conventions

            ```markdown
            ## Areas

            - `fenced-not-an-area`
            ```

            ## Areas

            | Area | Folder | Order |
            |------|--------|-------|
            | Projects | projects | 4 |
            | Inbox | `_inbox` | 1 |
            | Design | design | 3 |
            | Work | work | 2 |
            | Missing | missing | 5 |
            | Templates | `_templates` | 6 |

            ## Ignore

            - `_TEMPLATES`

            | Folder | Why |
            |---|---|
            | attic | Old things. |
            """),
        ("_templates/meeting.md", "# Meeting template\n"),
        ("attic/old.md", "# Old\n"),
        (".obsidian/workspace.md", "# Workspace\n"),
        ("dotted/.trash/gone.md", "# Gone\n"),
        ("empty/attachment.txt", "not a note"),
        ("Meetings/2026/10/standup.md", "# Standup\n\nA note only deep down.\n"),
        ("alpha/first.md", "# Alpha\n"),
        ("_drafts/later.md", "# Later\n"),
        ("kitap/okuma.md", "# Okuma\n"),
        ("ılık/su.md", "# Su\n"),
        ("_inbox/2026-10-13-idea.md", "# An idea\n\nCaptured on the phone.\n"),
        ("work/README.md", "# Work\n\n```meta\nindex: root\n```\n"),
        ("work/standup.md", "# Standup\n\n```meta\npara: journal\n```\n\nDaily notes.\n"),
        ("work/02-second.md", "# Second\n"),
        ("work/01-first.md", "# First\n"),
        ("work/hidden.md", "# Hidden\n\n```meta\nindex: exclude\n```\n"),
        ("work/clients/acme.md", "# Acme\n\n```meta\nrepos: acme-web\n```\n"),
        ("design/README.md", "# Design notes\n"),
        ("design/palette.md", "# Palette\n\n```meta\ntype: guideline\n```\n\n## Teal\n\nA calm accent.\n"),
        ("projects/brain.md", """
            # Brain

            ```meta
            repos: [backlog, devbook]
            tasks: [c5a14485, bb8ce071]
            related: [repo:backlog/.devbook/arc42/05-building-block-view.md#devbook, work/standup.md#standup, work/missing.md, notes/elsewhere.md]
            depends-on: [repo:devbook/.devbook/arc42/adr/0001-first.md]
            ```

            Before any chapter: [the ADR](repo:backlog/.devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md).

            ## Scope

            ```meta
            para: work
            related: [projects/brain.md]
            ```

            The scope chip: [ADR 0027](repo:backlog/.devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md "the scope")
            and again [the same](repo:backlog/.devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md).

            ### Detail

            Under a chapter with no block: [devbook](<repo:devbook/.devbook/domain/context-map.md>).

            ```markdown
            [fenced](repo:fenced/nothing.md)
            ```

            ## Scope

            ```meta
            tasks: t-dup
            ```

            A repeated slug: [dup](repo:dup/x.md).
            """),
        ("loose/not-an-area.md", "# Loose\n\n[out](repo:loose/x.md)\n")
    ];

    /// <summary>Whitespace to .NET and not to JavaScript, and the reverse: the
    /// two runtimes' trim sets differ on exactly these.</summary>
    private const string NextLine = "\u0085";
    private const string ByteOrderMark = "﻿";

    private static readonly (string Path, string Text)[] EdgeCases =
    [
        (".devbook/_reading-order.json", """{ "version": 1, "directories": { ".": { "order": [".devbook/ai"] } } }"""),
        (".devbook/arc42/_reading-order.json", """{ "version": 1, "directories": { ".devbook/arc42": { "root": "10-quality.md", "order": [] } } }"""),
        (".devbook/arc42/10-quality.md", "# 10. Quality\n"),
        (".devbook/arc42/02-constraints.md", "# 02. Constraints\n"),
        (".devbook/arc42/01-introduction.md", "# 01. Introduction\n"),
        (".devbook/arc42/adr/README.md", "# Architecture Decision Records\n"),
        (".devbook/arc42/adr/0002-second.md", "# ADR 0002: Second\n"),
        (".devbook/arc42/adr/0001-first.md", "# ADR 0001: First\n"),
        (".devbook/arc42/adr/renumbered.md", "# Renumbered\n\n```meta\nnumber: 3\n```\n"),
        (".devbook/arc42/tdr/README.md", "# Technical Debt Records\n\n```meta\nindex: root\n```\n"),
        (".devbook/arc42/tdr/0001-debt.md", "# TDR 0001: Debt\n"),
        (".devbook/arc42/tdr/0002-hidden.md", "# TDR 0002: Hidden\n\n```meta\nindex: exclude\n```\n"),
        (".devbook/domain/context-map.md", "# Context Map\n"),
        (".devbook/domain/zeta/context.md", "# Zeta\n\n```meta\ntests: unit:dotnet:Zeta.Tests.Context\n```\n"),
        (".devbook/domain/zeta/model.md", "# Zeta\n\n```meta\ntype: aggregate\ntests: [unit:dotnet:Zeta.Tests.First, unit:dotnet:Zeta.Tests.Second]\n```\n"),
        (".devbook/domain/zeta/domain.md", """
            Above the first heading.

            # Zeta

            ## Capture

            Captured first.

            ## Capture

            The same slug again.

            ## Fenced

            ```mermaid
            flowchart TB
            # Not a heading
            ```

            ````markdown
            ```text
            # Not a heading either
            ```
            ````
            """),
        (".devbook/domain/zeta/domain.invariants.md", "# Zeta\n"),
        (".devbook/domain/zeta/domain.order.md", "# Zeta\n"),
        (".devbook/domain/zeta/notes.md", "# Zeta\n"),
        // Demos: the context's own beside context.md, a split page's beside it,
        // one only a field names, one nothing pairs with, and a field that names
        // a missing demo and an address the metadata parse splits on its comma.
        (".devbook/domain/zeta/demo.html", "<!doctype html><title>Zeta</title>"),
        (".devbook/domain/zeta/orphan.demo.html", "<!doctype html><title>Orphan</title>"),
        (".devbook/domain/alpha/features.checkout.demo.html", "<!doctype html><title>Checkout</title>"),
        (".devbook/domain/alpha/flows.demo.html", "<!doctype html><title>Flows</title>"),
        (".devbook/domain/alpha/features.checkout.md", """
            # Alpha

            ```meta
            demo: [.devbook/domain/alpha/features.checkout.demo.html#pay, "./.devbook/domain/alpha/flows.demo.html#walkthrough/pay-declined/2?flags=retry,wallet", .devbook/domain/alpha/gone.demo.html]
            ```

            ## Declined

            ```meta
            demo: .devbook/domain/alpha/flows.demo.html
            ```
            """),
        (".devbook/domain/alpha/actors.md", "# Alpha\n"),
        // Journeys, and the `tests` entries their `Proved by:` pointers derive:
        // one a spec implements — named by two specs, the first in path order
        // winning — one only specs under node_modules and a dot-folder name,
        // which the walk skips, and one a spec names on its eleventh line, past
        // the ten read; neither of those last two derives anything.
        (".devbook/domain/alpha/scenarios.md", """
            # Alpha scenarios

            ```meta
            type: scenarios
            ```

            ## Checkout

            ```meta
            type: scenario
            ```

            ### Pay by card

            - **Given** a basket with one item
            - **When** the shopper pays by card
            - **Then** the order is placed

            ### See the receipt

            - **Then** the receipt shows the order

            ## Refund

            ```meta
            type: scenario
            ```

            ### Ask for a refund

            - **Given** a placed order
            - **When** the shopper asks for a refund
            - **Then** the refund is pending

            ## Returns

            ```meta
            type: scenario
            ```

            ### Send it back

            - **When** the shopper sends the order back
            - **Then** the return is logged
            """),
        // A context of its own, whose journey a requirement here and one in alpha
        // prove — one each, so the journey sits where most of them are — and
        // whose spec is written with CRLF line ends.
        (".devbook/domain/beta/scenarios.md", """
            # Beta scenarios

            ```meta
            type: scenarios
            ```

            ## Delivery

            ```meta
            type: scenario
            ```

            ### Track the parcel

            - **Given** a shipped order
            - **Then** the parcel's position shows
            """),
        (".devbook/domain/beta/requirements.md", """
            # Beta requirements

            ```meta
            type: requirements
            ```

            ## Shipping

            ### Track a parcel

            ```meta
            type: requirement
            ```

            #### Scenario: Tracking

            Proved by: scenarios.md#track-the-parcel
            """),
        // Cases pointing at the journeys: at one part twice, by a path from the
        // requirement's folder and by a repository path, at another from inside a
        // fence, which is no pointer, at beta's through `../`, and at the part
        // whose spec names it too late — and a requirement whose authored entries
        // already hold what its case derives.
        (".devbook/domain/alpha/requirements.md", """
            # Alpha requirements

            ```meta
            type: requirements
            ```

            ## Payments

            ### Pay for an order

            ```meta
            type: requirement
            tests: integration:dotnet:Alpha.Tests.Pay
            ```

            #### Scenario: Card payment

            Proved by: scenarios.md#pay-by-card

            #### Scenario: Card payment from the root

            Proved by: .devbook/domain/alpha/scenarios.md#pay-by-card

            #### Scenario: The receipt

            Proved by: scenarios.md#see-the-receipt

            ```text
            Proved by: scenarios.md#ask-for-a-refund
            ```

            #### Scenario: Delivered

            Proved by: ../beta/scenarios.md#track-the-parcel

            #### Scenario: Sent back

            Proved by: scenarios.md#send-it-back

            ### Refund an order

            ```meta
            type: requirement
            tests: [e2e:playwright:tests/e2e/checkout.spec.ts#Pay by card, unit:dotnet:Alpha.Tests.Refund]
            ```

            #### Scenario: Refund request

            Proved by: scenarios.md#ask-for-a-refund

            #### Scenario: Paid first

            Proved by: scenarios.md#pay-by-card
            """),
        ("tests/e2e/checkout.spec.ts", "// scenario: .devbook/domain/alpha/scenarios.md#checkout\nimport { test } from \"@playwright/test\";\n"),
        ("tests/e2e/later.spec.ts", "import { test } from \"@playwright/test\";\n\n// scenario: .devbook/domain/alpha/scenarios.md#checkout\n"),
        ("node_modules/refund/refund.spec.ts", "// scenario: .devbook/domain/alpha/scenarios.md#refund\n"),
        (".hidden/refund.spec.ts", "// scenario: .devbook/domain/alpha/scenarios.md#refund\n"),
        ("tests/e2e/returns.spec.ts", string.Concat(Enumerable.Repeat("// setup\n", 10)) + "// scenario: .devbook/domain/alpha/scenarios.md#returns\n"),
        ("tests/e2e/delivery.spec.ts", "import { test } from \"@playwright/test\";\r\n// scenario: .devbook/domain/beta/scenarios.md#delivery\r\n\r\ntest(\"delivery\", () => {});\r\n"),
        (".devbook/tech/tooling.md", "# Tooling\n"),
        (".devbook/tech/cloud.md", "# Cloud\n"),
        (".devbook/tech/technology-graph.md", "# Technology Graph\n"),
        (".devbook/tech/shared.md", "# Shared\n\n```meta\nrating: adopted\n```\n"),
        (".devbook/design/README.md", "# Design\n"),
        (".devbook/design/content-editing.md", "# Content Editing\n"),
        (".devbook/design/color-scheme.md", "# Color Scheme\n"),
        (".devbook/design/design-principles.md", "# Design Principles\n"),
        // Line terminators to JavaScript's `.` and not to .NET's: a heading
        // holding U+2028 or U+2029 starts no chapter, and neither does any
        // heading in a file whose lines end in a lone CR.
        (".devbook/design/line-separators.md", "# Line Separators\n\n## Kept\n\n#### Scenario: A\u2028B\n\n## Para\u2029graph\n"),
        (".devbook/design/lone-cr.md", "# Lone CR\r\r## Not a chapter\r"),
        (".devbook/ai/concepts.md", "# Concepts\n"),
        (".devbook/ai/02-code.md", "# Code\n"),
        (".devbook/ai/adoption-map.md", "# Adoption Map\n"),
        (".devbook/ai/01-plan.md", "# Plan\n"),
        // The C4 model (contract 33): a workspace with an element it includes
        // from c4/, a group, two relationships between one pair, an unnamed
        // deployment node holding two unnamed instances of one container, and a
        // view key used twice; a chapter embedding views under a section, in a
        // code span, a fence and a comment, one by a key in another case, and
        // `c4` and `c4-touches` naming elements, one of them nothing.
        (".devbook/arc42/workspace.dsl", """
            workspace "Fixture" "The parity fixture." {
                !identifiers hierarchical
                model {
                    !include c4/people.dsl
                    shop = softwareSystem "Shop" "Sells things." {
                        web = container "Web" "The storefront." "Blazor"
                        db = container "Store" "Orders." "SQLite" "Database"
                        web -> db "Reads"
                        web -> db "Writes"
                    }
                    group "Outside" {
                        pay = softwareSystem "Pay" "Takes payments."
                    }
                    shopper -> shop.web "Buys"
                    shop.web -> pay "Charges"
                    shop.web -> nowhere "Dropped"
                    live = deploymentEnvironment "Live" {
                        deploymentNode "Cloud" "The host." {
                            containerInstance shop.web
                            containerInstance shop.web
                        }
                    }
                }
                views {
                    container shop "containers" {
                        title "Shop containers"
                        include *
                    }
                    systemContext shop "context" "The context"
                    dynamic shop "checkout" {
                        shopper -> shop.web "Buys"
                    }
                    systemLandscape "Context"
                }
            }
            """),
        (".devbook/arc42/c4/people.dsl", "shopper = person \"Shopper\" \"Buys things.\"\n"),
        (".devbook/arc42/05-building-blocks.md", """
            # 05. Building Blocks

            ```meta
            c4: [shop, Shop.Web, nosuch]
            ```

            ## Containers

            ```meta
            related: [.devbook/domain/zeta/c4.md]
            ```

            ![The containers](c4:containers)

            `![not an embed](c4:checkout)`

            ```text
            ![fenced](c4:checkout)
            ```

            <!-- ![hidden](c4:checkout) -->

            ### Structural

            ![Context](c4:CONTEXT)
            """),
        (".devbook/domain/zeta/c4.md", "# Zeta\n\n```meta\nc4-touches: [shopper, pay]\n```\n"),
        (".devbook/ai/01-verify.md", $"""
            # Verify

            ```meta
            rating: adopted
            type: stage
            ```

            ## Review

            ```meta
            rating: trial
            type: agent
            stage: [verify]
            substage: [verify/review]
            layer: [harness]
            evidence: recorded
            feeds: [".devbook/ai/01-verify.md#merge: hands the verdict on", ".devbook/ai/01-verify.md#merge", "docs/runbook.md#ship: pages the owner", "docs/empty.md#label: ", "docs/two words.md#x: spaced", ": no reference", "docs/nel.md#a{NextLine}b: next line", "docs/bom.md#x: {ByteOrderMark}"]
            ```

            ## Merge

            ```meta
            rating: adopted
            type: agent
            stage: [integrate]
            ```
            """)
    ];

    private static List<string> CompareDatabases(string fromNode, string fromApp)
    {
        using var node = Open(fromNode);
        using var app = Open(fromApp);

        var differences = new List<string>();
        foreach (var (table, order) in (IEnumerable<(string, string)>)
            [
                ("node", "id"),
                ("node_attribute", "node_id, name, value"),
                ("edge", "id, type, source, target"),
                ("outline_entry", "id"),
                ("chapter", "id"),
                ("archify_artifact", "chapter_path, fence_hash, ordinal"),
                ("demo", "path"),
                ("demo_link", "chapter_path, line, ordinal"),
                ("chapter_embedding", "content_hash")
            ])
        {
            differences.AddRange(Compare(table, Rows(node, table, order), Rows(app, table, order)));
        }

        differences.AddRange(Compare("chapter_fts MATCH devbook", Fts(node, "devbook"), Fts(app, "devbook")));
        return differences;
    }

    private const string GeneratorName = "tools/devbook/build-database.mjs";

    private static void RunNodeWriter(string repository, string target, string? generator = null, bool brain = false)
    {
        var writer = Path.Combine(RepositoryRoot.Root.FullName, "tools", "devbook", "build-database.mjs");
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(writer);
        if (brain) start.ArgumentList.Add("--brain");
        start.ArgumentList.Add("--root");
        start.ArgumentList.Add(repository);
        start.ArgumentList.Add("--out");
        start.ArgumentList.Add(target);

        if (generator is not null)
        {
            start.ArgumentList.Add("--generator");
            start.ArgumentList.Add(generator);
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("node did not start");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Assert.Fail($"This comparison needs Node 22.5 or later on the PATH to run {GeneratorName}: {exception.Message}");
            return;
        }

        using (process)
        {
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();

            Assert.True(process.WaitForExit(TimeSpan.FromMinutes(2)), $"{GeneratorName} did not finish in two minutes.");
            Assert.True(process.ExitCode == 0, $"{GeneratorName} failed ({process.ExitCode}):\n{error.Result}\n{output.Result}");
        }
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static List<string> Rows(SqliteConnection connection, string table, string order)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {table} ORDER BY {order}";

        var rows = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var cells = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                cells[i] = reader.IsDBNull(i)
                    ? $"{reader.GetName(i)}=NULL"
                    : $"{reader.GetName(i)}={reader.GetDataTypeName(i)}:{Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)}";
            }

            rows.Add(string.Join(" | ", cells));
        }

        return rows;
    }

    /// <summary>A node's <c>tests</c> rows, in the order they were written.</summary>
    private static List<string> Tests(SqliteConnection connection, string nodeId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM node_attribute WHERE node_id = $node AND name = 'tests' ORDER BY rowid";
        command.Parameters.AddWithValue("$node", nodeId);

        var values = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) values.Add(reader.GetString(0));
        return values;
    }

    private static List<string> Fts(SqliteConnection connection, string term)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT rowid FROM chapter_fts WHERE chapter_fts MATCH $term ORDER BY rowid";
        command.Parameters.AddWithValue("$term", term);

        var rows = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) rows.Add(reader.GetInt64(0).ToString(CultureInfo.InvariantCulture));
        return rows;
    }

    private static IEnumerable<string> Compare(string table, List<string> expected, List<string> actual)
    {
        if (expected.Count != actual.Count)
        {
            yield return $"{table}: {expected.Count} row(s) from node, {actual.Count} from the app";
        }

        var missing = expected.Except(actual, StringComparer.Ordinal).Take(5);
        var extra = actual.Except(expected, StringComparer.Ordinal).Take(5);

        foreach (var row in missing) yield return $"{table}: only node wrote   {row}";
        foreach (var row in extra) yield return $"{table}: only the app wrote {row}";

        if (expected.Count == actual.Count && !expected.Except(actual, StringComparer.Ordinal).Any())
        {
            for (var i = 0; i < expected.Count; i++)
            {
                if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
                {
                    yield return $"{table}: same rows, different order from row {i}";
                    break;
                }
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder that outlives the run is not a test failure.
        }
    }
}
