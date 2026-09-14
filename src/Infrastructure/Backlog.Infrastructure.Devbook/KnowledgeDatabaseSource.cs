using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Infrastructure.Knowledge;

/// <summary>
/// Opens the knowledge database for a repository scope, given only the port every
/// knowledge consumer already has.
///
/// <para>The awkwardness this absorbs is real and is not going away: a consumer
/// is handed a knowledge <i>folder</i> — the absolute path of <c>.domain</c>, say
/// — because <c>IKnowledgeFolderSource</c> resolves an area per registered
/// repository, while the database is one file at the repository root above them
/// all. <see cref="KnowledgeDatabaseLocation"/> does that one step up; this does
/// the step before it, which is deciding <i>which</i> folder to step up from.</para>
///
/// <para>Every enabled folder is tried rather than one being nominated, because
/// <c>instructions</c> resolves into <c>.github</c> — a directory deeper than the
/// folders that sit at the root — and going up from it lands somewhere with no
/// database in it. The atlas discovered that first and its
/// <c>ResolveGraphPath</c> says the same thing about the JSON; putting the walk
/// here means the next consumer inherits the answer rather than rediscovering
/// it.</para>
///
/// <para><see langword="null"/> is an ordinary answer: no repository, no
/// database, a rebuild in flight, or a <c>schemaVersion</c> this reader does not
/// know. What a caller does with it depends on what it is for — a browsing
/// consumer reads Markdown, and retrieval says search is unavailable.</para>
/// </summary>
public static class KnowledgeDatabaseSource
{
    public static KnowledgeDatabase? TryOpen(IKnowledgeFolderSource folders, string? repositoryAlias)
    {
        ArgumentNullException.ThrowIfNull(folders);

        foreach (var setting in folders.Folders(repositoryAlias))
        {
            if (!setting.Enabled) continue;

            var location = folders.Resolve(setting.Key, repositoryAlias);
            if (location is not { Available: true, FullPath: { Length: > 0 } path }) continue;

            if (KnowledgeDatabase.TryOpenForFolder(path) is { } database) return database;
        }

        return null;
    }
}
