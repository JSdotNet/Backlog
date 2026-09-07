using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// <see cref="UiLibraryBoundaryTests"/> proves the applications reference the
/// shared library. Referencing it is not using it: a screen can take the
/// dependency and still hand-roll a button, and then the storybook documents a
/// control nobody renders while the app ships a second one nobody reviewed.
///
/// <para>These rules are about the second half — that a control the library
/// defines is the control the application renders. They come in two halves,
/// because a hand-roll takes two shapes:</para>
///
/// <list type="number">
/// <item>a raw interactive element where the library has a component —
/// <c>&lt;button&gt;</c>, <c>&lt;input&gt;</c>. Caught by the element name;</item>
/// <item>a <c>div</c>, <c>span</c> or <c>p</c> wearing a component's own class.
/// Caught by the class name, because the element name says nothing.</item>
/// </list>
///
/// <para>The second half exists because the first was fully held and the drift
/// went on anyway: a repository-wide search for raw controls returns nothing,
/// while the shell had hand-rolled the save-state indicator down to its dot and
/// its modifiers, and two knowledge panels had hand-rolled badges. None of it
/// was a raw control, so no rule saw it.</para>
///
/// <para>Both halves are deliberately narrow. A raw element is only a finding
/// when the library has a component for the job, and the exception lists below
/// say which elements are not that.</para>
/// </summary>
public class SharedControlAdoptionTests
{
    /// <summary>The interactive elements the library ships a component for:
    /// AppButton/IconButton/ToggleButton, TextField, TextArea, and the select
    /// family.</summary>
    private static readonly string[] RawControls = ["button", "input", "select", "textarea"];

    /// <summary>
    /// The raw elements that are not a component the library is missing.
    ///
    /// <para>Empty, and that is the finding rather than an omission. The backlog
    /// pane held all four exceptions there have ever been, and the To Do-shaped
    /// rewrite retired every one of them:</para>
    /// <list type="bullet">
    /// <item><c>entry-doc__editor</c> and <c>subitem-card__editor</c> — the two
    /// hand-rolled markdown textareas. The entry's note is a
    /// <c>MarkdownEditor</c>, a step's notes are the shared task row's own body
    /// editor, and the raw escape hatch is a <c>TextArea</c>. What the exception
    /// was really buying — a reading line inside the grow wrapper — turned out
    /// to be a sibling of the box rather than a child of it, and nothing needed
    /// an <c>ElementReference</c> once the surface stopped being what opened on
    /// click.</item>
    /// <item><c>entry-doc__grip</c> and <c>subitem-card__grip</c> — the reorder
    /// rails and their drop zones. <c>TaskListView</c> owns the whole drag,
    /// including the <c>ondragover:preventDefault</c> that could not be written
    /// on a component: it is written inside one.</item>
    /// </list>
    ///
    /// <para>Adding an entry here is a claim that the library has nothing to
    /// adopt. If the answer is instead that a component is missing a hook, the
    /// hook belongs in the library — which is how the sub-item hand-off buttons
    /// became <c>TaskListView.RowActions</c> rather than a fifth entry.</para>
    /// </summary>
    private static readonly string[] AllowedRawControls = [];

    /// <summary>
    /// The class names the shared library puts on the elements its own
    /// components render, read out of the library rather than listed here. A
    /// hand-written list would be a second copy of the library, going stale in
    /// exactly the way this file exists to prevent — a component renamed or
    /// added would leave the rule quietly passing.
    ///
    /// <para>Two sources are intersected, because neither alone is right.
    /// <c>components.css</c> knows every class the library <em>styles</em>, but
    /// that includes classes the applications wear and the library merely
    /// dresses — <c>entry-doc</c>, <c>subitem-card</c> — which are not
    /// components to adopt. The components' own sources name every class they
    /// <em>render</em>, but that set also holds ordinary strings that happen to
    /// look like class names: status words, ARIA roles, provider ids. A name in
    /// both is a class a library component draws and the library styles, which
    /// is the definition wanted here.</para>
    ///
    /// <para>Reading the sources rather than only the markup matters: the
    /// classes most worth catching are composed in C#, not written in an
    /// attribute. <c>SaveIndicator</c> builds <c>save-indicator</c> in a
    /// <c>ClassValue</c> getter, and a markup-only scan would miss it — which is
    /// to say it would have missed the hand-rolled copy this rule was written
    /// for.</para>
    ///
    /// <para>Computed on first use rather than in a field initializer, and not
    /// to be "simplified" back into one. Static initializers run in declaration
    /// order, so a field here would read the regexes below while they are still
    /// null — which is a NullReferenceException in a type initializer, reported
    /// against whichever test happened to run first.</para>
    /// </summary>
    private static HashSet<string> ComponentClasses => _componentClasses ??= LibraryComponentClasses();

    private static HashSet<string>? _componentClasses;

    /// <summary>
    /// The elements that wear a component's class and are still not a component
    /// the library is missing. An entry matches the whole class attribute, so
    /// the rest of an allowed element's classes come along with it.
    ///
    /// <list type="bullet">
    /// <item><c>badge--gh</c> — the linked GitHub issue and pull request badges,
    /// in Tasks and in Second Brain. <c>IntegrationLink</c> is what
    /// these become, and it draws itself as <c>integration-link</c> rather than
    /// <c>badge--gh</c>, so adopting it is a visual change to both areas at
    /// once. Converting one and leaving the other would put two shapes on the
    /// same fact on the same screen, which reads worse than the duplication
    /// does.</item>
    /// <item><c>pane-resizer</c> — the shell's side-pane separator. The class is
    /// <c>SplitPane</c>'s, but this separator is driven from
    /// <c>backlogPaneResizer</c> in the shell's app.js, which finds it by its
    /// <c>data-pane-resizer</c> attribute and measures the layout by test id,
    /// and the shell owns the arrow-key handling on it. SplitPane takes none of
    /// that yet, so adopting it means moving the interop into the library
    /// first.</item>
    /// <item><c>md-link</c> — one anchor into Settings from the domain knowledge
    /// panel, borrowing the class <c>MarkdownView</c> puts on a link inside
    /// rendered Markdown. There is no component to adopt: the library ships no
    /// link. Giving it one of the shell's own link classes instead is a visual
    /// change, not an adoption, so it is left where it is rather than changed
    /// under cover of this rule.</item>
    /// </list>
    ///
    /// <para>Adding an entry here is a claim either that the library has nothing
    /// to adopt, or that adopting it is a separate piece of work with its own
    /// visual consequences — and the entry has to say which. If the answer is
    /// instead that a component is missing a hook, the hook belongs in the
    /// library.</para>
    /// </summary>
    private static readonly string[] AllowedComponentClasses =
    [
        "badge--gh",
        "md-link",
        "pane-resizer"
    ];

    /// <summary>
    /// Classes the library ships that are utilities rather than components.
    /// There is nothing to adopt: <c>sr-only</c> visually hides text, and a host
    /// that needs visually hidden text is using it correctly by wearing it.
    /// </summary>
    private static readonly string[] UtilityClasses = ["sr-only"];

    /// <summary>
    /// The words that, in the element half of a screen's own BEM name, say the
    /// element is drawing a shape the library's feedback components already draw:
    /// a status or error line (<c>Alert</c>), a dead end (<c>EmptyState</c>), a
    /// count beside a title (<c>Badge</c>).
    ///
    /// <para>Matched as whole words after splitting on <c>-</c>, so
    /// <c>domain-knowledge__action-error</c> is a finding and
    /// <c>tools-inventory__meta</c> is not. Read off the modifier as well as the
    /// element, because the highest-value copies in this repository are written as
    /// modifiers rather than elements — <c>setting__status--error</c> is the
    /// inline alert, and a rule reading only element halves finds nothing at
    /// all.</para>
    /// </summary>
    private static readonly string[] FeedbackWords =
        ["empty", "loading", "message", "notice", "status", "state", "error", "count", "placeholder"];

    /// <summary>
    /// The screens' own names that read as a feedback shape and are still not one
    /// the library is missing.
    ///
    /// <list type="bullet">
    /// <item><c>setting__status</c> — the settings screen's status lines, and the
    /// one in the knowledge instructions panel that borrows the same class. Every
    /// one of them is an <c>Alert</c>, block or inline, and adopting them is real
    /// work rather than a rename: there are two dozen, each carries a test id that
    /// a unit test reads, and two of them are asserted through the outer
    /// element's <c>InnerHtml</c> rather than by their own class, so the nesting
    /// is load-bearing. Left whole and deliberately, so the conversion happens
    /// once with the settings screen in front of somebody.</item>
    /// <item><c>knowledge-folder__status</c> — the same screen and the same
    /// answer; it is a status line in the settings knowledge-folder rows, with a
    /// <c>data-folder-state</c> attribute that no component takes yet.</item>
    /// <item><c>feature-flag__status</c> — the words "Always available" inside a
    /// <c>Toggle</c>'s own <c>TextContent</c> slot. Not a status line: it says the
    /// switch beside it cannot be turned off, and it is markup landing in a
    /// component's slot rather than an element claiming to be a component. The
    /// same distinction <see cref="WornAs"/> draws for a BEM child.</item>
    /// <item><c>chip__count</c> — the count trailing a filter chip's label, inside
    /// the <c>ToggleButton</c> or <c>AppButton</c> that draws the chip. A
    /// <c>Badge</c> is a thing beside a title; this is part of a control's own
    /// label, and a badge nested in a chip would be a second bordered shape inside
    /// the first. A stylesheet test reads the class out of a media query, which is
    /// the other reason it does not move.</item>
    /// </list>
    ///
    /// <para>Every entry here is a claim that adopting the component is either
    /// wrong or separate work — never that the copy is fine. An entry that means
    /// "not got round to it" should say so, as the first two do.</para>
    /// </summary>
    private static readonly string[] AllowedAppFeedbackClasses =
    [
        "chip__count",
        "feature-flag__status",
        "knowledge-folder__status",
        "setting__status"
    ];

    /// <summary>The elements a hand-rolled feedback shape is written as.</summary>
    private static readonly Regex FeedbackElement =
        new(@"<(p|div|span)\s([^>]*)>", RegexOptions.Compiled);

    /// <summary>A Blazor component: an element whose name is capitalised. An
    /// element holding one is laying something out, not drawing it.</summary>
    private static readonly Regex ComponentTag =
        new("<[A-Z]", RegexOptions.Compiled);

    private static readonly Regex ClassAttribute =
        new("(?<![A-Za-z-])class=\"([^\"]*)\"", RegexOptions.Compiled);

    /// <summary>A class selector in the library's stylesheet.</summary>
    private static readonly Regex StyledClass =
        new(@"\.([a-z][a-z0-9-]*)", RegexOptions.Compiled);

    /// <summary>A string literal that opens with something shaped like a class
    /// name. Anchored on the quote so it reads both an attribute value and a C#
    /// literal, including the head of an interpolated one — the
    /// <c>save-indicator</c> in <c>$"save-indicator{modifier}{extra}"</c>.
    /// Underscores are outside the character class, so a literal naming a BEM
    /// element yields its block.</summary>
    private static readonly Regex ClassLiteral =
        new("\"([a-z][a-z0-9-]*)", RegexOptions.Compiled);

    [Fact]
    public void Application_screens_render_the_librarys_controls_rather_than_their_own()
    {
        var screens = ApplicationScreens().ToList();

        Assert.NotEmpty(screens);

        var offenders = new List<string>();

        foreach (var screen in screens)
        {
            var lines = File.ReadAllLines(screen.FullName);

            for (var index = 0; index < lines.Length; index++)
            {
                var tag = OpeningControlTag(lines[index]);
                if (tag is null) continue;

                // The opening tag runs over several lines here, and an event
                // handler in it can contain a `>` of its own, so the element is
                // read as a window rather than parsed.
                var window = string.Join('\n', lines.Skip(index).Take(12));
                if (AllowedRawControls.Any(allowed => window.Contains(allowed, StringComparison.Ordinal))) continue;

                offenders.Add($"{Relative(screen)}:{index + 1} <{tag}>");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These screens hand-roll a control the shared library already defines. Render the component, "
            + "and if it cannot wear the screen's classes, give it the hook rather than a second implementation:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>
    /// The other shape of the same mistake: not a raw control, but a plain
    /// element wearing a component's own class. That element is a second
    /// implementation of the component however exactly it matches today —
    /// exactly is how it starts, and the copy is what stops tracking the
    /// original.
    ///
    /// <para>Only the root of a class counts, so <c>badge--status</c> is a
    /// finding and <c>badge__state</c> is not. The rule is about an element
    /// claiming to <em>be</em> a component; markup that lands inside a
    /// component's slot and wears one of its child classes is not making that
    /// claim.</para>
    /// </summary>
    [Fact]
    public void Application_screens_do_not_wear_a_shared_components_own_class()
    {
        var screens = ApplicationScreens().ToList();

        Assert.NotEmpty(screens);

        var offenders = new List<string>();

        foreach (var screen in screens)
        {
            var lines = File.ReadAllLines(screen.FullName);

            for (var index = 0; index < lines.Length; index++)
            {
                foreach (var attribute in ClassAttribute.Matches(lines[index]).Cast<Match>())
                {
                    var tokens = attribute.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    // Keyed on any one class the element carries, so an allowed
                    // element is allowed whole rather than class by class.
                    if (tokens.Any(IsAllowedComponentClass)) continue;

                    var borrowed = tokens.Select(WornAs).Where(ComponentClasses.Contains).Distinct();

                    offenders.AddRange(borrowed.Select(name => $"{Relative(screen)}:{index + 1} class=\"{name}\""));
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These screens put a shared component's own class on an element of their own, which is a second "
            + "implementation of that component. Render the component instead — every one of them takes a "
            + "BaseClass or CssClass so it can wear the screen's own classes — and if it genuinely cannot, "
            + "give it the hook rather than a second implementation:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>
    /// The third shape, and the one the two rules above are blind to by
    /// construction: a copy wearing an entirely screen-owned name. Both rules
    /// ask whether a name belongs to the library, so
    /// <c>&lt;p class="sessions-panel__empty"&gt;</c> passes them while being as
    /// much a second <c>Alert</c> as anything they catch. The instructions have
    /// named this a review concern since the class rule was written; this is that
    /// paragraph turned into a test.
    ///
    /// <para>It cannot ask whether a name is the library's, so it asks what the
    /// name <em>says</em>. A screen's own BEM name whose element or modifier reads
    /// <c>status</c>, <c>empty</c>, <c>error</c> or one of the others in
    /// <see cref="FeedbackWords"/> is describing a shape the library's feedback
    /// components already draw.</para>
    ///
    /// <para>Two narrowings keep that from being a rule about naming rather than
    /// about duplication. Names the library owns are skipped, because they are the
    /// rule above's business and its BEM-child carve-out is deliberate — a
    /// <c>badge__state</c> inside a badge is not a finding twice over. And an
    /// element holding a component is skipped, because a wrapper is laying
    /// something out: <c>app-footer__status</c> exists to hold a
    /// <c>SaveIndicator</c> and reserve its width, and calling that a hand-rolled
    /// save indicator would be exactly wrong.</para>
    ///
    /// <para>That second narrowing is a heuristic and it errs toward silence: a
    /// copy that happens to contain a component is missed. The trade was
    /// deliberate — the alternative flags every layout wrapper in the shell, and
    /// an exception list whose entries are mostly "this is a div" is the loophole
    /// the companion test below exists to prevent.</para>
    /// </summary>
    [Fact]
    public void Application_screens_do_not_hand_roll_a_feedback_shape_under_their_own_name()
    {
        var screens = ApplicationScreens().ToList();

        Assert.NotEmpty(screens);

        var offenders = new List<string>();

        foreach (var screen in screens)
        {
            var text = File.ReadAllText(screen.FullName);

            foreach (var element in FeedbackElement.Matches(text).Cast<Match>())
            {
                var attributes = element.Groups[2].Value;
                var classes = ClassAttribute.Match(attributes);
                if (!classes.Success) continue;

                // A wrapper holding a component is laying it out, not drawing it.
                if (ComponentTag.IsMatch(Body(text, element))) continue;

                var borrowed = classes.Groups[1].Value
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(token => !ComponentClasses.Contains(WornAs(token)))
                    .Where(token => !ComponentClasses.Contains(NameOf(token)))
                    .Where(token => !IsAllowedAppFeedbackClass(token))
                    .Where(ReadsAsFeedback)
                    .Distinct();

                offenders.AddRange(borrowed.Select(
                    token => $"{Relative(screen)}:{LineOf(text, element.Index)} <{element.Groups[1].Value} class=\"{token}\">"));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These screens draw a shape the shared library already has — a status or error line, a dead "
            + "end, a count — as an element of their own, under a name of their own. A screen-owned class "
            + "is why no other rule sees it, not why it is allowed. Render Alert, EmptyState or Badge with "
            + "BaseClass set to the screen's class, and if the component cannot take the shape, add the "
            + "hook to the library rather than keeping the copy:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>
    /// An exception that has stopped being one is worse than no exception list:
    /// it reads as a considered decision while quietly permitting anything.
    /// </summary>
    [Fact]
    public void Every_allowed_raw_control_is_still_a_raw_control_somewhere()
    {
        var markup = string.Join('\n', ApplicationScreens().Select(file => File.ReadAllText(file.FullName)));

        Assert.NotEmpty(markup);

        var stale = AllowedRawControls
            .Where(allowed => !markup.Contains(allowed, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These raw-control exceptions no longer match anything and should be deleted: "
            + string.Join(", ", stale));
    }

    /// <inheritdoc cref="Every_allowed_raw_control_is_still_a_raw_control_somewhere" />
    [Fact]
    public void Every_allowed_component_class_is_still_worn_somewhere()
    {
        var markup = string.Join('\n', ApplicationScreens().Select(file => File.ReadAllText(file.FullName)));

        Assert.NotEmpty(markup);

        var stale = AllowedComponentClasses
            .Where(allowed => !markup.Contains(allowed, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These component-class exceptions no longer match anything and should be deleted: "
            + string.Join(", ", stale));
    }

    /// <inheritdoc cref="Every_allowed_raw_control_is_still_a_raw_control_somewhere" />
    [Fact]
    public void Every_allowed_app_feedback_class_is_still_worn_somewhere()
    {
        var markup = string.Join('\n', ApplicationScreens().Select(file => File.ReadAllText(file.FullName)));

        Assert.NotEmpty(markup);

        var stale = AllowedAppFeedbackClasses
            .Where(allowed => !markup.Contains(allowed, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These screen-owned feedback exceptions no longer match anything and should be deleted: "
            + string.Join(", ", stale));
    }

    /// <summary>
    /// The class rule reads the library to find out what the library owns, so a
    /// change that breaks that reading turns the rule green rather than red —
    /// the worst way for a rule to fail. These anchors are the cheapest way to
    /// notice: one class written in markup, one written as a parameter default,
    /// and one utility that must stay out.
    /// </summary>
    [Fact]
    public void The_librarys_own_classes_are_still_discoverable()
    {
        Assert.Contains("badge", ComponentClasses);
        Assert.Contains("empty-state", ComponentClasses);
        Assert.Contains("save-indicator", ComponentClasses);
        Assert.Contains("app-error-message", ComponentClasses);

        Assert.DoesNotContain("sr-only", ComponentClasses);
    }

    private static string? OpeningControlTag(string line) =>
        RawControls.FirstOrDefault(control => Regex.IsMatch(line, $@"<{control}(\s|>|$)"));

    private static bool IsAllowedComponentClass(string token) =>
        AllowedComponentClasses.Any(allowed => token.StartsWith(allowed, StringComparison.Ordinal));

    /// <summary>Keyed on the block-and-element stem, so one entry covers a class
    /// and every modifier of it: <c>setting__status</c> allows
    /// <c>setting__status--error</c> without listing it.</summary>
    private static bool IsAllowedAppFeedbackClass(string token) =>
        AllowedAppFeedbackClasses.Any(allowed => token.StartsWith(allowed, StringComparison.Ordinal));

    /// <summary>
    /// Whether a screen's own class name says it is drawing one of the library's
    /// feedback shapes. Only the element and modifier halves are read — the block
    /// is the screen's subject and says nothing about shape, so
    /// <c>sessions-panel__empty</c> is a finding while a block that happened to be
    /// called <c>status-bar</c> is not.
    /// </summary>
    private static bool ReadsAsFeedback(string token)
    {
        var element = token.IndexOf("__", StringComparison.Ordinal);
        if (element < 0) return false;

        return token[(element + 2)..]
            .Replace("--", "-", StringComparison.Ordinal)
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Any(word => FeedbackWords.Contains(word, StringComparer.Ordinal));
    }

    /// <summary>What an element contains, to the point it closes or for long
    /// enough to tell. Nested elements of the same name defeat the close-tag
    /// search, which is why there is a ceiling as well.</summary>
    private static string Body(string text, Match element)
    {
        var start = element.Index + element.Length;
        var close = text.IndexOf($"</{element.Groups[1].Value}>", start, StringComparison.Ordinal);
        var end = close < 0 ? text.Length : close;

        return text[start..Math.Min(end, Math.Min(start + 1200, text.Length))];
    }

    private static int LineOf(string text, int index) =>
        text.Take(index).Count(character => character == '\n') + 1;

    /// <summary>
    /// A class token as an application wears it, without its BEM modifier:
    /// <c>badge</c> out of <c>badge--status</c>.
    ///
    /// <para>The BEM element is deliberately left on, so <c>badge__state</c>
    /// stays <c>badge__state</c> and does not read as <c>badge</c>. The rule is
    /// about an element claiming to <em>be</em> a component; markup that lands
    /// inside a component's slot and wears one of its child classes is not
    /// making that claim. <see cref="NameOf"/> is the other half of this pair,
    /// and it does strip the element, because there the question is which
    /// component a name belongs to.</para>
    /// </summary>
    private static string WornAs(string token)
    {
        var modifier = token.IndexOf("--", StringComparison.Ordinal);
        return modifier < 0 ? token : token[..modifier];
    }

    /// <summary>The component a class name belongs to: <c>badge</c> out of both
    /// <c>badge--status</c> and <c>badge__state</c>.</summary>
    private static string NameOf(string token)
    {
        var name = WornAs(token);
        var element = name.IndexOf("__", StringComparison.Ordinal);
        return element < 0 ? name : name[..element];
    }

    private static HashSet<string> LibraryComponentClasses()
    {
        var library = Repository.ProjectsUnder("src", "Core", "Backlog.UI.Components")
            .Single(project => project.Name.Equals("Backlog.UI.Components.csproj", StringComparison.OrdinalIgnoreCase))
            .Directory!;

        var styled = StyledClasses(library);
        var classes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in LibrarySources(library))
        {
            var text = File.ReadAllText(source.FullName);

            var named = ClassLiteral.Matches(text).Cast<Match>().Select(literal => literal.Groups[1].Value)
                .Concat(ClassAttribute.Matches(text).Cast<Match>()
                    .SelectMany(attribute => attribute.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

            foreach (var candidate in named.Select(NameOf))
            {
                if (!styled.Contains(candidate)) continue;
                if (UtilityClasses.Contains(candidate, StringComparer.Ordinal)) continue;

                classes.Add(candidate);
            }
        }

        return classes;
    }

    /// <summary>Every class the library's stylesheet dresses, by name.</summary>
    private static HashSet<string> StyledClasses(DirectoryInfo library)
    {
        var stylesheet = new FileInfo(Path.Combine(library.FullName, "wwwroot", "components.css"));

        Assert.True(stylesheet.Exists, $"The library's stylesheet is where its class names are read from: {stylesheet.FullName}");

        return StyledClass.Matches(File.ReadAllText(stylesheet.FullName)).Cast<Match>()
            .Select(selector => NameOf(selector.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The components themselves — markup and the C# beside it, which
    /// is where a composed class name is written. Not <c>wwwroot</c>: the
    /// stylesheet is the other half of the intersection and would collapse
    /// it.</summary>
    private static IEnumerable<FileInfo> LibrarySources(DirectoryInfo library) =>
        library.EnumerateFiles("*.*", SearchOption.AllDirectories)
            .Where(file => file.Extension is ".razor" or ".cs")
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}"));

    /// <summary>Every .razor file in the UI projects — the screens a user
    /// actually sees, as opposed to the harnesses that host them.
    ///
    /// <para>That means <c>src/App</c> and <c>src/Modules</c> both. This used to
    /// read <c>src/App</c> alone and meant the same thing, because the desktop's
    /// three context panes were folders inside <c>Backlog.Desktop.UI</c>. Once
    /// they became their own projects under <c>src/Modules</c> the same code
    /// enumerated the shell and the mobile app and nothing else — every rule
    /// below stayed green while covering almost none of the screens it is
    /// written about.</para></summary>
    private static IEnumerable<FileInfo> ApplicationScreens() =>
        Repository.UserInterfaceProjects()
            .SelectMany(project => project.Directory!.EnumerateFiles("*.razor", SearchOption.AllDirectories))
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Name.StartsWith('_'));

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/');
}
