namespace Backlog.UI.Components.Menus;

/// <summary>
/// One keyboard shortcut a pager's host binds, as the pager prints it.
///
/// <para>The hint belongs to the host rather than to the pager: the same pager
/// reached by arrow keys on one surface and by <c>[</c>/<c>]</c> on another would
/// be telling one of them something untrue if the keys were fixed here.</para>
/// </summary>
/// <param name="Keys">The keys as a reader presses them, e.g. <c>↑ ↓</c>.</param>
/// <param name="Action">What they do, in the host's own words.</param>
public readonly record struct RecordPagerHint(string Keys, string Action);
