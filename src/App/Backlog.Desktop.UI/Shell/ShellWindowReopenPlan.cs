using Backlog.Infrastructure.FileSystem;
using Backlog.SharedKernel;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The remembered windows to reopen at start, and the ones to drop (local ADR 0043 §5).
/// A pure answer, so the head's window code only acts on it: what reopens is decided
/// where a test can reach it.
/// </summary>
/// <param name="Open">Every remembered window whose surface the catalog offers now, in
/// the order they were remembered.</param>
/// <param name="Drop">The keys of the remembered windows whose surface the catalog no
/// longer offers — its flag off, or its module gone — which are forgotten rather than
/// tried again at every start.</param>
public sealed record ShellWindowReopenPlan(IReadOnlyList<RememberedShellWindow> Open, IReadOnlyList<string> Drop)
{
    /// <summary>Plans the reopen of <paramref name="remembered"/> against what
    /// <paramref name="catalog"/> offers.</summary>
    public static ShellWindowReopenPlan For(IEnumerable<RememberedShellWindow> remembered, ISurfaceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(remembered);
        ArgumentNullException.ThrowIfNull(catalog);

        var open = new List<RememberedShellWindow>();
        var drop = new List<string>();
        foreach (var window in remembered)
        {
            if (catalog.TryGet(window.SurfaceId, out _)) open.Add(window);
            else drop.Add(window.Key);
        }

        return new ShellWindowReopenPlan(open, drop);
    }
}
