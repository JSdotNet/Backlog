using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.ColourBand;

/// <summary>
/// Gives a repository's band one of the sanctioned colours, or takes the choice back.
/// </summary>
/// <param name="Alias">The repository, by the alias the plan files work under.</param>
/// <param name="Colour">Which of the approved colours, 1 through 5, or null to stop
/// choosing and let the view place this band again. Never a colour value: which hue
/// each number is belongs to the stylesheet, and a plan that stored one would be a
/// plan inventing a colour.</param>
public sealed record ColourBandCommand(string Alias, int? Colour);

public sealed class ColourBandCommandHandler(IRoadmapPlanRepository plans)
    : ICommandHandler<ColourBandCommand, Result>
{
    public async Task<Result> Handle(ColourBandCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var plan = await plans.LoadAsync(cancellationToken);
        var coloured = plan.ColourBand(command.Alias, command.Colour);

        if (coloured.IsFailure) return coloured;

        await plans.SaveAsync(plan, cancellationToken);
        return Result.Success();
    }
}
