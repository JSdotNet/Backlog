namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Tests that measure when the store's own files were last written, and must
/// therefore not share a process with tests doing unrelated work at the same time.
///
/// <para>The list decides whether to reload by comparing the newest timestamp
/// across <c>backlog.db</c> and its WAL sidecars against the one it last read at.
/// <c>TasksDesktopState.ReloadRowsAsync</c> deliberately takes that stamp
/// <em>before</em> it reads the rows — the comment there says why: a write landing
/// mid-reload must be seen again on the next check rather than recorded as
/// something the list already has. The cost of that trade is that the store's own
/// read closes a SQLite connection after the stamp was taken, and closing one
/// touches <c>-wal</c> and <c>-shm</c>. The design permits the spurious reload that
/// follows; what it forbids is a missed write.</para>
///
/// <para>So "nothing changed" is only observable while nothing else is competing
/// for the thread pool. Run these alone and the teardown lands before the next
/// poll every time; run them beside enough other work and it sometimes does not,
/// and a test that asserts no reload fails for a reason that is not about the
/// store. That is a property of the tests rather than of the list, which is why
/// this is a collection and not a change to what they assert.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StoreTimestampCollection
{
    public const string Name = "Store file timestamps";
}
