using Backlog.Infrastructure.Sqlite.Events;
using Backlog.Infrastructure.Sqlite.Inbox;
using Backlog.Infrastructure.Sqlite.Roadmap;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// A journalled table's triggers are set up by whichever connection first opens an
/// unstamped file, outside any transaction, so several connections opening a fresh
/// <c>backlog.db</c> at once all run the same <c>DROP</c> and <c>CREATE TRIGGER</c>
/// batch (local ADR 0061 §2). None of them may fail because another created the
/// trigger first.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteJournalSetupRaceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Connections_setting_up_a_fresh_file_at_once_all_open_it()
    {
        for (var round = 0; round < 10; round++)
        {
            using var temp = new TemporaryDirectory("backlog-journal-setup-race");

            using var start = new ManualResetEventSlim(false);
            var opens = Enumerable.Range(0, 12).Select(index => Task.Run(async () =>
            {
                start.Wait(Token);
                switch (index % 4)
                {
                    case 0: await new SqliteRoadmapPlanRepository(temp.Path).LoadAsync(Token); break;
                    case 1: await new SqliteTaskRepository(temp.Path).ListAsync(Token); break;
                    case 2: await new SqliteInboxRepository(temp.Path).ListAsync(Token); break;
                    default: await new SqliteKnowledgeEventRepository(temp.Path).ListAsync(Token); break;
                }
            }, Token)).ToList();

            start.Set();
            await Task.WhenAll(opens);
        }
    }
}
