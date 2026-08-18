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
order: ["inbox"]
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