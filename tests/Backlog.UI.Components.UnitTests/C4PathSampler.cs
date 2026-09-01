using System.Globalization;
using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Points along an edge's path, whatever shape the layout gave it.
/// <para>
/// The tests that check no line is drawn through a card work by walking the path and
/// looking at where it goes, so they are only as good as this. An earlier version read
/// eight numbers and gave up on anything else, which was fine while every edge was a
/// single cubic — and then quietly reported success for every routed edge the moment
/// routing started emitting lines and corners. A test that skips what it cannot parse
/// passes hardest on exactly the cases it was written for.
/// </para>
/// </summary>
internal static class C4PathSampler
{
    private const int PerSegment = 24;

    public static IEnumerable<(double X, double Y)> Along(string path)
    {
        var at = (X: 0.0, Y: 0.0);

        foreach (Match command in Regex.Matches(path, "([MLQC])([^MLQC]*)"))
        {
            var n = Regex.Matches(command.Groups[2].Value, @"-?\d+(\.\d+)?")
                .Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture))
                .ToArray();

            switch (command.Groups[1].Value)
            {
                case "M" when n.Length >= 2:
                    at = (n[0], n[1]);
                    yield return at;
                    break;

                case "L" when n.Length >= 2:
                    foreach (var point in Line(at, (n[0], n[1]))) yield return point;
                    at = (n[0], n[1]);
                    break;

                case "Q" when n.Length >= 4:
                    foreach (var point in Quadratic(at, (n[0], n[1]), (n[2], n[3]))) yield return point;
                    at = (n[2], n[3]);
                    break;

                case "C" when n.Length >= 6:
                    foreach (var point in Cubic(at, (n[0], n[1]), (n[2], n[3]), (n[4], n[5]))) yield return point;
                    at = (n[4], n[5]);
                    break;
            }
        }
    }

    private static IEnumerable<(double X, double Y)> Line((double X, double Y) a, (double X, double Y) b)
    {
        for (var step = 1; step <= PerSegment; step++)
        {
            var t = step / (double)PerSegment;
            yield return (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }
    }

    private static IEnumerable<(double X, double Y)> Quadratic(
        (double X, double Y) a,
        (double X, double Y) control,
        (double X, double Y) b)
    {
        for (var step = 1; step <= PerSegment; step++)
        {
            var t = step / (double)PerSegment;
            var u = 1 - t;

            yield return (
                u * u * a.X + 2 * u * t * control.X + t * t * b.X,
                u * u * a.Y + 2 * u * t * control.Y + t * t * b.Y);
        }
    }

    private static IEnumerable<(double X, double Y)> Cubic(
        (double X, double Y) a,
        (double X, double Y) c1,
        (double X, double Y) c2,
        (double X, double Y) b)
    {
        for (var step = 1; step <= PerSegment; step++)
        {
            var t = step / (double)PerSegment;
            var u = 1 - t;

            yield return (
                u * u * u * a.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * b.X,
                u * u * u * a.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * b.Y);
        }
    }
}
