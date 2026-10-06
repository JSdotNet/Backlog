using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Capture.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The run's one memory, across a restart: whether a target has had its first
/// look, and what was passed over there.
/// </summary>
public sealed class CaptureTargetLedgerStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"capture-ledger-{Guid.NewGuid():N}");

    private string LedgerPath => Path.Combine(_directory, "capture-targets.json");

    [Fact]
    public void A_target_never_recorded_has_had_no_first_look()
    {
        var store = new CaptureTargetLedgerStore(LedgerPath);

        Assert.Null(store.PassedOverAt(CaptureSourceKind.Website, "https://aspire.dev"));
    }

    [Fact]
    public void What_was_passed_over_survives_a_restart_per_kind_and_target()
    {
        var passed = new[] { Guid.NewGuid(), Guid.NewGuid() };
        new CaptureTargetLedgerStore(LedgerPath).Record(CaptureSourceKind.Website, "https://aspire.dev", passed);

        var reopened = new CaptureTargetLedgerStore(LedgerPath);

        Assert.Equal(passed.ToHashSet(), reopened.PassedOverAt(CaptureSourceKind.Website, " https://aspire.dev ")!.ToHashSet());
        Assert.Null(reopened.PassedOverAt(CaptureSourceKind.YouTube, "https://aspire.dev"));
        Assert.Null(reopened.PassedOverAt(CaptureSourceKind.Website, "https://aspire.dev/Blog"));
    }

    /// <summary>A target recorded with nothing passed over has still had its
    /// first look — empty is an answer, null is not.</summary>
    [Fact]
    public void A_target_with_nothing_passed_over_is_still_known()
    {
        new CaptureTargetLedgerStore(LedgerPath).Record(CaptureSourceKind.Website, "https://aspire.dev", []);

        Assert.Empty(new CaptureTargetLedgerStore(LedgerPath).PassedOverAt(CaptureSourceKind.Website, "https://aspire.dev")!);
    }

    [Fact]
    public void A_corrupt_ledger_opens_as_nothing_remembered()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(LedgerPath, "{ not json");

        Assert.Null(new CaptureTargetLedgerStore(LedgerPath).PassedOverAt(CaptureSourceKind.Website, "https://aspire.dev"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
