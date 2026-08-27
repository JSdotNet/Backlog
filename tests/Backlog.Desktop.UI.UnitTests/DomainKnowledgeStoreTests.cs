using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class DomainKnowledgeStoreTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    [Fact]
    public async Task Reports_configuration_needed_when_no_repository_is_configured()
    {
        var view = await new DomainKnowledgeStore(new KnowledgeFolderSource(NewSettingsStore())).LoadAsync();

        Assert.NotNull(view.Error);
        Assert.Empty(view.Contexts);
    }

    [Fact]
    public async Task Reads_context_map_contexts_metadata_links_and_diagrams()
    {
        var repo = TempDir();
        WriteDomain(repo);
        var settings = NewSettingsStore();
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        settings.SetRepositories(repositories);
        settings.SetCloneDirectory("backlog", repo);

        var view = await new DomainKnowledgeStore(new KnowledgeFolderSource(settings)).LoadAsync();

        Assert.Null(view.Error);
        Assert.Equal("JSdotNet/Backlog", view.RepositoryLabel);
        Assert.Equal(repo, view.RepositoryRoot);
        Assert.Equal("Context Map: Test", view.ContextMap.Title);
        Assert.Equal("draft", view.ContextMap.Status);
        Assert.Equal("context map", Assert.Single(view.ContextMap.Diagrams).Kind);
        Assert.Equal(".domain/inbox/domain.md, .domain/inbox/features.md", view.ContextMap.Metadata["related"]);

        var context = Assert.Single(view.Contexts);
        Assert.Equal("Inbox", context.DisplayName);
        Assert.Equal("active", context.Status);

        var domain = context.Documents.Single(d => d.Kind == DomainKnowledgeDocumentKind.Domain);
        var aggregate = Assert.Single(domain.Sections, s => s.Title == "Aggregate: Inbox Item");
        Assert.Equal("active", aggregate.Status);
        Assert.Contains(".domain/context-map.md", aggregate.Links);

        var model = context.Documents.Single(d => d.Kind == DomainKnowledgeDocumentKind.Model);
        Assert.Equal("domain model", Assert.Single(model.Diagrams).Kind);

        var features = context.Documents.Single(d => d.Kind == DomainKnowledgeDocumentKind.Features);
        Assert.Equal("planned", KnowledgeActionMetadata.Status(features.Metadata));
        var issue = KnowledgeActionMetadata.Issue(features.Metadata, view.RepositoryLabel);
        Assert.NotNull(issue);
        Assert.Equal("#77", issue.Label);
        Assert.Equal("https://github.com/JSdotNet/Backlog/issues/77", issue.Url);
    }

    [Fact]
    public async Task Reads_context_index_and_additional_markdown_documents()
    {
        var repo = TempDir();
        WriteDomain(repo);
        File.WriteAllText(Path.Combine(repo, ".domain", "inbox", "index.md"), "# Inbox overview");
        File.WriteAllText(Path.Combine(repo, ".domain", "inbox", "config.md"), "# Inbox config");
        var settings = ConfiguredSettings(repo);

        var view = await new DomainKnowledgeStore(new KnowledgeFolderSource(settings)).LoadAsync("backlog");

        var context = Assert.Single(view.Contexts);
        Assert.Equal(
            [".domain/inbox/domain.md", ".domain/inbox/index.md", ".domain/inbox/features.md", ".domain/inbox/model.md", ".domain/inbox/config.md"],
            context.Documents.Select(document => document.Path));
        Assert.Contains(context.Documents, document => document.Title == "Inbox overview" && document.Kind == DomainKnowledgeDocumentKind.Other);
        Assert.Contains(context.Documents, document => document.Title == "Inbox config" && document.Kind == DomainKnowledgeDocumentKind.Other);
    }

    [Fact]
    public async Task Honors_repository_relative_domain_folder_override()
    {
        var repo = TempDir();
        Directory.CreateDirectory(Path.Combine(repo, "knowledge"));
        WriteDomain(Path.Combine(repo, "knowledge"));
        var settings = NewSettingsStore();
        var (repositories, _) = GitHubSettings.ParseText("JSdotNet/Backlog");
        settings.SetRepositories(repositories);
        settings.SetCloneDirectory("backlog", repo);
        settings.SetKnowledgeFolder("backlog", ".domain", enabled: true, path: "knowledge/.domain");

        var view = await new DomainKnowledgeStore(new KnowledgeFolderSource(settings)).LoadAsync();

        Assert.Null(view.Error);
        Assert.EndsWith(Path.Combine("knowledge", ".domain"), view.RootPath);
    }


    [Fact]
    public async Task Updates_document_status_metadata()
    {
        var repo = TempDir();
        WriteDomain(repo);
        var settings = ConfiguredSettings(repo);
        var store = new DomainKnowledgeStore(new KnowledgeFolderSource(settings));

        await store.UpdateStatusAsync("backlog", ".domain/inbox/features.md", "accepted");
        var view = await store.LoadAsync("backlog");

        var features = Assert.Single(Assert.Single(view.Contexts).Documents, d => d.Kind == DomainKnowledgeDocumentKind.Features);
        Assert.Equal("accepted", features.Status);
        Assert.Contains("status: accepted", File.ReadAllText(Path.Combine(repo, ".domain", "inbox", "features.md")));
    }

    [Fact]
    public async Task Updates_section_status_metadata()
    {
        var repo = TempDir();
        WriteDomain(repo);
        var settings = ConfiguredSettings(repo);
        var store = new DomainKnowledgeStore(new KnowledgeFolderSource(settings));

        await store.UpdateStatusAsync("backlog", ".domain/inbox/features.md#feature-inbox-capture", "adopted");
        var view = await store.LoadAsync("backlog");

        var section = Assert.Single(Assert.Single(view.Contexts).Documents.Single(d => d.Kind == DomainKnowledgeDocumentKind.Features).Sections);
        Assert.Equal("adopted", section.Status);
        Assert.Contains("status: adopted", File.ReadAllText(Path.Combine(repo, ".domain", "inbox", "features.md")));
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }


    /// <summary>
    /// The point of the generated index: the panel can name every bounded
    /// context without opening one document. Proven by deleting the documents —
    /// a load that still names the context cannot have read them.
    /// </summary>
    [Fact]
    public async Task Lists_contexts_from_the_index_without_reading_their_documents()
    {
        var repo = TempDir();
        WriteDomain(repo);
        WriteDomainIndex(repo);
        File.Delete(Path.Combine(repo, ".domain", "inbox", "domain.md"));
        File.Delete(Path.Combine(repo, ".domain", "inbox", "features.md"));
        File.Delete(Path.Combine(repo, ".domain", "inbox", "model.md"));
        var settings = ConfiguredSettings(repo);

        var view = await new DomainKnowledgeStore(new KnowledgeFolderSource(settings)).LoadAsync("backlog");

        var context = Assert.Single(view.Contexts);
        Assert.Equal("Inbox", context.DisplayName);
        Assert.Equal("active", context.Status);
    }

    [Fact]
    public async Task Defers_reading_a_context_until_its_documents_are_asked_for()
    {
        var repo = TempDir();
        WriteDomain(repo);
        WriteDomainIndex(repo);
        var settings = ConfiguredSettings(repo);

        var view = await new DomainKnowledgeStore(new KnowledgeFolderSource(settings)).LoadAsync("backlog");

        var context = Assert.Single(view.Contexts);
        var documents = Assert.IsType<LazyKnowledgeList<DomainKnowledgeDocument>>(context.Documents);
        Assert.False(documents.IsMaterialized);

        var domain = context.Documents.Single(document => document.Kind == DomainKnowledgeDocumentKind.Domain);

        Assert.True(documents.IsMaterialized);
        Assert.Equal("Domain: Inbox", domain.Title);
        Assert.Equal("active", domain.Status);
        Assert.Single(domain.Sections, section => section.Title == "Aggregate: Inbox Item");
    }

    /// <summary>
    /// The committed index is refreshed deliberately, so it can lag the Markdown
    /// beside it. An entry whose file has been written since is read rather than
    /// trusted, which is what keeps an edit made between refreshes off the stale
    /// path.
    /// </summary>
    [Fact]
    public async Task Re_reads_a_context_root_edited_since_the_index_was_written()
    {
        var repo = TempDir();
        WriteDomain(repo);
        WriteDomainIndex(repo, contextTitle: "Stale Name", contextStatus: "draft");

        var domainPath = Path.Combine(repo, ".domain", "inbox", "domain.md");
        File.SetLastWriteTimeUtc(domainPath, DateTime.UtcNow.AddMinutes(5));
        var settings = ConfiguredSettings(repo);

        var view = await new DomainKnowledgeStore(new KnowledgeFolderSource(settings)).LoadAsync("backlog");

        var context = Assert.Single(view.Contexts);
        Assert.Equal("Inbox", context.DisplayName);
        Assert.Equal("active", context.Status);
    }

    [Fact]
    public async Task Falls_back_to_scanning_the_folder_when_no_index_is_present()
    {
        var repo = TempDir();
        WriteDomain(repo);
        Assert.False(Directory.Exists(Path.Combine(repo, ".domain", "_meta")));
        var settings = ConfiguredSettings(repo);

        var view = await new DomainKnowledgeStore(new KnowledgeFolderSource(settings)).LoadAsync("backlog");

        var context = Assert.Single(view.Contexts);
        Assert.Equal("Inbox", context.DisplayName);
        Assert.Equal("active", context.Status);
        Assert.Contains(context.Documents, document => document.Kind == DomainKnowledgeDocumentKind.Domain);
    }

    /// <summary>
    /// A payload shape this reader does not know is not something to guess at.
    /// The derived-artifact convention says an unrecognised <c>schemaVersion</c>
    /// sends the consumer back to the sources, which is the one behaviour that
    /// cannot go wrong when the envelope changes under it.
    /// </summary>
    [Fact]
    public async Task Falls_back_to_scanning_when_the_index_declares_an_unknown_schema_version()
    {
        var repo = TempDir();
        WriteDomain(repo);
        WriteDomainIndex(repo);

        var indexPath = Path.Combine(repo, ".domain", "_meta", "index.json");
        File.WriteAllText(indexPath, File.ReadAllText(indexPath).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99", StringComparison.Ordinal));
        var settings = ConfiguredSettings(repo);

        var view = await new DomainKnowledgeStore(new KnowledgeFolderSource(settings)).LoadAsync("backlog");

        var context = Assert.Single(view.Contexts);
        Assert.Equal("Inbox", context.DisplayName);
        Assert.Equal("active", context.Status);
        Assert.Contains(context.Documents, document => document.Kind == DomainKnowledgeDocumentKind.Domain);

        // The scan materialises its documents; only the index path defers them.
        Assert.IsNotType<LazyKnowledgeList<DomainKnowledgeDocument>>(context.Documents);
    }

    /// <summary>
    /// The outline the <c>knowledge-meta</c> generator writes to
    /// <c>.domain/_meta/index.json</c>, in the shape the real one has. Written
    /// last so it is newer than the Markdown, which is the state a fresh
    /// regeneration leaves behind.
    /// </summary>
    private static void WriteDomainIndex(string repoRoot, string contextTitle = "Inbox", string contextStatus = "active")
    {
        var metaDir = Path.Combine(repoRoot, ".domain", "_meta");
        Directory.CreateDirectory(metaDir);

        File.WriteAllText(Path.Combine(metaDir, "index.json"), $$"""
{
  "schemaVersion": 1,
  "generatedBy": ".github/tools/knowledge-meta/build.mjs",
  "scope": ".domain",
  "sources": [".domain"],
  "problems": [],
  "entries": [
    { "type": "file", "name": "context-map.md", "path": ".domain/context-map.md",
      "title": "Context Map: Test", "status": "draft", "root": true },
    { "type": "directory", "name": "inbox", "path": ".domain/inbox", "title": "{{contextTitle}}",
      "children": [
        { "type": "file", "name": "domain.md", "path": ".domain/inbox/domain.md",
          "title": "Domain: {{contextTitle}}", "status": "{{contextStatus}}", "root": true },
        { "type": "file", "name": "features.md", "path": ".domain/inbox/features.md",
          "title": "Features: Inbox", "status": "planned" },
        { "type": "file", "name": "model.md", "path": ".domain/inbox/model.md",
          "title": "Domain Model: Inbox", "status": "active" }
      ] }
  ]
}
""");
    }

    private GitHubSettingsStore ConfiguredSettings(string repo)
    {
        var settings = NewSettingsStore();
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        settings.SetRepositories(repositories);
        settings.SetCloneDirectory("backlog", repo);
        return settings;
    }
    private GitHubSettingsStore NewSettingsStore()
    {
        var path = Path.Combine(TempDir(), "github.json");
        return new GitHubSettingsStore(path);
    }

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "domain-knowledge-tests", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(path);
        return path;
    }

    private static void WriteDomain(string repoRoot)
    {
        var domainRoot = Path.Combine(repoRoot, ".domain");
        Directory.CreateDirectory(Path.Combine(domainRoot, "inbox"));

        File.WriteAllText(Path.Combine(domainRoot, "context-map.md"), """
# Context Map: Test

```meta
status: draft
related:
  - .domain/inbox/domain.md
  - .domain/inbox/features.md
```

> Test context map.

## Context map

```mermaid
flowchart LR
    Inbox[Inbox]
```
""");

        File.WriteAllText(Path.Combine(domainRoot, "inbox", "domain.md"), """
# Domain: Inbox

```meta
status: active
```

## Aggregate: Inbox Item

```meta
status: active
related: [.domain/context-map.md]
```

Owns the triage item.
""");

        File.WriteAllText(Path.Combine(domainRoot, "inbox", "features.md"), """
# Features: Inbox

```meta
status: planned
knowledge-issue: 77
```

## Feature: Inbox capture

```meta
status: active
knowledge-issue: owner/repo#99
```

Captures new items.
""");

        File.WriteAllText(Path.Combine(domainRoot, "inbox", "model.md"), """
# Domain Model: Inbox

```meta
status: active
```

## Model diagram

```mermaid
classDiagram
    class InboxItem
```
""");
    }
}