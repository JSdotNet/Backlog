using Backlog.UI.Components.Compare;

namespace Backlog.UI.Storybook.Components.Shared;

/// <summary>
/// The two versions of each file the section-comparison stories are drawn from,
/// plus the ranges and commits the picker shows.
/// </summary>
/// <remarks>
/// <para>
/// Markdown written out by hand, and nothing here reads a repository. The
/// storybook references the component library and the service defaults and
/// nothing else, which is what proves the library carries no domain — and the
/// comparison components are presentational for the same reason. A fixture that
/// shelled out to git would quietly undo both.
/// </para>
/// <para>
/// The bodies are short but real: each carries a <c>meta</c> fence, a table or a
/// code block, so the stories also prove that MarkdownView still renders every
/// block shape correctly inside a compare wrapper. A comparison that renders
/// paragraphs and quietly mangles tables would look fine on a fixture made only
/// of paragraphs.
/// </para>
/// <para>
/// Self-consistent on purpose, the way ProductivityFixtures is. The per-file
/// counts shown in the file list are <em>counted from the comparison</em> rather
/// than typed in beside it, so the "+3 −1 ±2" on a row cannot drift away from
/// what the pane beside it draws. A fixture whose summary disagrees with its
/// detail hides exactly the bug this view exists to surface.
/// </para>
/// </remarks>
internal static class CompareFixtures
{
    /// <summary>One file in one scope: what it is called, what it was, what it
    /// is, and the comparison of the two.</summary>
    internal sealed record Fixture(ChangedFile File, string Before, string After)
    {
        public ComparedSection Comparison { get; } = MarkdownCompare.Compare(Before, After);
    }

    public const string CommittedScopeId = "committed";
    public const string LastCommitScopeId = "last-commit";
    public const string UncommittedScopeId = "uncommitted";

    /// <summary>
    /// Three commits. <c>Age</c> is a written-out string, not a timestamp, so
    /// these render identically on every machine and every run — a relative time
    /// computed from the clock would make a screenshot taken today
    /// uncomparable with one taken next month.
    /// </summary>
    public static IReadOnlyList<ChangeCommit> Commits { get; } =
    [
        new("6e636df", "6e636df", "Run Deploy Foundry on a self-hosted runner", "36m ago"),
        new("b19f9d1", "b19f9d1", "Session configuration reference", "4h ago"),
        new("f209acf", "f209acf", "Release checklist: freeze before tagging, not after", "2d ago")
    ];

    /// <summary>The files behind the "Committed" range: three edited chapters.</summary>
    public static IReadOnlyList<Fixture> Committed { get; } =
    [
        Modified(".tech/deploy-foundry.md", DeployBefore, DeployAfter),
        Modified(".tech/session-configuration.md", SessionBefore, SessionAfter),
        Modified(".tech/release-checklist.md", ChecklistBefore, ChecklistAfter)
    ];

    /// <summary>
    /// The files behind the "Last commit" range, chosen so all three file kinds
    /// are on screen at once: one edited, one whole new chapter, one deleted.
    /// </summary>
    public static IReadOnlyList<Fixture> LastCommit { get; } =
    [
        Modified(".tech/deploy-foundry.md", DeployBefore, DeployAfter),
        Added(".tech/ci-runners.md", RunnersAdded),
        Removed("docs/old-runbook.md", RunbookRemoved)
    ];

    /// <summary>
    /// Three ranges, declared after the two lists above because a static
    /// initializer runs in textual order and these read their counts off them —
    /// a count written out beside a list is a second copy waiting to disagree
    /// with it.
    /// </summary>
    public static IReadOnlyList<ChangeScope> Scopes { get; } =
    [
        new(CommittedScopeId, "Committed", Committed.Count),
        new(LastCommitScopeId, "Last commit", LastCommit.Count),
        new(UncommittedScopeId, "Uncommitted", 0)
    ];

    public static IReadOnlyList<Fixture> For(string? scopeId) => scopeId switch
    {
        LastCommitScopeId => LastCommit,
        UncommittedScopeId => [],
        _ => Committed
    };

    /// <summary>The chapter the centrepiece story draws: an edited paragraph, a
    /// section that went, a section that arrived, and a table, a fence and a
    /// meta block that did not move.</summary>
    public static Fixture Deploy => Committed[0];

    /// <summary>The same body under two different headings.</summary>
    public static Fixture Session => Committed[1];

    /// <summary>A long section with two edits far apart, so most of what is on
    /// screen is what did not happen.</summary>
    public static Fixture Checklist => Committed[2];

    private static Fixture Modified(string path, string before, string after) =>
        Build(path, before, after, ChangeKind.Changed);

    private static Fixture Added(string path, string after) =>
        Build(path, string.Empty, after, ChangeKind.Added);

    private static Fixture Removed(string path, string before) =>
        Build(path, before, string.Empty, ChangeKind.Removed);

    private static Fixture Build(string path, string before, string after, ChangeKind kind)
    {
        var comparison = MarkdownCompare.Compare(before, after);

        var added = 0;
        var removed = 0;
        var changed = 0;

        Tally(comparison, ref added, ref removed, ref changed);

        var separator = path.LastIndexOf('/');

        return new Fixture(
            new ChangedFile(
                path,
                separator < 0 ? path : path[(separator + 1)..],
                separator < 0 ? null : path[..separator],
                kind,
                added,
                removed,
                changed),
            before,
            after);
    }

    /// <summary>Counts headings and blocks together, because the row's "+3 −1
    /// ±2" is answering "how much moved" and a reader asking that is not
    /// distinguishing between the two.</summary>
    private static void Tally(ComparedSection section, ref int added, ref int removed, ref int changed)
    {
        foreach (var kind in section.Blocks.Select(block => block.Kind).Append(section.Kind))
        {
            switch (kind)
            {
                case ChangeKind.Added: added++; break;
                case ChangeKind.Removed: removed++; break;
                case ChangeKind.Changed: changed++; break;
            }
        }

        foreach (var child in section.Children) Tally(child, ref added, ref removed, ref changed);
    }

    // ---------------------------------------------------------------------
    // .tech/deploy-foundry.md — all four states in one document.
    // ---------------------------------------------------------------------

    private const string DeployBefore = """
        # Deploy Foundry

        ```meta
        status: active
        related: [".tech/ci-runners.md"]
        ```

        The workflow that publishes the model catalogue to Azure AI Foundry.

        ## Prerequisites

        An Azure subscription with the Foundry resource provider registered.

        | Secret | Where it comes from |
        | --- | --- |
        | `AZURE_CLIENT_ID` | The federated credential on the app registration |
        | `AZURE_TENANT_ID` | The directory the subscription lives in |

        ## Running the workflow

        Dispatch it from the Actions tab, or let the nightly schedule pick it up.

        ```bash
        gh workflow run deploy-foundry.yml --ref main
        ```

        ## Rolling back

        Re-run the last deployment that succeeded, from its own run page.
        """;

    private const string DeployAfter = """
        # Deploy Foundry

        ```meta
        status: active
        related: [".tech/ci-runners.md"]
        ```

        The workflow that publishes the model catalogue to Azure AI Foundry.

        ## Prerequisites

        An Azure subscription with the Foundry resource provider registered, and a
        self-hosted runner in the deployment pool.

        | Secret | Where it comes from |
        | --- | --- |
        | `AZURE_CLIENT_ID` | The federated credential on the app registration |
        | `AZURE_TENANT_ID` | The directory the subscription lives in |

        ## Running the workflow

        Dispatch it from the Actions tab, or let the nightly schedule pick it up.

        ```bash
        gh workflow run deploy-foundry.yml --ref main
        ```

        ## Watching a run

        Console output streams to the runner's own log. What Foundry did with it is
        in the resource's activity log, which is the half people forget.
        """;

    // ---------------------------------------------------------------------
    // .tech/session-configuration.md — one heading renamed, body untouched.
    // ---------------------------------------------------------------------

    private const string SessionBefore = """
        # Session configuration

        ## Setup

        Copy `.env.example` to `.env` and fill in the two tokens.

        The CLI reads it once on start and never writes back to it, so an edit made
        while a session is running takes effect on the next one.

        ```bash
        cp .env.example .env
        ```
        """;

    private const string SessionAfter = """
        # Session configuration

        ## Getting started

        Copy `.env.example` to `.env` and fill in the two tokens.

        The CLI reads it once on start and never writes back to it, so an edit made
        while a session is running takes effect on the next one.

        ```bash
        cp .env.example .env
        ```
        """;

    // ---------------------------------------------------------------------
    // .tech/release-checklist.md — a long section with two edits far apart.
    // ---------------------------------------------------------------------

    private const string ChecklistBefore = """
        # Release checklist

        ```meta
        status: active
        related: [".tech/deploy-foundry.md"]
        ```

        ## Before you tag

        Run the full suite locally. CI runs the same targets, but a red run there
        costs twenty minutes you could have spent fixing it.

        Check that the changelog names every user-visible change.

        Confirm the version in `Directory.Build.props` matches the tag you are about
        to push.

        Read the open pull requests for anything that was meant to land in this
        release and has not.

        Ask the two people who reviewed the largest change whether they are happy
        for it to ship today.

        Take a copy of the production configuration. Restoring it is cheap;
        re-deriving it is not.

        Announce the freeze in the team channel so nobody merges under you.

        Check that the release notes render the way you expect in the preview.

        Confirm the licence file still lists every bundled dependency.

        Tag the commit, and push the tag rather than the branch.

        Watch the first deployment to staging all the way through, then promote it.

        ## Afterwards

        Close the milestone and open the next one.
        """;

    private const string ChecklistAfter = """
        # Release checklist

        ```meta
        status: active
        related: [".tech/deploy-foundry.md"]
        ```

        ## Before you tag

        Run the full suite locally. CI runs the same targets, but a red run there
        costs twenty minutes you could have spent fixing it.

        Check that the changelog names every user-visible change, and that each one
        links to the pull request it came from.

        Confirm the version in `Directory.Build.props` matches the tag you are about
        to push.

        Read the open pull requests for anything that was meant to land in this
        release and has not.

        Ask the two people who reviewed the largest change whether they are happy
        for it to ship today.

        Take a copy of the production configuration. Restoring it is cheap;
        re-deriving it is not.

        Announce the freeze in the team channel so nobody merges under you.

        Check that the release notes render the way you expect in the preview.

        Confirm the licence file still lists every bundled dependency.

        Tag the commit, and push the tag rather than the branch.

        Watch the first deployment to staging all the way through. Promote it only
        after the smoke suite has gone green against staging, not before.

        ## Afterwards

        Close the milestone and open the next one.
        """;

    // ---------------------------------------------------------------------
    // A whole chapter added, and a whole chapter deleted.
    // ---------------------------------------------------------------------

    private const string RunnersAdded = """
        # CI runners

        ```meta
        status: draft
        related: [".tech/deploy-foundry.md"]
        ```

        Which jobs run where, and why the deployment ones cannot run on the hosted
        pool.

        ## The pools

        | Pool | Used by |
        | --- | --- |
        | `ubuntu-latest` | Build and test |
        | `foundry-deploy` | Anything that touches the Foundry resource |
        """;

    private const string RunbookRemoved = """
        # Old runbook

        The manual deployment steps, kept while the workflow was being written.

        ## Publishing by hand

        Sign in with the deployment account, upload the catalogue, and refresh the
        endpoint. Superseded by `.tech/deploy-foundry.md`.
        """;
}
