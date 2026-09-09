namespace Backlog.UI.Components.Selects;

/// <summary>One stretch of an option list that renders together: a named section,
/// or the unsectioned options between two of them. <c>Ordinal</c> numbers the named
/// runs and leaves the rest at <c>-1</c>, because it exists to name the label element
/// a section points at and an unsectioned run has no label to name.
/// <para>
/// <c>Start</c> and <c>End</c> index back into the list the runs were taken from
/// rather than carrying the options themselves, so a control whose option identity is
/// its index — <c>TagMultiSelect</c>'s reading cursor — keeps that index across a
/// section boundary.
/// </para></summary>
internal readonly record struct OptionRun(string? Name, int Ordinal, int Start, int End);

/// <summary>
/// Splits an option list into the sections a control draws it in.
///
/// <para>Sections are <em>runs</em>, walked in order: the first option whose group
/// differs from the one before it opens a new one. Not a <c>GroupBy</c>. That would
/// coalesce two non-adjacent runs of the same name into one, which is the control
/// quietly deciding an ordering — and the host owns the order, as
/// <see cref="SelectorOption.Group"/> says. A control that re-derived it would be a
/// second definition of the order, free to disagree with the first.</para>
///
/// <para>An option with no group opens a run of its own kind and is left exactly
/// where the host put it, never hoisted to the top. Giving it a section would mean
/// inventing a name the host did not supply, and hoisting it would move it past
/// options the host deliberately ordered it after. So a list nobody grouped comes
/// back as one nameless run and renders precisely the markup it rendered before
/// groups existed.</para>
///
/// <para>Here rather than in a component because three controls draw the same
/// sections out of the same list — <c>SelectField</c> and <c>BadgeSelect</c> as
/// native <c>optgroup</c>s, <c>TagMultiSelect</c> as labelled groups in its own
/// listbox. Three copies of a run-walker is how the three of them stop agreeing on
/// what a section is.</para>
/// </summary>
internal static class SelectorOptionRuns
{
    public static List<OptionRun> Of(IReadOnlyList<SelectorOption> options)
    {
        var runs = new List<OptionRun>();
        var ordinal = 0;

        for (var index = 0; index < options.Count;)
        {
            var name = options[index].Group;
            var start = index;

            while (index < options.Count && string.Equals(options[index].Group, name, StringComparison.Ordinal))
            {
                index++;
            }

            runs.Add(new OptionRun(name, name is null ? -1 : ordinal++, start, index));
        }

        return runs;
    }
}
