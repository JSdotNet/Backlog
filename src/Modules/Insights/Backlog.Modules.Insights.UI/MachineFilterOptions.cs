using Backlog.Modules.DevPc.Abstractions;
using Backlog.UI.Components.Selects;

namespace Backlog.Modules.Insights.UI;

/// <summary>
/// The machine filter's options, from Dev PCs' machines (local ADR 0073 §7).
/// </summary>
/// <remarks>
/// The value is the machine's id and the label its name: a filter whose value was the
/// name would merge two machines that share one, or split one that was renamed. Two
/// options under one name cannot be told apart in a list, though, so a name shared
/// (ignoring case) says which is which: this device, which the registry puts first,
/// says so, and every other carries the first eight characters of its id.
/// </remarks>
internal static class MachineFilterOptions
{
    public static IReadOnlyList<SelectorOption> From(IReadOnlyList<Machine> machines)
    {
        var shared = machines
            .GroupBy(machine => machine.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. machines.Select((machine, index) => new SelectorOption(
                machine.Id.ToString(),
                !shared.Contains(machine.Name)
                    ? machine.Name
                    : index == 0
                        ? $"{machine.Name} (this device)"
                        : $"{machine.Name} ({machine.Id.ToString()[..8]})"))
        ];
    }
}
