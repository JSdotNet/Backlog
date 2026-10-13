namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The test classes that record a Brain folder. <c>DevbookDatabaseLocation</c>
/// holds one Brain root for the process — recording one replaces the last — so
/// two classes recording their own at once would each see the other's, and a
/// build would run under a repository's convention instead of the Brain's. One
/// collection runs them one after the other.
/// </summary>
[CollectionDefinition(Name)]
public sealed class BrainRootCollection
{
    public const string Name = "Brain root";
}
