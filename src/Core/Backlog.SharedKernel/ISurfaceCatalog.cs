using System.Diagnostics.CodeAnalysis;

namespace Backlog.SharedKernel;

/// <summary>
/// Every <see cref="ShellSurface"/> registered in the container, as the shell offers
/// them (local ADR 0043 §1): ordered by group, then by <see cref="ShellSurface.Order"/>,
/// and without a surface whose feature is switched off.
/// </summary>
public interface ISurfaceCatalog
{
    /// <summary>The surfaces on offer now, views first, each group lowest
    /// <see cref="ShellSurface.Order"/> first. Read again after
    /// <see cref="IAppFeatureSettings.Changed"/>: a switched flag changes it.</summary>
    IReadOnlyList<ShellSurface> Surfaces { get; }

    /// <summary>
    /// The surface with this id, when it is registered and its feature is on.
    /// Answers false — not available — for an id nothing registered, for a surface
    /// whose feature is off, and for <see cref="ShellSurface.ProposalsId"/> until the
    /// pane it is reserved for registers.
    /// </summary>
    bool TryGet(string surfaceId, [NotNullWhen(true)] out ShellSurface? surface);
}

/// <summary>
/// The catalog over what the container holds. Refuses at construction a second
/// surface with an id already registered, so two modules cannot both claim one.
/// </summary>
public sealed class SurfaceCatalog : ISurfaceCatalog
{
    private readonly IReadOnlyList<ShellSurface> _registered;
    private readonly IAppFeatureSettings _features;

    public SurfaceCatalog(IEnumerable<ShellSurface> surfaces, IAppFeatureSettings features)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(features);

        var registered = surfaces.ToList();
        var duplicate = registered
            .GroupBy(surface => surface.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"The shell surface '{duplicate.Key}' is registered more than once. A surface id names "
                + "one surface; register it from one module only.");
        }

        _registered = [.. registered
            .OrderBy(surface => surface.Group)
            .ThenBy(surface => surface.Order)];
        _features = features;
    }

    public IReadOnlyList<ShellSurface> Surfaces => [.. _registered.Where(IsOffered)];

    public bool TryGet(string surfaceId, [NotNullWhen(true)] out ShellSurface? surface)
    {
        surface = _registered.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, surfaceId, StringComparison.Ordinal) && IsOffered(candidate));
        return surface is not null;
    }

    private bool IsOffered(ShellSurface surface) =>
        surface.FeatureKey is null || _features.IsEnabled(surface.FeatureKey);
}
