using System.Diagnostics;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <c>window.backlogRevealHeading</c>, run rather than read.
/// <para>
/// The Devbook pane asks for it once per followed reference that names a heading
/// — a chapter followed out of the Lobes atlas, a Brain note's gold chip, an
/// entry's reference — because a rendered heading carries no id the browser could
/// scroll to. The script finds the heading by its text, slugged the way the devbook
/// generator slugs one, and keeps it at the top of the reading while the reading
/// settles. These run the shipped function in Node against a small document whose
/// chapter body arrives a few frames late, as it does when the body is read off
/// disk after the pane selects it, and whose panel shows the file it is leaving
/// under the new path first, as the arc42 panel does.
/// </para>
/// </summary>
public sealed class BacklogRevealHeadingScriptTests
{
    /// <summary>The heading lands at the top of the reading, its type mark left out
    /// of the text it is matched on.</summary>
    [Fact]
    public void A_heading_that_arrives_late_is_scrolled_to_the_top_of_the_reading()
    {
        var result = Run("""
            schedule(3, () => setBody(view('.devbook/arc42/01-introduction-and-goals.md', [heading('Requirements Overview', 200), heading('Quality Goals', 500, 'Goal')])));
            reveal('quality-goals', '01-introduction-and-goals.md');
            """);

        Assert.Equal("scrollTop 500", result);
    }

    /// <summary>An anchor typed as the heading reads still lands, as the reference
    /// resolver lets it.</summary>
    [Fact]
    public void An_anchor_written_as_the_heading_reads_still_finds_it()
    {
        var result = Run("""
            setBody(view('.devbook/arc42/01-introduction-and-goals.md', [heading('Quality Goals', 500)]));
            reveal('Quality Goals', '01-introduction-and-goals.md');
            """);

        Assert.Equal("scrollTop 500", result);
    }

    /// <summary>GitHub's rule as the generator spells it: punctuation dropped and
    /// each space a hyphen, runs not collapsed — so the dash in a heading leaves
    /// two hyphens in its anchor.</summary>
    [Fact]
    public void The_slug_drops_punctuation_and_keeps_each_space_as_a_hyphen()
    {
        var result = Run("""
            setBody(view('.devbook/arc42/02-constraints.md', [heading('Why — and when?', 300)]));
            reveal('why--and-when', '02-constraints.md');
            """);

        Assert.Equal("scrollTop 300", result);
    }

    /// <summary>The section on screen is the one searched: the hidden panels of the
    /// sections not chosen come first in the document and hold nothing.</summary>
    [Fact]
    public void Only_the_section_on_screen_is_searched()
    {
        var result = Run("""
            hiddenSection.children.push(view('.devbook/domain/context-map.md', [heading('Quality Goals', 900)]));
            setBody(view('.devbook/arc42/01-introduction-and-goals.md', [heading('Quality Goals', 500)]));
            reveal('quality-goals', '01-introduction-and-goals.md');
            """);

        Assert.Equal("scrollTop 500", result);
    }

    /// <summary>A reading that stacks several files is searched in the file the pane
    /// opened, not the first file holding a heading of that name.</summary>
    [Fact]
    public void In_a_stacked_reading_the_heading_is_found_in_the_file_that_was_opened()
    {
        var result = Run("""
            setBody(
                view('.devbook/design/principles.md', [heading('Quality Goals', 300)]),
                view('.devbook/design/tokens.md', [heading('Quality Goals', 800)]));
            reveal('quality-goals', 'tokens.md');
            """);

        Assert.Equal("scrollTop 800", result);
    }

    /// <summary>The panel switches its path before the new body lands, so the first
    /// match is the file it is leaving; the reveal keeps the heading at the top and
    /// lands on the new body's heading once it is drawn.</summary>
    [Fact]
    public void A_body_that_lands_after_the_path_still_ends_at_its_heading()
    {
        var result = Run("""
            setBody(view('.devbook/arc42/adr/0002-new.md', [heading('Context', 300)]));
            schedule(5, () => setBody(view('.devbook/arc42/adr/0002-new.md', [heading('Decision', 400), heading('Context', 1200)])));
            reveal('context', 'adr/0002-new.md');
            """);

        Assert.Equal("scrollTop 1200", result);
    }

    /// <summary>The reader scrolling ends the reveal: what lands after it does not
    /// pull them back.</summary>
    [Fact]
    public void The_reader_scrolling_ends_the_reveal()
    {
        var result = Run("""
            setBody(view('.devbook/arc42/adr/0002-new.md', [heading('Context', 300)]));
            schedule(2, () => { section.dispatch('wheel'); container.scrollTop = 50; });
            schedule(5, () => setBody(view('.devbook/arc42/adr/0002-new.md', [heading('Decision', 400), heading('Context', 1200)])));
            reveal('context', 'adr/0002-new.md');
            """);

        Assert.Equal("scrollTop 50", result);
    }

    /// <summary>A scenario part its page marked is that page's to reveal.</summary>
    [Fact]
    public void A_marked_scenario_part_is_left_to_its_page()
    {
        var result = Run("""
            const part = node({ attrs: { 'data-testid': 'devbook-scenario-part', 'aria-current': 'true' } });
            setBody(view('.devbook/domain/devbook/scenarios.md', [part, heading('Quality Goals', 500)]));
            reveal('quality-goals', 'devbook/scenarios.md');
            """);

        Assert.Equal("scrollTop 0", result);
    }

    /// <summary>Waiting is bounded: a heading the chapter does not have leaves no
    /// frame loop running.</summary>
    [Fact]
    public void A_heading_that_never_arrives_stops_being_waited_for()
    {
        var result = Run("""
            setBody(view('.devbook/arc42/01-introduction-and-goals.md', [heading('Requirements Overview', 500)]));
            reveal('quality-goals', '01-introduction-and-goals.md');
            """);

        Assert.Equal("scrollTop 0", result);
    }

    /// <summary>Once the reading settles, focus lands on the heading without
    /// scrolling again, so a keyboard or screen reader user is where the eye
    /// is; the heading is made focusable to take it.</summary>
    [Fact]
    public void Focus_lands_on_the_heading_once_the_reading_settles()
    {
        var result = Run("""
            setBody(view('.devbook/arc42/01-introduction-and-goals.md', [heading('Quality Goals', 500)]));
            reveal('quality-goals', '01-introduction-and-goals.md');
            report = () => `scrollTop ${container.scrollTop} focus ${focused?.element.textContent} ${focused?.element.attrs.tabindex} ${focused?.preventScroll}`;
            """);

        Assert.Equal("scrollTop 500 focus Quality Goals -1 true", result);
    }

    /// <summary>The reader moving first keeps their focus where it is.</summary>
    [Fact]
    public void The_reader_moving_first_keeps_focus_where_it_is()
    {
        var result = Run("""
            setBody(view('.devbook/arc42/01-introduction-and-goals.md', [heading('Quality Goals', 500)]));
            schedule(2, () => section.dispatch('keydown'));
            reveal('quality-goals', '01-introduction-and-goals.md');
            report = () => `focus ${focused === null ? 'none' : 'moved'}`;
            """);

        Assert.Equal("focus none", result);
    }

    /// <summary>A heading's own text, as the AI and Design folders' document view
    /// passes it with no path, finds the heading; and a body that keeps its
    /// overflow but grows rather than scrolls is passed over for the container
    /// that does scroll, the heading landing below that container's top
    /// padding.</summary>
    [Fact]
    public void A_heading_text_with_no_path_scrolls_the_container_that_actually_scrolls()
    {
        var result = Run("""
            const grown = node({ classes: ['file-view__body'], overflowY: 'auto', scrollHeight: 400, clientHeight: 400, children: [heading('Coding agent', 700)] });
            container.paddingTop = '20px';
            setBody(node({ classes: ['file-view'], children: [grown] }));
            reveal('Coding agent');
            """);

        Assert.Equal("scrollTop 680", result);
    }

    /// <summary>
    /// A document just large enough for the script: elements with classes,
    /// attributes, text and children; the compound selectors the script uses; one
    /// scroll container whose children sit at a fixed offset from the top of its
    /// content; and a frame queue the harness drains, running what a test
    /// scheduled for a frame before the script's own callbacks for it.
    /// </summary>
    private static string Run(string steps)
    {
        var harness = $$"""
            let frame = 0;
            let queue = [];
            const scheduled = [];
            const schedule = (at, action) => scheduled.push({ at, action });
            const requestAnimationFrame = callback => { queue.push(callback); };

            let focused = null;
            const node = ({ classes = [], attrs = {}, text = '', children = [], top = 0, overflowY, scrollHeight = 0, clientHeight = 0 } = {}) => {
                const element = {
                    classes, attrs, text, children, top, overflowY, scrollHeight, clientHeight, parentElement: null, listeners: {},
                    hasAttribute: name => name in element.attrs,
                    setAttribute(name, value) { element.attrs[name] = value; },
                    focus(options) { focused = { element, preventScroll: options?.preventScroll === true }; },
                    get textContent() { return element.text + element.children.map(child => child.textContent).join(''); },
                    matches: selector => matches(element, selector),
                    querySelectorAll: selector => descendants(element).filter(child => matches(child, selector)),
                    querySelector: selector => element.querySelectorAll(selector)[0] ?? null,
                    cloneNode: () => clone(element),
                    remove() { const parent = element.parentElement; if (parent) parent.children = parent.children.filter(child => child !== element); },
                    addEventListener(type, listener) { (element.listeners[type] ??= []).push(listener); },
                    removeEventListener(type, listener) { element.listeners[type] = (element.listeners[type] ?? []).filter(l => l !== listener); },
                    dispatch(type) { (element.listeners[type] ?? []).forEach(listener => listener()); },
                    getBoundingClientRect: () => ({ top: element.overflowY ? 100 : 100 + element.top - container.scrollTop })
                };
                return element;
            };

            const adopt = parent => { parent.children.forEach(child => { child.parentElement = parent; adopt(child); }); return parent; };
            const descendants = element => element.children.flatMap(child => [child, ...descendants(child)]);
            const clone = element => adopt(node({ ...element, classes: [...element.classes], attrs: { ...element.attrs }, children: element.children.map(clone) }));

            const matches = (element, selector) => {
                let rest = selector.trim();
                const negated = [];
                rest = rest.replace(/:not\(\[(\w[\w-]*)\]\)/g, (_, name) => { negated.push(name); return ''; });
                if (negated.some(name => name in element.attrs)) return false;
                for (const [, cls] of rest.matchAll(/\.([\w-]+)/g)) if (!element.classes.includes(cls)) return false;
                for (const [, name, value] of rest.matchAll(/\[([\w-]+)=["']([^"']*)["']\]/g)) if (element.attrs[name] !== value) return false;
                return true;
            };

            const heading = (words, top, mark) => node({
                attrs: { role: 'heading' }, top,
                children: mark ? [node({ attrs: { 'data-testid': 'markdown-chapter-type-mark' }, text: mark }), node({ text: words })] : [node({ text: words })]
            });
            const view = (path, children) => node({ classes: ['file-view'], children: [node({ classes: ['file-view__path'], text: path }), ...children] });

            const body = node();
            const container = node({ classes: ['devbook-document'], overflowY: 'auto', scrollHeight: 100000, clientHeight: 500 });
            container.scrollTop = 0;
            const hiddenSection = node({ classes: ['devbook-stack__section'], attrs: { hidden: '' } });
            const section = node({ classes: ['devbook-stack__section'], children: [container] });
            const stack = node({ attrs: { 'data-testid': 'devbook-stack' }, children: [hiddenSection, section] });
            body.children.push(stack);
            adopt(body);

            const setBody = (...views) => { container.children = views; adopt(container); };

            const window = {};
            const document = {
                body,
                querySelector: selector => {
                    const [ancestor, ...own] = selector.split(' ');
                    const roots = descendants(body).filter(element => matches(element, ancestor));
                    return roots.flatMap(root => descendants(root)).find(element => matches(element, own.join(' '))) ?? null;
                }
            };
            const getComputedStyle = element => ({ overflowY: element.overflowY ?? 'visible', paddingTop: element.paddingTop ?? '0px' });
            {{HeadingFunction()}}

            const reveal = (anchor, path) => window.backlogRevealHeading("[data-testid='devbook-stack'] .devbook-stack__section:not([hidden])", anchor, path);

            let report = null;
            {{steps}}

            while ((queue.length > 0 || scheduled.some(entry => entry.at > frame)) && frame < 1000) {
                frame++;
                scheduled.filter(entry => entry.at === frame).forEach(entry => entry.action());
                const due = queue;
                queue = [];
                due.forEach(callback => callback());
            }

            console.log(report ? report() : `scrollTop ${container.scrollTop}`);
            """;

        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            // The headings under test hold a dash; Node reads its script as UTF-8.
            StandardInputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            UseShellExecute = false
        };

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("node did not start");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Assert.Fail($"This test runs components.js and needs Node on the PATH: {exception.Message}");
            return string.Empty;
        }

        using (process)
        {
            process.StandardInput.Write(harness);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();

            Assert.True(process.WaitForExit(TimeSpan.FromSeconds(30)), "node did not finish in thirty seconds.");
            Assert.True(process.ExitCode == 0, $"node failed ({process.ExitCode}):\n{error.Result}");

            return output.Result.Trim();
        }
    }

    /// <summary>The frame budgets, the slug rule and <c>window.backlogRevealHeading</c>,
    /// cut out of the script as it ships, so the function under test is the one the
    /// app loads.</summary>
    private static string HeadingFunction()
    {
        var script = ComponentsScript.Source;

        const string from = "    const headingFrames = ";
        const string to = "    // A card the settings search found";

        var begin = script.IndexOf(from, StringComparison.Ordinal);
        Assert.True(begin >= 0, "components.js no longer declares the frames window.backlogRevealHeading waits.");

        var end = script.IndexOf(to, begin, StringComparison.Ordinal);
        Assert.True(end > begin, "The settings card reveal no longer follows window.backlogRevealHeading in components.js.");

        return script[begin..end];
    }
}
