using Backlog.Desktop.UI.Devbook;
using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.UI.Components.Diagrams;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// Every service type the two desktop heads registered in their inline Devbook blocks
/// before issue #738 moved them behind <c>AddDevbookModule</c>, and the open-chapter
/// mirror <c>AddDevbookAiContentSource</c> adds beside them. Diffed from both blocks:
/// the heads registered the same set, and differed only in how two of them were built
/// and how long one of them lived.
/// </summary>
internal static class DevbookModuleServices
{
    public static IReadOnlyList<Type> All { get; } =
    [
        typeof(IDevbookSnapshotCache),
        typeof(DevbookDatabaseRefresher),
        typeof(IDevbookFolderSource),
        typeof(DesignDevbookProvider),
        typeof(AiDevbookProvider),
        typeof(TechnologyDevbookService),
        typeof(DevbookAtlasService),
        typeof(IDevbookSearch),
        typeof(IDevbookVectorSearch),
        typeof(IDevbookReferenceResolver),
        typeof(InstructionSourceDiscovery),
        typeof(DevbookMenu),
        typeof(DevbookCopilotCli),
        typeof(IDiagramArtifactSource),
        typeof(DevbookScope),
        typeof(DevbookUpdateService),
        typeof(DevbookSourceSelection),
        typeof(DevbookFolderOpenService),
        typeof(Arc42DevbookStore),
        typeof(C4DevbookStore),
        typeof(DevbookChapterWriter),
        typeof(DomainDevbookStore),
        typeof(IDevbookAnnotationStore),
        typeof(DevbookOpenChapter)
    ];
}
