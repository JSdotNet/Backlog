using Backlog.Desktop.UI.Services;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class DomainKnowledgeStoreTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    [Fact]
    public async Task Reports_configuration_needed_when_no_repository_is_configured()
    {
        var view = await new DomainKnowledgeStore(NewSettingsStore()).LoadAsync();

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

        var view = await new DomainKnowledgeStore(settings).LoadAsync();

        Assert.Null(view.Error);
        Assert.Equal("JSdotNet/Backlog", view.RepositoryLabel);
        Assert.Equal("Context Map: Test", view.ContextMap.Title);
        Assert.Equal("draft", view.ContextMap.Status);
        Assert.Equal("context map", Assert.Single(view.ContextMap.Diagrams).Kind);

        var context = Assert.Single(view.Contexts);
        Assert.Equal("Inbox", context.DisplayName);
        Assert.Equal("active", context.Status);

        var domain = context.Documents.Single(d => d.Kind == DomainKnowledgeDocumentKind.Domain);
        var aggregate = Assert.Single(domain.Sections, s => s.Title == "Aggregate: Inbox Item");
        Assert.Equal("active", aggregate.Status);
        Assert.Contains(".domain/context-map.md", aggregate.Links);

        var model = context.Documents.Single(d => d.Kind == DomainKnowledgeDocumentKind.Model);
        Assert.Equal("domain model", Assert.Single(model.Diagrams).Kind);
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

        var view = await new DomainKnowledgeStore(settings).LoadAsync();

        Assert.Null(view.Error);
        Assert.EndsWith(Path.Combine("knowledge", ".domain"), view.RootPath);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
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
