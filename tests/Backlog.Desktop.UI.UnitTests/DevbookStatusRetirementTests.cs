using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The stores after the status writer retired: none of them writes a workflow
/// <c>status</c> into <c>arc42/</c>, <c>domain/</c> or <c>design/</c>, and the two
/// folders whose <c>status</c> is a rating still take one.
///
/// <para>Under devbook's branch-true decision a chapter's workflow state is the
/// tool's, kept in the subject store; the file keeps what the chapter says. An
/// authored <c>status</c> in those three folders still reads, as a badge, until the
/// devbook migration strips it — it is only the product's write that went. The
/// exemption is spelled as "the method does not exist" where a whole store lost the
/// write, as <c>.tech</c>'s missing clear always was, because a guard is something a
/// later caller can copy wrongly; the design provider shares its class with
/// <c>ai/</c>, so there the rating writer's folder check is the refusal.</para>
/// </summary>
public sealed class DevbookStatusRetirementTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public void Neither_the_architecture_nor_the_domain_store_writes_a_status()
    {
        Assert.Null(typeof(Arc42DevbookStore).GetMethod("UpdateStatusAsync"));
        Assert.Null(typeof(Arc42DevbookStore).GetMethod("ClearStatusAsync"));
        Assert.Null(typeof(DomainDevbookStore).GetMethod("UpdateStatusAsync"));
        Assert.Null(typeof(DomainDevbookStore).GetMethod("ClearStatusAsync"));
    }

    /// <summary>A rating is set and never cleared: <c>ai/</c> requires one, and an
    /// absent <c>tech/</c> rating could not be told apart from <c>candidate</c>. The
    /// only clear any document folder had was design's.</summary>
    [Fact]
    public void No_store_clears_a_status_any_more()
    {
        Assert.Null(typeof(DesignDevbookProvider).GetMethod("ClearStatusAsync"));
        Assert.Null(typeof(AiDevbookProvider).GetMethod("ClearStatusAsync"));
        Assert.Null(typeof(TechnologyDevbookService).GetMethod("ClearStatusAsync"));
    }

    [Fact]
    public async Task A_design_status_write_is_refused_and_the_file_left_alone()
    {
        var root = NewRoot();
        var file = Path.Combine(root, ".design", "colors.md");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        const string original = "# Colors\n\n```meta\nstatus: draft\n```\n\nPalette.\n";
        File.WriteAllText(file, original);
        using var design = new DesignDevbookProvider(new DevbookFolderSource(ConfiguredSettings(root)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            design.UpdateStatusAsync("backlog", ".design/colors.md", "deprecated", TestContext.Current.CancellationToken));

        Assert.Equal(original, File.ReadAllText(file));
    }

    [Fact]
    public async Task An_ai_rating_is_still_written_through_its_store()
    {
        var root = NewRoot();
        var file = Path.Combine(root, ".ai", "01-author.md");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "# Author\n\n```meta\nstatus: trial\n```\n\nWriting with AI.\n");
        using var ai = new AiDevbookProvider(new DevbookFolderSource(ConfiguredSettings(root)));

        await ai.UpdateStatusAsync("backlog", ".ai/01-author.md", "adopted", TestContext.Current.CancellationToken);

        Assert.Contains("status: adopted", File.ReadAllText(file), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-devbook-retirement-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _roots.Add(root);
        return root;
    }

    private static GitHubSettingsStore ConfiguredSettings(string repo)
    {
        var settings = new GitHubSettingsStore(Path.Combine(repo, "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        settings.SetRepositories(repositories);
        settings.SetCloneDirectory("backlog", repo);
        return settings;
    }
}
