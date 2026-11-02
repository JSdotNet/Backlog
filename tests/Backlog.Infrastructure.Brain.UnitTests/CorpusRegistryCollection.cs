namespace Backlog.Infrastructure.Brain.UnitTests;

/// <summary>
/// Tests that register a root in Devbook's corpus registry, which is
/// process-wide: one profile holds one root, so two such tests running at once
/// would replace each other's Brain folder under them.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CorpusRegistryCollection
{
    public const string Name = "Corpus registry";
}
