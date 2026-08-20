using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Which identity hue each repository wears, and where that choice is kept.
/// <para>
/// Asserted here rather than on any one screen because the whole point of moving the
/// choice out of the roadmap plan is that there is one answer — see
/// <c>.design/color-scheme.md#band-identity-tokens</c>. A test per surface would be
/// four tests of four answers.
/// </para>
/// </summary>
public class RepositoryColourTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "repository-colour-tests-" + Guid.NewGuid().ToString("N"));

    private GitHubSettingsStore Store() => new(Path.Combine(_root, "github.json"));

    private static GitHubRepositoryRef Repository(string alias, int? colour = null) =>
        new(alias, "JSdotNet", alias) { Colour = colour };

    // --- Resolution -----------------------------------------------------------

    [Fact]
    public void ARepositoryNobodyHasColouredTakesItsPosition()
    {
        var resolved = RepositoryColours.Resolve([Repository("one"), Repository("two"), Repository("three")]);

        Assert.Equal(1, resolved["one"]);
        Assert.Equal(2, resolved["two"]);
        Assert.Equal(3, resolved["three"]);
    }

    [Fact]
    public void AChosenColourWins()
    {
        var resolved = RepositoryColours.Resolve([Repository("one", 4), Repository("two", 2)]);

        Assert.Equal(4, resolved["one"]);
        Assert.Equal(2, resolved["two"]);
    }

    [Fact]
    public void AnAutomaticColourStepsOverTheOnesAlreadyClaimed()
    {
        // Otherwise the repository beside a deliberately-coloured one lands on the very
        // hue that choice was making room for.
        var resolved = RepositoryColours.Resolve([Repository("one"), Repository("two", 1), Repository("three")]);

        Assert.Equal(2, resolved["one"]);
        Assert.Equal(1, resolved["two"]);
        Assert.Equal(3, resolved["three"]);
    }

    [Fact]
    public void PastFiveTheHuesWrap()
    {
        var many = Enumerable.Range(1, 7).Select(index => Repository($"repo{index}")).ToList();

        var resolved = RepositoryColours.Resolve(many);

        // A sixth repository repeats the first hue rather than growing the set, which
        // the design section allows because the hue is not the identifier — the alias
        // beside it is.
        Assert.Equal(1, resolved["repo6"]);
        Assert.Equal(2, resolved["repo7"]);
        Assert.All(resolved.Values, colour => Assert.InRange(colour, 1, RepositoryColours.Available));
    }

    [Fact]
    public void EveryRepositoryGetsAHue()
    {
        var resolved = RepositoryColours.Resolve([Repository("one"), Repository("two", 3)]);

        Assert.Equal(2, resolved.Count);
    }

    // --- Storage --------------------------------------------------------------

    [Fact]
    public void AColourIsPersistedAsSoonAsItIsChosen()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog")]));

        Assert.Null(store.SetRepositoryColour("backlog", 3));

        // Read back through a second store over the same file: the house rule is no
        // save button, so the choice has to be on disk already.
        Assert.Equal(3, Store().Current.Find("backlog")!.Colour);
    }

    [Fact]
    public void AColourCanBeGivenBack()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog")]));
        Assert.Null(store.SetRepositoryColour("backlog", 3));

        Assert.Null(store.SetRepositoryColour("backlog", null));

        Assert.Null(Store().Current.Find("backlog")!.Colour);

        // And the repository still has a hue — clearing the choice returns it to the
        // automatic one rather than leaving it colourless.
        Assert.Equal(1, Store().Current.ColourFor("backlog"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void AColourOutsideTheSanctionedSetIsRefused(int colour)
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog")]));

        Assert.NotNull(store.SetRepositoryColour("backlog", colour));
        Assert.Null(store.Current.Find("backlog")!.Colour);
    }

    [Fact]
    public void AStoredColourOutsideTheSetIsDroppedOnLoad_NotClamped()
    {
        var store = Store();

        // Straight into the file the way a hand edit would put it there.
        Assert.Null(store.SetRepositories([Repository("backlog") with { Colour = 9 }]));

        // Clamping would hand somebody a hue they never chose and make it look like a
        // choice they had made.
        Assert.Null(Store().Current.Find("backlog")!.Colour);
    }

    [Fact]
    public void ColouringARepositoryThatIsNoLongerConfiguredIsRefused()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog")]));

        Assert.NotNull(store.SetRepositoryColour("gone", 2));
    }

    [Fact]
    public void RetypingTheRepositoryListKeepsTheColours()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog")]));
        Assert.Null(store.SetRepositoryColour("backlog", 5));

        // What Settings does when somebody edits the text box: the whole list is
        // committed again, and everything configured per repository has to survive it.
        var (repositories, errors) = GitHubSettings.ParseText("backlog = JSdotNet/Backlog\ndocs = JSdotNet/Docs");
        Assert.Empty(errors);
        Assert.Null(store.SetRepositories(repositories));

        Assert.Equal(5, store.Current.Find("backlog")!.Colour);
    }

    [Fact]
    public void AnAliasThatNamesNothingConfiguredHasNoColour()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog")]));

        Assert.Null(store.Current.ColourFor("nowhere"));
    }

    // --- The visualization gate -----------------------------------------------
    //
    // Whether the hues are drawn at all is a separate question from which hue a
    // repository wears, and it is answered in the same place for the same reason: a
    // surface that decided for itself would be a second answer. So the store keeps both
    // the choice and the visibility, and hands out a gated view of the choice.

    [Fact]
    public void TheVisualizationIsOffUntilSomebodyTurnsItOn()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog"), Repository("docs")]));

        // Off is the first-run answer rather than a migration step: the identity hue is
        // an opt-in layer over a workspace that reads perfectly well without it.
        Assert.False(store.Current.ShowRepositoryColours);
        Assert.Empty(store.Current.VisibleColours());
        Assert.Null(store.Current.VisibleColourFor("backlog"));
    }

    [Fact]
    public void ASettingsFileWrittenBeforeTheToggleExistedReadsAsOff()
    {
        var path = Path.Combine(_root, "github.json");
        Directory.CreateDirectory(_root);

        // Exactly what a build from before this change left on disk: no property at all.
        File.WriteAllText(
            path,
            """
            {
              "repositories": [ { "alias": "backlog", "owner": "JSdotNet", "name": "Backlog" } ],
              "apiEndpoint": "https://api.github.com"
            }
            """);

        var store = new GitHubSettingsStore(path);

        Assert.False(store.Current.ShowRepositoryColours);

        // And the colour the repository would wear is still there to be read, because
        // the visualization being off is not the same as the choice being gone.
        Assert.Equal(1, store.Current.ColourFor("backlog"));
    }

    [Fact]
    public void WithTheVisualizationOnTheSurfacesSeeExactlyTheResolvedColours()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog", 4), Repository("docs")]));
        Assert.Null(store.SetShowRepositoryColours(true));

        // The gate decides whether the answer is handed over, never what the answer is.
        Assert.Equal(store.Current.Colours(), store.Current.VisibleColours());
        Assert.Equal(4, store.Current.VisibleColourFor("backlog"));
        Assert.Equal(store.Current.ColourFor("docs"), store.Current.VisibleColourFor("docs"));
    }

    [Fact]
    public void TurningTheVisualizationOnLastsPastARestart()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog")]));

        Assert.Null(store.SetShowRepositoryColours(true));

        // A second store over the same file is what the next launch does, and the house
        // rule is no save button: it has to be on disk already.
        Assert.True(Store().Current.ShowRepositoryColours);
    }

    [Fact]
    public void TurningTheVisualizationOffAgainLastsToo()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog")]));
        Assert.Null(store.SetShowRepositoryColours(true));

        Assert.Null(store.SetShowRepositoryColours(false));

        Assert.False(Store().Current.ShowRepositoryColours);
    }

    [Fact]
    public void ChangingTheVisualizationAnnouncesItself()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog")]));

        var announced = 0;
        store.Changed += () => announced++;

        Assert.Null(store.SetShowRepositoryColours(true));

        // Every surface reading the gated answer is already on screen when it flips, and
        // the ones that read per load rather than per render — the roadmap band — only
        // redraw because they are told.
        Assert.Equal(1, announced);
    }

    [Fact]
    public void RetypingTheRepositoryListKeepsTheVisualizationOn()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog")]));
        Assert.Null(store.SetShowRepositoryColours(true));

        var (repositories, errors) = GitHubSettings.ParseText("backlog = JSdotNet/Backlog\ndocs = JSdotNet/Docs");
        Assert.Empty(errors);
        Assert.Null(store.SetRepositories(repositories));

        // A save path that wrote the settings without carrying this forward would turn
        // somebody's visualization off behind their back the next time they edited the
        // repository list.
        Assert.True(store.Current.ShowRepositoryColours);
        Assert.True(Store().Current.ShowRepositoryColours);
    }

    [Fact]
    public void ChoosingAColourKeepsTheVisualizationOn()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog")]));
        Assert.Null(store.SetShowRepositoryColours(true));

        Assert.Null(store.SetRepositoryColour("backlog", 3));

        Assert.True(Store().Current.ShowRepositoryColours);
    }

    [Fact]
    public void EveryOtherSavePathKeepsTheVisualizationOn()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog"), Repository("docs")]));
        Assert.Null(store.SetShowRepositoryColours(true));

        Assert.Null(store.SetRepositoryToken("backlog", "ghp_token"));
        Assert.Null(store.SetCloneDirectory("backlog", Path.Combine(_root, "clone")));
        Assert.Null(store.SetApiEndpoint("https://github.example/api/v3"));
        Assert.Null(store.RemoveRepository("docs"));

        Assert.True(Store().Current.ShowRepositoryColours);
    }

    [Fact]
    public void TheChoiceItselfIsStillReadableWhileTheVisualizationIsOff()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog")]));
        Assert.Null(store.SetRepositoryColour("backlog", 2));

        // Settings' own swatches are the one control whose subject *is* the colour, so
        // they read the ungated answer and keep showing the choice with the
        // visualization off.
        Assert.Equal(2, store.Current.Find("backlog")!.Colour);
        Assert.Equal(2, store.Current.ColourFor("backlog"));
        Assert.Equal(2, store.Current.Colours()["backlog"]);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
