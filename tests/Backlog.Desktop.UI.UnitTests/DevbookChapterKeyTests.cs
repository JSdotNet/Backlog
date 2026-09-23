namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The one name a Devbook chapter has, whichever device is speaking and
/// wherever that device has the area's folder pointed.
/// <para>
/// Idempotence is the load-bearing property rather than a nicety. It is what
/// lets the annotation stores canonicalize on every load and every write with no
/// marker recording whether they already have, so it is asserted directly and
/// again over every case below rather than assumed from the shape of the code.
/// </para>
/// </summary>
public sealed class DevbookChapterKeyTests
{
    private const string Decision = ".arc42/adr/0001-decision.md";

    [Fact]
    public void A_chapter_under_a_conventional_folder_keeps_its_name()
    {
        Assert.Equal(Decision, DevbookChapterKey.Canonical(Decision, Conventional()));
        Assert.Equal(".domain/context-map.md", DevbookChapterKey.Canonical(".domain/context-map.md", Conventional()));
        Assert.Equal(".tech/graph.md", DevbookChapterKey.Canonical(".tech/graph.md", Conventional()));
    }

    /// <summary>
    /// The defect, stated at the level it was fixed: arc42 pointed at
    /// <c>docs/arch</c> spells its documents that way, and the remark left on one
    /// has to be filed under the name a device with the conventional layout will
    /// come looking with.
    /// </summary>
    [Fact]
    public void A_chapter_under_a_relocated_folder_is_named_by_the_conventional_one()
    {
        var folders = Arc42At("docs/arch");

        Assert.Equal(Decision, DevbookChapterKey.Canonical("docs/arch/adr/0001-decision.md", folders));

        // And the conventional spelling of the same chapter still names it, which
        // is what makes the two devices agree rather than merely both move.
        Assert.Equal(Decision, DevbookChapterKey.Canonical(Decision, folders));
    }

    /// <summary>
    /// A folder configured off the clone entirely is the strongest case for the
    /// conventional prefix. <c>Arc42DevbookReader</c> falls back to spelling its
    /// documents relative to the folder's own parent there, so they arrive
    /// carrying nothing but the last segment — and a chapter read from
    /// <c>D:/knowledge/arch</c> on one desktop and <c>.arc42</c> on another has to
    /// key identically or the remark is invisible on the second.
    /// </summary>
    [Fact]
    public void A_chapter_under_a_folder_outside_the_clone_is_named_by_its_last_segment()
    {
        var folders = Arc42At("D:/knowledge/arch");

        Assert.Equal(Decision, DevbookChapterKey.Canonical("arch/adr/0001-decision.md", folders));
    }

    /// <summary>The panels trim the leading dot when they present an area, so a
    /// selection can name the same folder undotted and must not become a second
    /// chapter by it.</summary>
    [Fact]
    public void The_undotted_spelling_of_a_folder_names_the_same_chapter()
    {
        Assert.Equal(Decision, DevbookChapterKey.Canonical("arc42/adr/0001-decision.md", Conventional()));
        Assert.Equal(".domain/context-map.md", DevbookChapterKey.Canonical("domain/context-map.md", Conventional()));
    }

    /// <summary>
    /// Instructions has no folder of its own — its root is the repository — so its
    /// canonical form is the repository-relative path with nothing added and
    /// nothing stripped. Its empty default path must never act as a prefix
    /// either: one would match every path there is.
    /// </summary>
    [Fact]
    public void An_instructions_chapter_keeps_the_repository_relative_path_it_arrived_with()
    {
        Assert.Equal(".github/copilot-instructions.md", DevbookChapterKey.Canonical(".github/copilot-instructions.md", Conventional()));
        Assert.Equal(".claude/rules/naming.md", DevbookChapterKey.Canonical(".claude/rules/naming.md", Conventional()));
        Assert.Equal("README.md", DevbookChapterKey.Canonical("README.md", Conventional()));
    }

    /// <summary>The menu presents <c>.agents</c> as <c>.agent</c> so the three
    /// instruction roots read alike, which would otherwise give one file two
    /// keys.</summary>
    [Fact]
    public void The_menus_short_spelling_of_the_agents_folder_folds_into_the_real_one()
    {
        Assert.Equal(".agents/rules/naming.md", DevbookChapterKey.Canonical(".agent/rules/naming.md", Conventional()));
        Assert.Equal(".agents/rules/naming.md", DevbookChapterKey.Canonical(".agents/rules/naming.md", Conventional()));
    }

    /// <summary>
    /// With nothing configured — a storybook, a harness, a test — the
    /// conventional folders are still read, which is the right answer for every
    /// repository that has not moved one. A path from a repository that has is
    /// left alone rather than refused: degrading is the failure mode, never an
    /// exception.
    /// </summary>
    [Fact]
    public void With_no_folder_configuration_the_conventional_folders_are_still_read()
    {
        Assert.Equal(Decision, DevbookChapterKey.Canonical(Decision, null));
        Assert.Equal(Decision, DevbookChapterKey.Canonical("arc42/adr/0001-decision.md", []));
        Assert.Equal("docs/arch/adr/0001-decision.md", DevbookChapterKey.Canonical("docs/arch/adr/0001-decision.md", null));
        Assert.Equal(string.Empty, DevbookChapterKey.Canonical(null, null));
        Assert.Equal(string.Empty, DevbookChapterKey.Canonical("   ", null));
    }

    /// <summary>The cheap half: one spelling for a path before anything decides
    /// which area it belongs to. The anchor goes because a chapter is a file and
    /// the domain panel names sections as <c>path#anchor</c>.</summary>
    [Fact]
    public void One_spelling_survives_separators_anchors_and_leading_noise()
    {
        Assert.Equal(Decision, DevbookChapterKey.Canonical(@"  .arc42\adr\0001-decision.md  ", Conventional()));
        Assert.Equal(Decision, DevbookChapterKey.Canonical("./.arc42/adr/0001-decision.md", Conventional()));
        Assert.Equal(Decision, DevbookChapterKey.Canonical("/.arc42/adr/0001-decision.md", Conventional()));
        Assert.Equal(Decision, DevbookChapterKey.Canonical(".arc42/adr/0001-decision.md#the-decision", Conventional()));
    }

    /// <summary>
    /// Two areas can both claim a path once somebody points one configured folder
    /// inside another configured folder, and only the longer claim is right. The
    /// areas never compete like this inside one area's own prefix list, which is
    /// why the order across them is worth pinning.
    /// <para>
    /// Both folders are relocated ones here. Nesting a configured folder inside a
    /// <em>conventional</em> one is a different question with a different answer —
    /// the conventional folder wins, whatever its length, because that is what
    /// makes the key idempotent — and
    /// <see cref="An_area_pointed_at_another_areas_conventional_folder_keys_under_that_folder"/>
    /// is where that is pinned.
    /// </para>
    /// </summary>
    [Fact]
    public void The_most_specific_folder_claims_a_path_two_areas_could_read()
    {
        var folders = At((".arc42", "docs/arch"), (".tech", "docs/arch/adr"));

        Assert.Equal(".tech/0001-decision.md", DevbookChapterKey.Canonical("docs/arch/adr/0001-decision.md", folders));
        Assert.Equal(".arc42/08-concepts.md", DevbookChapterKey.Canonical("docs/arch/08-concepts.md", folders));
    }

    /// <summary>
    /// The property that replaces a migration marker. Canonicalizing an
    /// already-canonical path returns it, so the stores can run this on every
    /// load and every write without recording whether they already did — which is
    /// cheaper than remembering, and cannot get out of step with what was
    /// remembered.
    /// <para>
    /// The configurations swept here matter as much as the paths. Three
    /// single-relocation layouts are the family that cannot break — every one of
    /// them was green while the key was still rewriting a chapter twice — so the
    /// set also carries an area nested inside another area's conventional folder,
    /// an area pointed <em>at</em> one, and two areas pointed at one folder.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(".arc42/adr/0001-decision.md")]
    [InlineData("docs/arch/adr/0001-decision.md")]
    [InlineData("arch/adr/0001-decision.md")]
    [InlineData("arc42/adr/0001-decision.md")]
    [InlineData(".domain/context-map.md")]
    [InlineData(".tech/0001-decision.md")]
    [InlineData(".agent/rules/naming.md")]
    [InlineData(".agents/rules/naming.md")]
    [InlineData(".github/copilot-instructions.md")]
    [InlineData("README.md")]
    [InlineData("docs/shared/notes.md")]
    [InlineData(@".arc42\adr\0001-decision.md#anchor")]
    [InlineData(".arc42")]
    [InlineData(".arc42/")]
    public void Canonicalizing_a_canonical_path_returns_it(string path)
    {
        foreach (var folders in EveryConfiguration())
        {
            var once = DevbookChapterKey.Canonical(path, folders);

            Assert.Equal(once, DevbookChapterKey.Canonical(once, folders));
            Assert.Equal(once, DevbookChapterKey.Canonical(DevbookChapterKey.Canonical(once, folders), folders));

            // The store applies this at one call site, but nothing about the
            // function may depend on that: two applications have to be one.
            Assert.Equal(once, DevbookChapterKey.Canonical(DevbookChapterKey.Canonical(path, folders), folders));
        }
    }

    /// <summary>
    /// The counterexample that proved the first draft of this key wrong, kept as
    /// a test in its own right rather than only as a row in the sweep above.
    /// <para>
    /// <c>.arc42</c> at <c>docs/arch</c> and <c>.tech</c> at <c>.arc42/adr</c>:
    /// the first pass rewrote a chapter into arc42's conventional folder, and on
    /// the second pass tech's configured prefix was the longer one and claimed the
    /// result. Two of the call sites applied the key twice, so the store held
    /// <c>.tech/0001-decision.md</c> while the caller was handed
    /// <c>.arc42/adr/0001-decision.md</c> — a remark filed where nobody would ever
    /// look for it. Testing the conventional folder first is what closed it.
    /// </para>
    /// </summary>
    [Fact]
    public void An_area_nested_inside_another_areas_conventional_folder_does_not_move_a_chapter_twice()
    {
        var folders = At((".arc42", "docs/arch"), (".tech", ".arc42/adr"));

        var once = DevbookChapterKey.Canonical("docs/arch/adr/0001-decision.md", folders);

        Assert.Equal(Decision, once);
        Assert.Equal(once, DevbookChapterKey.Canonical(once, folders));
    }

    /// <summary>
    /// The price of testing the conventional folder first, pinned so that it is a
    /// decision rather than a surprise: point one area at another's conventional
    /// folder and chapters under it key as the folder's own area. Two areas
    /// reading one folder is ambiguous and no rule reads it right; this one at
    /// least reads it the same way twice.
    /// </summary>
    [Fact]
    public void An_area_pointed_at_another_areas_conventional_folder_keys_under_that_folder()
    {
        var folders = At((".arc42", ".domain"));

        var once = DevbookChapterKey.Canonical(".domain/context-map.md", folders);

        Assert.Equal(".domain/context-map.md", once);
        Assert.Equal(once, DevbookChapterKey.Canonical(once, folders));
    }

    /// <summary>
    /// Two areas pointed at the same folder offer the same prefix at the same
    /// rank, and the winner is settled by area key alphabetically — written down
    /// in the match order rather than left to fall out of the declaration order of
    /// <c>DevbookFolderSetting.Defaults()</c> through a stable sort. Arbitrary for
    /// an arbitrary configuration, but predictable without knowing how LINQ sorts.
    /// </summary>
    [Fact]
    public void Two_areas_sharing_one_folder_are_settled_by_area_key()
    {
        var folders = At((".tech", "docs/shared"), (".design", "docs/shared"));

        // "design" before "tech", whichever order the defaults happen to list
        // them in, and whichever order the settings were written in.
        var once = DevbookChapterKey.Canonical("docs/shared/notes.md", folders);

        Assert.Equal(".design/notes.md", once);
        Assert.Equal(once, DevbookChapterKey.Canonical(once, folders));

        Assert.Equal(".design/notes.md", DevbookChapterKey.Canonical("docs/shared/notes.md", At((".design", "docs/shared"), (".tech", "docs/shared"))));
    }

    /// <summary>Every layout the sweep above runs each path through: the ordinary
    /// one, the three single relocations that can never break, and the three
    /// shapes that can.</summary>
    private static IEnumerable<IReadOnlyList<DevbookFolderSetting>> EveryConfiguration() =>
    [
        Conventional(),
        Arc42At("docs/arch"),
        Arc42At("D:/knowledge/arch"),
        Arc42At(".domain"),
        At((".arc42", "docs/arch"), (".tech", ".arc42/adr")),
        At((".tech", ".arc42/adr")),
        At((".tech", "docs/shared"), (".design", "docs/shared")),
    ];

    private static IReadOnlyList<DevbookFolderSetting> Conventional() => DevbookFolderSetting.Defaults();

    private static IReadOnlyList<DevbookFolderSetting> Arc42At(string path) => At((".arc42", path));

    /// <summary>The conventional folders with the named areas pointed
    /// elsewhere.</summary>
    private static IReadOnlyList<DevbookFolderSetting> At(params (string Key, string Path)[] overrides) =>
    [
        .. DevbookFolderSetting.Defaults().Select(folder =>
            overrides.FirstOrDefault(o => string.Equals(o.Key, folder.Key, StringComparison.OrdinalIgnoreCase)) is { Path: not null } match
                ? folder with { Path = match.Path }
                : folder)
    ];
}
