namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Where a popped-out window of a surface last stood (local ADR 0040 §5): its restored
/// rectangle in screen pixels, and whether it was maximised over it. A maximised window
/// keeps the rectangle it restores to, so the next one opens maximised on that monitor
/// and un-maximises to where the last one would have.
/// </summary>
public sealed record ShellWindowBounds(int X, int Y, int Width, int Height, bool Maximized)
{
    /// <summary>The smallest window <see cref="ClampTo"/> leaves: below it a window
    /// has no room for its own header.</summary>
    public const int MinimumSize = 200;

    /// <summary>Whether the rectangle has a size a window can open at.</summary>
    public bool HasArea => Width > 0 && Height > 0;

    /// <summary>
    /// These bounds moved onto a monitor that is connected now. Monitors are given by
    /// their work areas, the primary one first.
    /// <list type="bullet">
    /// <item>The window goes to the monitor it overlaps most, or — when it overlaps
    /// none, because the monitor it was on is gone — to the primary one.</item>
    /// <item>It shrinks to fit that monitor's work area, no smaller than
    /// <see cref="MinimumSize"/> where the work area allows, and moves inside it.</item>
    /// <item>Maximised stays as it was: the window maximises on the monitor it lands on.</item>
    /// </list>
    /// Null when no monitor with a work area is given, or when the bounds have no size: the window then
    /// opens wherever the system puts a new one.
    /// </summary>
    public ShellWindowBounds? ClampTo(IReadOnlyList<ScreenArea> workAreas)
    {
        ArgumentNullException.ThrowIfNull(workAreas);
        ScreenArea[] usable = [.. workAreas.Where(area => area.Width > 0 && area.Height > 0)];
        if (usable.Length == 0 || !HasArea) return null;

        var target = usable[0];
        long best = 0;
        foreach (var area in usable)
        {
            var overlap = Overlap(area);
            if (overlap > best)
            {
                best = overlap;
                target = area;
            }
        }

        var width = Math.Min(Math.Max(Width, MinimumSize), target.Width);
        var height = Math.Min(Math.Max(Height, MinimumSize), target.Height);
        var x = Math.Clamp(X, target.X, target.X + target.Width - width);
        var y = Math.Clamp(Y, target.Y, target.Y + target.Height - height);

        return new ShellWindowBounds(x, y, width, height, Maximized);
    }

    private long Overlap(ScreenArea area)
    {
        var width = Math.Min((long)X + Width, (long)area.X + area.Width) - Math.Max(X, area.X);
        var height = Math.Min((long)Y + Height, (long)area.Y + area.Height) - Math.Max(Y, area.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }
}

/// <summary>A monitor's work area in screen pixels: the desktop less its taskbars.</summary>
public readonly record struct ScreenArea(int X, int Y, int Width, int Height);
