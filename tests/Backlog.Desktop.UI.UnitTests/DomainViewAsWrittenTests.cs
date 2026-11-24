using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Domain view's As written reading held across a repository switch. The pane
/// keeps the view mounted and the selected path when the repository changes, so the
/// same relative path can name another repository's copy of the file. That copy is
/// another file: a Compare baseline read from the first, or an edit mode opened on
/// it, must not carry over to it.
/// </summary>
public sealed class DomainViewAsWrittenTests : IDisposable
{
    private readonly List<TemporaryDirectory> _temps = [];

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>Each repository's chapter has a last commit of its own that differs
    /// from its working copy. Compared with its last commit in the first, then
    /// switched to the second: the comparison is closed and its baseline is back on
    /// As opened, and compared with the last commit again it is the second's own,
    /// never the first's.</summary>
    [Fact]
    public void Compare_does_not_carry_over_to_another_repositorys_copy_of_the_file()
    {
        using var harness = CreateHarness();

        var component = harness.Render(harness.First);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-compare']")), Patience);
        component.Find("[data-testid='domain-chapter-file-compare']").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-baseline-committed']")), Patience);
        component.Find("[data-testid='domain-chapter-file-baseline-committed']").Click();
        component.WaitForAssertion(() => Assert.Contains(FirstCommit, Body(component), StringComparison.Ordinal), Patience);

        component.Render(parameters => parameters.Add(view => view.RepositoryAlias, harness.Second));

        component.WaitForAssertion(() => Assert.Contains("The second repository's map.", Body(component), StringComparison.Ordinal), Patience);
        component.WaitForAssertion(() => Assert.Equal("false", component.Find("[data-testid='domain-chapter-file-compare']").GetAttribute("aria-pressed")), Patience);
        Assert.DoesNotContain(FirstCommit, Body(component), StringComparison.Ordinal);

        component.Find("[data-testid='domain-chapter-file-compare']").Click();
        component.WaitForAssertion(() => Assert.Equal("true", component.Find("[data-testid='domain-chapter-file-baseline-opened']").GetAttribute("aria-pressed")), Patience);
        Assert.Equal("false", component.Find("[data-testid='domain-chapter-file-baseline-committed']").GetAttribute("aria-pressed"));
        component.Find("[data-testid='domain-chapter-file-baseline-committed']").Click();

        component.WaitForAssertion(() => Assert.Contains(SecondCommit, Body(component), StringComparison.Ordinal), Patience);
        Assert.DoesNotContain(FirstCommit, Body(component), StringComparison.Ordinal);
    }

    private const string FirstCommit = "What the first map said at its last commit.";
    private const string SecondCommit = "What the second map said at its last commit.";

    private static string Body(IRenderedComponent<DomainViewAsWritten> component) =>
        component.Find("[data-testid='domain-chapter-file']").TextContent;

    [Fact]
    public void An_open_editor_does_not_carry_over_to_another_repositorys_copy_of_the_file()
    {
        using var harness = CreateHarness();

        var component = harness.Render(harness.First);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-edit']")), Patience);
        component.Find("[data-testid='domain-chapter-file-edit']").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("textarea")), Patience);

        component.Render(parameters => parameters.Add(view => view.RepositoryAlias, harness.Second));

        component.WaitForAssertion(() => Assert.Contains("The second repository's map.", component.Find("[data-testid='domain-chapter-file'] .file-view__body").TextContent, StringComparison.Ordinal), Patience);
        Assert.Empty(component.FindAll("textarea"));
    }

    private Harness CreateHarness()
    {
        var first = NewRoot("The first repository's map.");
        var second = NewRoot("The second repository's map.");

        var settings = new WorkspaceSettingsStore(Path.Combine(first, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(first, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog\nfork = someone/Backlog");
        Assert.Empty(errors);
        var configured = repositories
            .Select((entry, index) => entry with { CloneDirectory = index == 0 ? first : second, DevbookFolders = DevbookFolderSetting.Defaults() })
            .ToList();
        Assert.Equal(2, configured.Count);
        Assert.Null(gitHub.SetRepositories(configured));

        // Each repository's chapter was last committed as something else.
        var history = new StubGitFileHistory(path => GitFileAtRevisionResult.Committed(
            $"# Context Map\n\n{(path.StartsWith(first, StringComparison.OrdinalIgnoreCase) ? FirstCommit : SecondCommit)}\n"));

        var folders = new DevbookFolderSource(gitHub, settings);
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IDevbookFolderSource>(folders);
        context.Services.AddSingleton(new DomainDevbookStore(folders));
        context.Services.AddSingleton<DevbookChapterWriter>();
        context.Services.AddSingleton<IGitFileHistoryService>(history);

        return new Harness(context, configured[0].Alias, configured[1].Alias);
    }

    private string NewRoot(string prose)
    {
        var temp = new TemporaryDirectory("backlog-domain-view-as-written");
        _temps.Add(temp);
        Directory.CreateDirectory(Path.Combine(temp.Path, ".domain"));
        File.WriteAllText(Path.Combine(temp.Path, ".domain", "context-map.md"), $"# Context Map\n\n```meta\nstatus: draft\n```\n\n{prose}\n");
        return temp.Path;
    }

    public void Dispose()
    {
        foreach (var temp in _temps) temp.Dispose();
    }

    private sealed record Harness(BunitContext Context, string First, string Second) : IDisposable
    {
        public IRenderedComponent<DomainViewAsWritten> Render(string alias) =>
            Context.Render<DomainViewAsWritten>(parameters => parameters
                .Add(view => view.RepositoryAlias, alias)
                .Add(view => view.SelectedPath, "context-map.md"));

        public void Dispose() => Context.Dispose();
    }
}
