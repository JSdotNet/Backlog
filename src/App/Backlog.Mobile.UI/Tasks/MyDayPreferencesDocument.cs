using System.Text.Json;

using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Tasks;

/// <summary>
/// The My Day preferences as the phone last pulled them: the document a desktop writes
/// whole on the task feed (.devbook/arc42/adr/0032, §1 and §4), kept verbatim beside the
/// rows. Read-only here — the phone has no switch and pushes nothing, so it only reads
/// <see cref="ClearsOvernight"/> off it.
/// </summary>
/// <param name="UpdatedAt">The document's own last-write-wins stamp.</param>
/// <param name="ServerTimestamp">The replica's ordering stamp, which breaks a tie
/// between two copies with the same <paramref name="UpdatedAt"/>, as it does for a task
/// row.</param>
/// <param name="Content">The document's JSON, verbatim, unknown keys included.</param>
public sealed record MyDayPreferencesDocument(DateTimeOffset UpdatedAt, long ServerTimestamp, string Content)
{
    private const string ClearsOvernightKey = "clearsOvernight";

    /// <summary>Clear My Day overnight. On when the document does not say — the
    /// default, and the rule every build before the switch kept.</summary>
    public bool ClearsOvernight => !TryRead(Content, out var clearsOvernight) || clearsOvernight;

    /// <summary>
    /// Reads the switch off a document's JSON: an object whose <c>clearsOvernight</c>
    /// is a boolean, or absent, which reads on. Anything else is a document this build
    /// cannot read — not JSON, not an object, or a <c>clearsOvernight</c> that is not a
    /// boolean — and answers false, as the desktop's store calls it Unreadable.
    /// </summary>
    public static bool TryRead(string? content, out bool clearsOvernight)
    {
        clearsOvernight = true;
        if (string.IsNullOrWhiteSpace(content)) return false;

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty(ClearsOvernightKey, out var value)) return true;

            switch (value.ValueKind)
            {
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    clearsOvernight = false;
                    return true;
                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// The preferences document among <paramref name="records"/> that replaces
    /// <paramref name="current"/>, or null when none does. Ordered as
    /// <see cref="TaskFold.Wins"/> orders rows — the later <see cref="UpdatedAt"/>, then
    /// the higher <see cref="ServerTimestamp"/> — so the order pages arrive in does not
    /// change which copy the phone ends on. A copy this build cannot read is passed over
    /// and leaves the one kept alone; the desktop never writes a tombstone for it, so
    /// none is taken.
    /// </summary>
    public static MyDayPreferencesDocument? Fold(MyDayPreferencesDocument? current, IEnumerable<TaskChangeRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        MyDayPreferencesDocument? best = current;

        foreach (var record in records)
        {
            var change = record.Change;
            if (!string.Equals(change.Task.Type, TaskFold.MyDayPreferencesType, StringComparison.Ordinal)) continue;
            if (change.DeletedAt is not null || !TryRead(change.Task.ContentMd, out _)) continue;

            var incoming = new MyDayPreferencesDocument(change.UpdatedAt, record.ServerTimestamp, change.Task.ContentMd);
            if (best is null || Wins(incoming, best)) best = incoming;
        }

        return ReferenceEquals(best, current) ? null : best;
    }

    private static bool Wins(MyDayPreferencesDocument incoming, MyDayPreferencesDocument current) =>
        incoming.UpdatedAt != current.UpdatedAt
            ? incoming.UpdatedAt > current.UpdatedAt
            : incoming.ServerTimestamp > current.ServerTimestamp;
}
