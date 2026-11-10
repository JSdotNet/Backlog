using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Devbook;

/// <summary>
/// Whether the chapters say a domain event is sent, read one way wherever it is
/// shown: Model's Events marks it Emitted or Documented, a unit's events say "Not
/// sent yet." (local ADR 0047). Read from what the chapters already write — no
/// <c>status</c> is asked of them (§8).
/// </summary>
internal static partial class DomainEventSending
{
    /// <summary>Whether a chapter says its context publishes no events yet, which
    /// it then says of every event.</summary>
    internal static bool PublishesNone(string text) => PublishesNoneSentence().IsMatch(text);

    /// <summary>Whether a chapter says an event is not raised yet.</summary>
    internal static bool NotEmitted(string text) => NotEmittedSentence().IsMatch(text);

    /// <summary>
    /// Sent only when a chapter says so — a raiser emits or publishes
    /// <paramref name="eventName"/> by name — and neither the event's chapter nor a
    /// raiser's says it is not raised yet. A context that publishes none is the
    /// caller's to read first, over every chapter of the context.
    /// </summary>
    internal static bool IsSent(string eventName, string eventText, IEnumerable<string> raiserTexts)
    {
        var raisers = raiserTexts as IReadOnlyCollection<string> ?? [.. raiserTexts];
        if (NotEmitted(eventText) || raisers.Any(NotEmitted)) return false;

        var emits = new Regex($@"\b(emits|emitting|publishes|publishing|raises|raising)\b[^.]*?\b{Regex.Escape(eventName)}\b", RegexOptions.IgnoreCase);
        return raisers.Any(emits.IsMatch);
    }

    [GeneratedRegex(@"publishes no (domain )?events", RegexOptions.IgnoreCase)]
    private static partial Regex PublishesNoneSentence();

    [GeneratedRegex(@"documented rather than emitted|\bnot (yet )?(emitted|published|raised)\b|\b(emitted|published|raised) yet\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotEmittedSentence();
}
