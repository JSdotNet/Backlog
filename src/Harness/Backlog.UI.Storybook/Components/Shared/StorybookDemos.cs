using System.Reflection;

namespace Backlog.UI.Storybook.Components.Shared;

/// <summary>
/// devbook's sample click demo, embedded so <c>DemoView</c>'s story has a real demo
/// to frame wherever the storybook runs. See the README in <c>Shared/Demos</c>.
/// </summary>
internal static class StorybookDemos
{
    /// <summary>Where the sample sits in this repository, which is what the story
    /// shows under the frame.</summary>
    public const string SamplePath = "src/Harness/Backlog.UI.Storybook/Components/Shared/Demos/features.demo.html";

    /// <summary>The sample's walkthrough, which plays the declined-card scenario.</summary>
    public const string Walkthrough = "walkthrough/a-declined-card-keeps-the-basket";

    /// <summary>A screen state in the sample.</summary>
    public const string DeclinedScreen = "checkout-declined";

    private const string ResourceName = "demos/features.demo.html";

    private static readonly Lazy<string> Html = new(Read);

    public static string Sample => Html.Value;

    /// <summary>Fails loudly, for the reason <see cref="StorybookDiagramArtifacts"/>
    /// gives: a lost embed is a csproj fault, not a component one.</summary>
    private static string Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The storybook assembly does not embed '{ResourceName}'. {SamplePath} is meant to be an "
                + "EmbeddedResource with that LogicalName in Backlog.UI.Storybook.csproj.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
