using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Notifications.DomainModels;

/// <summary>
/// Whether an <see cref="AnswerSpec"/> is one of the four shapes
/// <c>.devbook/domain/notifications/domain.md#answer-spec</c> allows — single choice, multiple
/// choice, open, or choices with open — and nothing else.
/// </summary>
internal static class AnswerSpecRules
{
    /// <summary>The fewest set choices a question with choices offers.</summary>
    public const int MinimumChoices = 2;

    /// <summary>The most set choices a question offers.</summary>
    public const int MaximumChoices = 8;

    public static Result Validate(AnswerSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var choices = spec.Choices ?? [];

        if (!Enum.IsDefined(spec.AnswerOn)) return Invalid("A question is answered on any instance or on the owner only.");
        if (spec.Open is { MaxLength: < 1 }) return Invalid("An open answer's length limit is at least one character.");
        if (spec.Note is { MaxLength: < 1 }) return Invalid("A note's length limit is at least one character.");

        if (choices.Count == 0)
        {
            if (spec.Open is null) return Invalid("A question offers set choices, an open answer, or both.");
            if (spec.AllowMultiple || spec.MinChoices is not null || spec.MaxChoices is not null)
                return Invalid("An open question has no choices to pick several of.");

            return Result.Success();
        }

        if (choices.Count is < MinimumChoices or > MaximumChoices)
            return Invalid($"A question offers {MinimumChoices} to {MaximumChoices} set choices.");

        if (choices.Any(choice => choice is null || string.IsNullOrWhiteSpace(choice.Id) || string.IsNullOrWhiteSpace(choice.Label)))
            return Invalid("Every choice has an id and a label.");

        if (choices.Select(choice => choice.Id).Distinct(StringComparer.Ordinal).Count() != choices.Count)
            return Invalid("Every choice has its own id.");

        if (choices.Any(choice => !Enum.IsDefined(choice.Style)))
            return Invalid("A choice is drawn as default, primary or destructive.");

        if (!spec.AllowMultiple)
        {
            return spec.MinChoices is null && spec.MaxChoices is null
                ? Result.Success()
                : Invalid("Only a multiple-choice question sets how many choices to pick.");
        }

        if (spec.Open is not null) return Invalid("A multiple-choice question takes no open answer.");

        var min = spec.MinChoices ?? 1;
        var max = spec.MaxChoices ?? choices.Count;

        return min < 0 || max < 1 || min > max || max > choices.Count
            ? Invalid("A multiple-choice question picks between its minimum and maximum, within its choices.")
            : Result.Success();
    }

    private static Result Invalid(string reason) => Result.Failure(NotificationsErrors.InvalidAnswerSpec(reason));
}
