using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Notifications.DomainModels;

/// <summary>
/// Whether a reply fits the question's <see cref="AnswerSpec"/>
/// (<c>.devbook/domain/notifications/domain.invariants.md#invariant-a-reply-fits-the-answer-spec</c>):
/// only choice ids the spec offers, within its count limits, and open text only where the spec
/// allows it and within its length, and a note only where the spec takes one, within its length
/// and beside an answer. A reply that does not fit is refused with
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
        var open = string.IsNullOrWhiteSpace(text) ? null : text;

        if (ids.Count > 0 && open is not null) return Invalid("A reply picks a choice or gives an open answer, not both.");

        if (open is not null)
        {
            if (spec.Open is null) return Invalid("This question takes no open answer.");

            return open.Length > spec.Open.MaxLength
                ? Invalid($"The answer is too long: at most {spec.Open.MaxLength} characters.")
                : Result.Success();
        }

        var choices = spec.Choices ?? [];
        var min = spec.AllowMultiple ? spec.MinChoices ?? 1 : 1;

        if (ids.Count == 0)
        {
            return min == 0 ? Result.Success() : Invalid("The reply holds no answer.");
        }

        if (ids.Any(id => !choices.Any(choice => string.Equals(choice.Id, id, StringComparison.Ordinal))))
            return Invalid("The reply picks a choice the question does not offer.");

        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            return Invalid("The reply picks the same choice twice.");

        if (!spec.AllowMultiple)
        {
            return ids.Count == 1 ? Result.Success() : Invalid("The question takes exactly one choice.");
        }

        var max = spec.MaxChoices ?? choices.Count;

        return ids.Count < min || ids.Count > max
            ? Invalid($"The question takes {min} to {max} choices.")
            : Result.Success();
    }

    private static Result Invalid(string reason) => Result.Failure(NotificationsErrors.ReplyInvalid(reason));
}
