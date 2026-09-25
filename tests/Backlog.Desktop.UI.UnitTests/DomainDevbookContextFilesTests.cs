using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Devbook;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A bounded context as contract 16 of <c>devbook-domain.md</c> writes one, read by
/// <see cref="DomainDevbookStore"/>.
///
/// <para>Four things changed from contract 9 and each is pinned here. The context's
/// root document is <c>context.md</c>, which declares <c>index: root</c>, reads
/// first, and is where the context's name comes from. There are five new files —
/// <c>context.md</c>, <c>actors.md</c>, <c>skills.md</c>, <c>requirements.md</c>,
/// <c>invariants.md</c> — and a split file <c>&lt;file&gt;.&lt;name&gt;.md</c> is the
/// same kind as its base and reads directly after it. A file the convention does
/// not name is an additional page whose type is its filename, which is what
/// <c>naming.md</c> became. And <c>deployment</c> is stated twice, on the map's
/// <c>bounded-context</c> chapter and on <c>context.md</c>, and the two must
/// agree.</para>
///
/// <para>The fixtures are the rule's own templates with the placeholders filled in,
/// so a template that changes shape is a fixture to re-copy rather than a shape
/// somebody here invented.</para>
/// </summary>
public sealed class DomainDevbookContextFilesTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    /// <summary>The <c>context.md</c> template, filled in.</summary>
    private const string ContextFile = """
        # Billing

        ```meta
        status: draft
        index: root
        type: context
        deployment: module
        ```

        What this context is responsible for, in one or two sentences.

        Inside the boundary: invoices and what is owed on them.

        Outside it: payment collection, which the Payments context answers.

        ## Express Invoicing

        ```meta
        status: draft
        type: feature-flag
        key: billing.express
        default: off
        related: [.domain/billing/features.md#invoicing]
        ```

        Decided at release, from configuration.

        ## Invoice Currency

        ```meta
        status: draft
        type: setting
        key: billing.currency
        scope: tenant
        default: EUR
        related: [.domain/billing/features.md#invoicing]
        ```

        Chosen at runtime by whoever `scope` names.

        ## Clerk

        ```meta
        status: draft
        type: user
        role: BillingClerk
        related: [.domain/billing/features.md#invoicing]
        ```

        The person who issues invoices.

        ## Dependencies

        ### Outbound dependencies

        | Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
        |---|---|---|---|---|
        | Payments | Customer/Supplier | event | PaymentReceived | settles invoices |
        """;

    /// <summary>The <c>requirements.md</c> template, filled in.</summary>
    private const string RequirementsFile = """
        # Billing

        ```meta
        status: draft
        type: requirements
        ```

        > What this context's features guarantee, one chapter per feature. Each
        > requirement is one SHALL sentence with the scenarios that prove it.

        ## Invoicing

        ```meta
        status: draft
        type: requirements
        related: [.domain/billing/features.md#invoicing]
        ```

        > The requirements of one feature.

        ### Requirement: An invoice is numbered once

        ```meta
        status: draft
        type: requirement
        tests: e2e:playwright:tests/e2e/invoicing.spec.ts#numbers once
        ```

        The system SHALL give every issued invoice exactly one number.

        #### Scenario: Issuing an invoice

        - **Given** a draft invoice
        - **When** the clerk issues it
        - **Then** it carries a number
        """;

    /// <summary>The <c>invariants.md</c> template, filled in.</summary>
    private const string InvariantsFile = """
        # Billing

        ```meta
        status: draft
        type: invariants
        ```

        > What each aggregate in this context enforces, one chapter per aggregate.

        ## Invoice

        ```meta
        status: draft
        type: invariants
        related: [.domain/billing/domain.md#invoice]
        ```

        > The invariants of one aggregate.

        ### Invariant: An issued invoice cannot be edited

        ```meta
        status: draft
        type: invariant
        tests: unit:dotnet:Billing.Domain.Tests.InvoiceTests.IssuedIsFrozen
        ```

        An issued invoice's lines never change.

        Enforced at: AddLine()

        #### Scenario: Adding a line after issue

        - **Given** InvoiceIssued
        - **When** AddLine
        - **Then** the command is rejected
        """;

    [Fact]
    public async Task A_context_reads_in_the_conventions_order_with_split_files_after_their_base_and_pages_last()
    {
        var repo = TempDir();
        var context = WriteContext(repo,
            "context.md", "domain.md", "domain.invoice.md", "actors.md", "features.md",
            "requirements.md", "requirements.invoicing.md", "invariants.md", "invariants.invoice.md",
            "model.md", "flow.md", "flow.issue.md", "dependencies.md", "go-live-takeover.md", "naming.md");

        var view = await Load(repo);

        // devbook-domain.md: context.md is the root and reads first, then the
        // listed files in the tree's order, a split file directly after the file
        // it is named after, and the additional pages after all of them. None of
        // this is filename order — `actors` would otherwise precede `context`.
        Assert.Equal(
            [
                "context.md", "domain.md", "domain.invoice.md", "actors.md", "features.md",
                "requirements.md", "requirements.invoicing.md", "invariants.md", "invariants.invoice.md",
                "model.md", "flow.md", "flow.issue.md", "dependencies.md", "go-live-takeover.md", "naming.md"
            ],
            Assert.Single(view.Contexts).Documents.Select(document => Path.GetFileName(document.Path)));
        Assert.Equal(".domain/billing", context);
    }

    [Fact]
    public async Task A_split_file_reads_in_its_base_files_place_when_the_base_is_gone()
    {
        var repo = TempDir();
        WriteContext(repo, "context.md", "domain.md", "invariants.md", "model.order.md", "model.invoice.md", "flow.md");

        var view = await Load(repo);

        Assert.Equal(
            ["context.md", "domain.md", "invariants.md", "model.invoice.md", "model.order.md", "flow.md"],
            Assert.Single(view.Contexts).Documents.Select(document => Path.GetFileName(document.Path)));
    }

    [Fact]
    public async Task The_context_takes_its_name_and_status_from_context_md_its_root_document()
    {
        var repo = TempDir();
        WriteContext(repo, "context.md", "domain.md");
        File.WriteAllText(Path.Combine(repo, ".domain", "billing", "domain.md"), "# Domain: Legacy Name\n\n```meta\ntype: domain\nstatus: proposed\n```\n");

        var view = await Load(repo);

        var context = Assert.Single(view.Contexts);
        Assert.Equal("Billing", context.DisplayName);
        Assert.Equal("draft", context.Status);
        Assert.Equal(DomainDevbookDocumentKind.Context, context.RootDocument!.Kind);
        Assert.Equal("root", context.RootDocument.Metadata["index"]);
    }

    [Fact]
    public async Task A_context_with_no_context_md_still_takes_its_name_from_domain_md()
    {
        // The legacy fallback: a context written before contract 11.
        var repo = TempDir();
        WriteContext(repo, "domain.md", "features.md");
        File.WriteAllText(Path.Combine(repo, ".domain", "billing", "domain.md"), "# Domain: Legacy Billing\n\n```meta\nstatus: proposed\n```\n");

        var view = await Load(repo);

        var context = Assert.Single(view.Contexts);
        Assert.Equal("Legacy Billing", context.DisplayName);
        Assert.Equal("proposed", context.Status);
    }

    [Fact]
    public async Task The_index_route_takes_context_md_as_the_root_and_reads_in_the_conventions_order()
    {
        // An outline written by a generator that predates contract 11 marks
        // domain.md as the root and lists the files in its own order. The
        // convention wins on both.
        var repo = TempDir();
        WriteContext(repo, "context.md", "domain.md", "requirements.md");
        WriteIndex(repo, ["requirements.md", "domain.md", "context.md"], root: "domain.md");

        var view = await Load(repo);

        var context = Assert.Single(view.Contexts);
        Assert.Equal("Billing (from the index)", context.DisplayName);
        Assert.Equal(
            ["context.md", "domain.md", "requirements.md"],
            context.Documents.Select(document => Path.GetFileName(document.Path)));
    }

    [Theory]
    [InlineData("context-map.md", DomainDevbookDocumentKind.ContextMap)]
    [InlineData("context.md", DomainDevbookDocumentKind.Context)]
    [InlineData("domain.md", DomainDevbookDocumentKind.Domain)]
    [InlineData("actors.md", DomainDevbookDocumentKind.Actors)]
    [InlineData("features.md", DomainDevbookDocumentKind.Features)]
    [InlineData("skills.md", DomainDevbookDocumentKind.Skills)]
    [InlineData("requirements.md", DomainDevbookDocumentKind.Requirements)]
    [InlineData("invariants.md", DomainDevbookDocumentKind.Invariants)]
    [InlineData("model.md", DomainDevbookDocumentKind.Model)]
    [InlineData("flow.md", DomainDevbookDocumentKind.Flow)]
    [InlineData("dependencies.md", DomainDevbookDocumentKind.Dependencies)]
    [InlineData("domain.order.md", DomainDevbookDocumentKind.Domain)]
    [InlineData("skills.flow-code.md", DomainDevbookDocumentKind.Skills)]
    [InlineData("requirements.checkout.md", DomainDevbookDocumentKind.Requirements)]
    [InlineData("invariants.order.md", DomainDevbookDocumentKind.Invariants)]
    [InlineData("flow.flow-code.md", DomainDevbookDocumentKind.Flow)]
    [InlineData("naming.md", DomainDevbookDocumentKind.Page)]
    [InlineData("go-live-takeover.md", DomainDevbookDocumentKind.Page)]
    [InlineData("context.billing.md", DomainDevbookDocumentKind.Page)]
    [InlineData("index.md", DomainDevbookDocumentKind.Other)]
    public void Every_contract_16_file_is_the_kind_its_name_says(string file, DomainDevbookDocumentKind kind)
    {
        // context.md does not split — "it is the root document and what it holds
        // is small by construction" — so context.billing.md is a page, not a
        // context file.
        Assert.Equal(kind, DomainDevbookStore.KindFromFile(file));
    }

    [Fact]
    public void A_files_type_is_the_schemas_own_value_including_a_pages_own_filename()
    {
        // Every kind the mark bridge names is a file type the schema knows, and
        // every file type the schema knows has a kind that names it.
        foreach (var fileType in DevbookSchema.FileTypes(DevbookFolder.Domain))
        {
            Assert.Equal(fileType, DomainDevbookFileTypes.Of($"{fileType}.md"));
        }

        Assert.Equal("domain", DomainDevbookFileTypes.Of("domain.order.md"));
        Assert.Equal("go-live-takeover", DomainDevbookFileTypes.Of("go-live-takeover.md"));
        Assert.True(DevbookSchema.IsKnownType(DevbookFolder.Domain, DevbookMetadataLevel.File, DomainDevbookFileTypes.Of("naming.md"), "naming.md"));
        Assert.Null(DomainDevbookFileTypes.Of("index.md"));
    }

    [Fact]
    public async Task Requirements_and_invariants_files_are_read_with_their_chapters_types()
    {
        var repo = TempDir();
        WriteContext(repo, "context.md", "requirements.md", "invariants.md");

        var documents = Assert.Single((await Load(repo)).Contexts).Documents;

        var requirements = Assert.Single(documents, document => document.Kind == DomainDevbookDocumentKind.Requirements);
        Assert.Equal("requirements", requirements.Metadata["type"]);
        var feature = Assert.Single(requirements.Sections);
        Assert.Equal("Invoicing", feature.Title);
        Assert.Equal("requirements", feature.Metadata["type"]);

        var invariants = Assert.Single(documents, document => document.Kind == DomainDevbookDocumentKind.Invariants);
        var aggregate = Assert.Single(invariants.Sections);
        Assert.Equal("Invoice", aggregate.Title);
        Assert.Equal("invariants", aggregate.Metadata["type"]);
        Assert.Contains(".domain/billing/domain.md#invoice", aggregate.Links);
    }

    [Fact]
    public async Task Deployment_is_read_from_context_md_and_from_the_map_chapter_whose_related_names_it()
    {
        var repo = TempDir();
        WriteContext(repo, "context.md");
        WriteContextMap(repo, """
            ## Invoicing and Billing

            ```meta
            type: bounded-context
            deployment: module
            related: [.domain/billing/context.md, .arc42/05-building-block-view.md#monolith]
            ```

            Where invoices are kept.
            """);

        var context = Assert.Single((await Load(repo)).Contexts);

        // Matched by `related`, although the heading slugs to something else.
        Assert.Equal("Invoicing and Billing", context.MapChapter!.Title);
        Assert.Equal(new DomainDevbookDeployment("module", "module"), context.Deployment);
        Assert.False(context.Deployment.Disagrees);
        Assert.Null(context.Deployment.Problem);
        Assert.Equal("module", context.Deployment.Value);
    }

    [Fact]
    public async Task A_map_chapter_without_the_reference_is_matched_by_its_heading()
    {
        var repo = TempDir();
        WriteContext(repo, "context.md");
        WriteContextMap(repo, """
            ## Billing

            ```meta
            type: bounded-context
            deployment: service
            ```

            Where invoices are kept.
            """);

        var context = Assert.Single((await Load(repo)).Contexts);

        Assert.Equal("Billing", context.MapChapter!.Title);
        Assert.True(context.Deployment.Disagrees);
        Assert.Equal(
            "Deployment disagrees: the bounded-context chapter in context-map.md says \"service\", and context.md says \"module\".",
            context.Deployment.Problem);
    }

    [Fact]
    public async Task A_value_on_one_side_only_is_a_disagreement_and_neither_side_is_none()
    {
        var repo = TempDir();
        WriteContext(repo, "context.md");
        WriteContextMap(repo, """
            ## Billing

            ```meta
            type: bounded-context
            ```

            Where invoices are kept.
            """);

        var context = Assert.Single((await Load(repo)).Contexts);

        Assert.True(context.Deployment.Disagrees);
        Assert.Contains("context-map.md states none", context.Deployment.Problem, StringComparison.Ordinal);
        Assert.Contains("context.md says \"module\"", context.Deployment.Problem, StringComparison.Ordinal);

        var neither = new DomainDevbookDeployment(null, null);
        Assert.False(neither.IsStated);
        Assert.False(neither.Disagrees);
    }

    [Fact]
    public async Task Only_a_bounded_context_chapter_stands_for_a_context()
    {
        // The map's structural sections carry no block; a heading that happens to
        // slug to the context's name is not its chapter unless it says so.
        var repo = TempDir();
        WriteContext(repo, "context.md");
        WriteContextMap(repo, """
            ## Billing

            The billing part of the landscape, as prose.
            """);

        var context = Assert.Single((await Load(repo)).Contexts);

        Assert.Null(context.MapChapter);
        Assert.Equal(new DomainDevbookDeployment(null, "module"), context.Deployment);
    }

    /// <summary>Writes a <c>billing</c> context holding the named files, each a
    /// minimal file of its kind unless a fixture above is its template.</summary>
    private static string WriteContext(string repo, params string[] files)
    {
        var folder = Path.Combine(repo, ".domain", "billing");
        Directory.CreateDirectory(folder);
        if (!File.Exists(Path.Combine(repo, ".domain", "context-map.md"))) WriteContextMap(repo, string.Empty);

        foreach (var file in files)
        {
            var text = file switch
            {
                "context.md" => ContextFile,
                "requirements.md" => RequirementsFile,
                "invariants.md" => InvariantsFile,
                _ => $"# Billing\n\n```meta\ntype: {DomainDevbookFileTypes.Of(file)}\n```\n\nThe {file} file.\n"
            };
            File.WriteAllText(Path.Combine(folder, file), text);
        }

        return ".domain/billing";
    }

    private static void WriteContextMap(string repo, string chapters)
    {
        Directory.CreateDirectory(Path.Combine(repo, ".domain"));
        File.WriteAllText(Path.Combine(repo, ".domain", "context-map.md"),
            $"# Order Platform\n\n```meta\nstatus: draft\ntype: context-map\n```\n\nThe contexts.\n\n{chapters}\n\n## Subdomain landscape\n\n| Subdomain | Classification | Bounded context |\n|---|---|---|\n| Billing | Supporting | Billing |\n");
    }

    /// <summary>A <c>_meta/index.json</c> outline for the billing context, in the
    /// generator's shape, listing the files in the order given and marking one as
    /// the root.</summary>
    private static void WriteIndex(string repo, IReadOnlyList<string> files, string root)
    {
        var metaDir = Path.Combine(repo, ".domain", "_meta");
        Directory.CreateDirectory(metaDir);

        var children = string.Join(",\n", files.Select(file =>
            $$"""        { "type": "file", "name": "{{file}}", "path": ".domain/billing/{{file}}", "title": "{{(file == root ? "Domain: Stale" : "Billing (from the index)")}}", "status": "draft"{{(file == root ? ", \"root\": true" : string.Empty)}} }"""));

        File.WriteAllText(Path.Combine(metaDir, "index.json"), $$"""
{
  "schemaVersion": 1,
  "generatedBy": ".github/tools/knowledge-meta/build.mjs",
  "scope": ".domain",
  "sources": [".domain"],
  "problems": [],
  "entries": [
    { "type": "file", "name": "context-map.md", "path": ".domain/context-map.md",
      "title": "Order Platform", "status": "draft", "root": true },
    { "type": "directory", "name": "billing", "path": ".domain/billing", "title": "Domain: Stale",
      "children": [
{{children}}
      ] }
  ]
}
""");
    }

    private async Task<DomainDevbookView> Load(string repo)
    {
        var settings = new GitHubSettingsStore(Path.Combine(TempDir(), "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        settings.SetRepositories(repositories);
        settings.SetCloneDirectory("backlog", repo);

        var view = await new DomainDevbookStore(new DevbookFolderSource(settings)).LoadAsync("backlog", TestContext.Current.CancellationToken);
        Assert.Null(view.Error);
        return view;
    }

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "domain-devbook-context-files", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
