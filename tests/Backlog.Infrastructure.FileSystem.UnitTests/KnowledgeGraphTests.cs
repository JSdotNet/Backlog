using Backlog.Infrastructure.FileSystem.Roadmap;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Reading the generated <c>_meta/graph.json</c> down to what a rollup needs.
/// Parsing is a pure function of the text, so effort — now a JSON number — and the
/// <c>roadmap</c> array are asserted without a file behind them, and malformed input
/// reads as no nodes rather than throwing.
/// </summary>
public class KnowledgeGraphTests
{
    private const string OneNode = """
        {
          "elements": {
            "nodes": [
              {
                "data": {
                  "id": "roadmap/domain.md#aggregate-roadmap-plan",
                  "label": "Aggregate: Roadmap Plan",
                  "effort": 8,
                  "roadmap": ["roadmap-planning"]
                }
              }
            ]
          }
        }
        """;

    [Fact]
    public void EffortIsReadAsANumber()
    {
        var node = Assert.Single(KnowledgeGraph.Parse(OneNode));

        Assert.Equal("roadmap/domain.md#aggregate-roadmap-plan", node.Id);
        Assert.Equal("Aggregate: Roadmap Plan", node.Label);
        Assert.Equal(8, node.Effort);
        Assert.Equal(["roadmap-planning"], node.Roadmap);
    }

    [Fact]
    public void AStringEffortIsStillRead_SoAnOlderGraphKeepsWorking()
    {
        const string json = """
            { "elements": { "nodes": [ { "data": { "id": "a", "effort": "5" } } ] } }
            """;

        var node = Assert.Single(KnowledgeGraph.Parse(json));

        Assert.Equal(5, node.Effort);
    }

    [Fact]
    public void AMissingOrUnreadableEffortIsNull_NotZero()
    {
        const string json = """
            { "elements": { "nodes": [
              { "data": { "id": "none" } },
              { "data": { "id": "junk", "effort": "eight" } }
            ] } }
            """;

        var nodes = KnowledgeGraph.Parse(json);

        Assert.All(nodes, node => Assert.Null(node.Effort));
    }

    [Fact]
    public void ANodeWithNoRoadmapListContributesNoTags()
    {
        const string json = """
            { "elements": { "nodes": [ { "data": { "id": "a", "effort": 3 } } ] } }
            """;

        var node = Assert.Single(KnowledgeGraph.Parse(json));

        Assert.Empty(node.Roadmap);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{ "elements": {} }""")]
    public void MissingOrMalformedInputReadsAsNoNodes(string? json)
    {
        Assert.Empty(KnowledgeGraph.Parse(json));
    }
}
