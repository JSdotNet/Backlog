using System.Globalization;

namespace Backlog.Modules.Brain.Abstractions;

/// <summary>
/// The Brain notes whose <c>review</c> date has come (local ADR 0054 §6 and §8,
/// <c>.devbook/domain/brain/domain.md#knowledge-note</c>): the date a note's top
/// <c>meta</c> block names for the weekly review to look at it again. Read from the
/// Brain's own database in the owner's app storage, like <see cref="IBrainTaskLinks"/>,
/// and never written: no writer of the Brain adds or changes the field.
/// <para>
/// With no Brain set, or no database built for it yet, the answer is
/// <see cref="BrainReviewsDue.None"/> — the same answer as a Brain where no review
/// date has come.
/// </para>
/// </summary>
public interface IBrainReviewDates
{
    /// <summary>
    /// The notes whose review date is <paramref name="today"/> or earlier, oldest
    /// date first, and every note whose <c>review</c> value is no date — a warning
    /// to show beside them, never a failure.
    /// </summary>
    Task<BrainReviewsDue> DueAsync(DateOnly today, CancellationToken cancellationToken = default);
}

/// <summary>What <see cref="IBrainReviewDates.DueAsync"/> answers.</summary>
/// <param name="Due">The notes whose review date has come, oldest date first, then by path.</param>
/// <param name="Unreadable">The notes whose <c>review</c> value is no <c>YYYY-MM-DD</c> date, by path.</param>
public sealed record BrainReviewsDue(IReadOnlyList<BrainReviewNote> Due, IReadOnlyList<BrainReviewWarning> Unreadable)
{
    /// <summary>No note is due and none is unreadable.</summary>
    public static BrainReviewsDue None { get; } = new([], []);
}

/// <summary>A Brain note whose review date has come.</summary>
/// <param name="NotePath">The note's path in the Brain — <c>areas/health.md</c>.</param>
/// <param name="Title">The note's title.</param>
/// <param name="Review">The date its <c>review</c> field names.</param>
public sealed record BrainReviewNote(string NotePath, string Title, DateOnly Review);

/// <summary>A Brain note whose <c>review</c> value does not read as a date.</summary>
/// <param name="NotePath">The note's path in the Brain.</param>
/// <param name="Title">The note's title.</param>
/// <param name="Value">The value as the note writes it.</param>
public sealed record BrainReviewWarning(string NotePath, string Title, string Value)
{
    /// <summary>The warning in words, naming the note and what it wrote.</summary>
    public string Message =>
        $"{NotePath}: review \"{Value}\" is not a YYYY-MM-DD date, so the note is left out of the review.";
}

/// <summary>The <c>review</c> field of a Brain note and how its value reads.</summary>
public static class BrainReviewDate
{
    /// <summary>The field's name in a note's top <c>meta</c> block.</summary>
    public const string Field = "review";

    /// <summary>
    /// The value as a date: <c>YYYY-MM-DD</c> exactly, once the quotes and blanks a
    /// YAML scalar may leave are trimmed; anything else — another order, a missing
    /// zero, a date that does not exist, a list — is null.
    /// </summary>
    public static DateOnly? Parse(string? value)
    {
        var text = value?.Trim().Trim('"', '\'').Trim();
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
