namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// The catalog the shell reads its surfaces from (local ADR 0043 §1): ordered by
/// group then <see cref="ShellSurface.Order"/>, filtered by each surface's feature,
/// refusing a duplicate id, and answering "not available" for an id it cannot offer.
/// </summary>
public class SurfaceCatalogTests
{
    private sealed class First;
    private sealed class Second;

    private static ShellSurface Surface(string id, ShellSurfaceGroup group, int order, string? featureKey = null) =>
        new(id, id, group, order, typeof(First), featureKey);

    [Fact]
    public void Surfaces_AreOrderedByGroupThenOrder_ViewsFirst()
    {
        var catalog = new SurfaceCatalog(
            [
                Surface("dashboard", ShellSurfaceGroup.Takeover, 20),
                Surface("roadmap", ShellSurfaceGroup.View, 20),
                Surface("devbook", ShellSurfaceGroup.Takeover, 10),
                Surface("tasks", ShellSurfaceGroup.View, 10),
            ],
            new Features());

        Assert.Equal(["tasks", "roadmap", "devbook", "dashboard"], catalog.Surfaces.Select(surface => surface.Id));
    }

    [Fact]
    public void ASurfaceWhoseFeatureIsOff_IsLeftOutAndNotAvailable_UntilTheFeatureIsOnAgain()
    {
        var features = new Features("repository-devbook");
        var catalog = new SurfaceCatalog(
            [
                Surface("tasks", ShellSurfaceGroup.View, 10),
                Surface("devbook", ShellSurfaceGroup.Takeover, 10, "repository-devbook"),
            ],
            features);

        Assert.Equal(["tasks"], catalog.Surfaces.Select(surface => surface.Id));
        Assert.False(catalog.TryGet("devbook", out _));

        features.Disabled.Clear();

        Assert.Equal(["tasks", "devbook"], catalog.Surfaces.Select(surface => surface.Id));
        Assert.True(catalog.TryGet("devbook", out var devbook));
        Assert.Equal("devbook", devbook.Id);
    }

    [Fact]
    public void ASecondSurfaceWithAnIdAlreadyRegistered_IsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new SurfaceCatalog(
            [
                Surface("devbook", ShellSurfaceGroup.Takeover, 10),
                new ShellSurface("devbook", "Other", ShellSurfaceGroup.Takeover, 20, typeof(Second)),
            ],
            new Features()));

        Assert.Contains("'devbook'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownId_IsNotAvailable()
    {
        var catalog = new SurfaceCatalog([Surface("devbook", ShellSurfaceGroup.Takeover, 10)], new Features());

        Assert.False(catalog.TryGet("nowhere", out var surface));
        Assert.Null(surface);
    }

    [Fact]
    public void TheReservedProposalsId_IsNotAvailable_WhileNothingRegistersIt()
    {
        var catalog = new SurfaceCatalog([Surface("devbook", ShellSurfaceGroup.Takeover, 10)], new Features());

        Assert.False(catalog.TryGet(ShellSurface.ProposalsId, out _));
        Assert.DoesNotContain(catalog.Surfaces, surface => surface.Id == ShellSurface.ProposalsId);
    }

    [Fact]
    public void TheNavigator_RaisesTheRequestWithItsQuery_AndAnEmptyQueryWhenNoneIsGiven()
    {
        var navigator = new SurfaceNavigator();
        var requests = new List<SurfaceRequest>();
        navigator.ShowRequested += requests.Add;

        navigator.Show("devbook", new Dictionary<string, string> { ["chapter"] = "arc42/adr/0040.md" });
        navigator.Show("tasks");

        Assert.Collection(
            requests,
            first =>
            {
                Assert.Equal("devbook", first.SurfaceId);
                Assert.Equal("arc42/adr/0040.md", first.Value("chapter"));
            },
            second =>
            {
                Assert.Equal("tasks", second.SurfaceId);
                Assert.Empty(second.Query);
                Assert.Null(second.Value("entry"));
            });
    }

    private sealed class Features(params string[] disabled) : IAppFeatureSettings
    {
        public HashSet<string> Disabled { get; } = new(disabled, StringComparer.Ordinal);

        public event Action? Changed { add { } remove { } }

        public AppFeatureSettings Current => new();

        public string SettingsPath => string.Empty;

        public bool IsEnabled(string key) => !Disabled.Contains(key);

        public string? SetEnabled(string key, bool enabled) => null;
    }
}
