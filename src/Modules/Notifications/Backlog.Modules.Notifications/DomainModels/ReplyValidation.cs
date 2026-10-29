using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Notifications.DomainModels;

/// <summary>
/// Whether a reply fits the question's <see cref="AnswerSpec"/>
/// (<c>.devbook/domain/notifications/domain.invariants.md#invariant-a-reply-fits-the-answer-spec</c>
/// and <c>#invariant-a-choice-that-takes-text-comes-with-text</c>): only choice ids the spec
/// offers, within its count limits, and text only where the spec allows it — as the open answer
/// instead of a choice, or as the words of a picked choice that
/// <see cref="AnswerChoice.TakesText"/> — and within its length. A choice that takes text comes
/// with non-empty text. A note only where the spec takes one, within its length and beside an
/// answer; the note is never the choice's words. A reply that does not fit is refused with
/// <c>reply.invalid</c> and its reason, and the viewer can reply again.
/// </summary>
internal static class ReplyValidation
{
    public static Result Validate(AnswerSpec spec, IReadOnlyList<string>? choiceIds, string? text, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (!string.IsNullOrWhiteSpace(note))
        {
            if (spec.Note is null) return Invalid("This question takes no note.");
            if (note.Trim().Length > spec.Note.MaxLength)
                return Invalid($"The note is too long: at most {spec.Note.MaxLength} characters.");
        }

        var ids = choiceIds ?? [];
        var words = string.IsNullOrWhiteSpace(text) ? null : text;
        var choices = spec.Choices ?? [];

        if (ids.Count == 0)
        {
            if (words is not null)
            {
                if (spec.Open is null) return Invalid("This question takes no open answer.");

                return TooLong(words, spec.Open.MaxLength) ?? Result.Success();
            }

            var min = spec.AllowMultiple ? spec.MinChoices ?? 1 : 1;
            return min == 0 ? Result.Success() : Invalid("The reply holds no answer.");
        }

        var picked = new List<AnswerChoice>(ids.Count);
        foreach (var id in ids)
        {
            var choice = choices.FirstOrDefault(choice => string.Equals(choice.Id, id, StringComparison.Ordinal));
            if (choice is null) return Invalid("The reply picks a choice the question does not offer.");
            picked.Add(choice);
        }

        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            return Invalid("The reply picks the same choice twice.");

        var takesText = picked.Find(choice => choice.TakesText);

        if (takesText is not null)
        {
            if (words is null) return Invalid($"“{takesText.Label}” takes your words with it.");

            if (TooLong(words, spec.Open?.MaxLength ?? OpenAnswer.DefaultMaxLength) is { } tooLong) return tooLong;
        }
        else if (words is not null)
        {
            return Invalid("A reply picks a choice or gives an open answer, not both.");
        }

        if (!spec.AllowMultiple)
        {
            return ids.Count == 1 ? Result.Success() : Invalid("The question takes exactly one choice.");
        }

        var least = spec.MinChoices ?? 1;
        var most = spec.MaxChoices ?? choices.Count;

        return ids.Count < least || ids.Count > most
            ? Invalid($"The question takes {least} to {most} choices.")
            : Result.Success();
    }

    private static Result? TooLong(string words, int maxLength) =>
        words.Length > maxLength ? Invalid($"The answer is too long: at most {maxLength} characters.") : null;

    private static Result Invalid(string reason) => Result.Failure(NotificationsErrors.ReplyInvalid(reason));
}
