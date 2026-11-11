using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// Tasks' <see cref="IBrainAreaNames"/>, answered from the Brain's folder: the areas
/// it shows, declared then discovered (<see cref="IBrainFolderSource.Areas"/>), by
/// name. No Brain composed, no Brain set, or a folder that is not there, is no area
/// to suggest.
/// </summary>
public sealed class ShellBrainAreaNames(IBrainFolderSource? brain) : IBrainAreaNames
{
    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default) =>
        brain is null
            ? Task.FromResult<IReadOnlyList<string>>([])
            : Task.Run<IReadOnlyList<string>>(
                () => brain.BrainFolderPresent()
                ? [.. brain.Areas().Select(area => area.Name).Where(name => !string.IsNullOrWhiteSpace(name))]
                : [],
            cancellationToken);
}
