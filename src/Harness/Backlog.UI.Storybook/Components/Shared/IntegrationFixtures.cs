using Backlog.UI.Components.Integrations;
using Backlog.UI.Components.Markdown;

namespace Backlog.UI.Storybook.Components.Shared;

/// <summary>
/// The sample integration data the five Integrations pages are drawn from: one
/// backlog entry that has been projected into GitHub, run through two agents,
/// and annotated.
/// </summary>
/// <remarks>
/// <para>
/// One entry rather than a fresh set per page. The whole argument of this section
/// is that the same six acts and the same reference shapes hold across a backlog
/// entry, a knowledge chapter and a roadmap bar, and five pages inventing five
/// unrelated fixtures would quietly undercut it — a reader could not tell whether
/// the density page and the actions page were showing the same acts collapsing or
/// two different sets that happen to look alike.
/// </para>
/// <para>
/// A provider is set here only where work crosses out of the application. The
/// three AI acts and the proposals they produce carry none, because "Ask AI",
/// "Resolve comments with AI" and "Rewrite part with AI" are this product's own
/// features and a Claude or Copilot logo on one would be the sample claiming a
/// hand-off that never happened. The acts that do hand a section to an outside
/// session, the sessions that come back, and the proposals written in them carry
/// the provider they belong to. Getting that backwards in a fixture is how a
/// storybook teaches the wrong rule.
/// </para>
/// <para>
/// No clock and no <c>Random</c>. Every timestamp here is a preformatted string
/// because <c>IntegrationReading.AsOf</c> and <c>AiProposal.Timestamp</c> are
/// preformatted by design — the library holds no clock, so the fixture holds no
/// clock either, and a screenshot taken today compares with one taken next month.
/// </para>
/// <para>
/// Nothing here calls into the library's internals. <c>IntegrationStates</c> is
/// <c>internal</c>, so every sentence a story quotes back — "GitHub is not
/// connected", the four remedy labels — is written out where it is shown rather
/// than read out of the mapper. That is a real cost: the storybook can restate a
/// sentence the library later changes. It is accepted because the alternative is
/// an <c>InternalsVisibleTo</c> for a harness, which would make the library's
/// private vocabulary part of its contract.
/// </para>
/// </remarks>
internal static class IntegrationFixtures
{
    // --- Repositories ------------------------------------------------------

    /// <summary>The repository the entry lives in. The alias is what a heading
    /// says: "owner/name" is mostly punctuation to a reader who already knows
    /// which repository they are looking at.</summary>
    /// <summary>The colours are the workspace's answer, which in the app means the one
    /// chosen in Settings. A fixture stands in for it here because the library declines
    /// to pick — see <c>.design/color-scheme.md#band-identity-tokens</c>.</summary>
    public static readonly IntegrationRepositoryRef ProductRepo =
        new("repo-backlog", "jsdotnet/backlog", "Backlog") { Colour = 1 };

    public static readonly IntegrationRepositoryRef DesktopRepo =
        new("repo-desktop", "jsdotnet/backlog-desktop", "Desktop") { Colour = 2 };

    public static readonly IntegrationRepositoryRef SyncRepo =
        new("repo-sync", "jsdotnet/backlog-sync", "Sync") { Colour = 3 };

    /// <summary>Deliberately without an alias, so one group heading on the
    /// eleven-link story shows what <c>DisplayName</c> falls back to.</summary>
    public static readonly IntegrationRepositoryRef PluginsRepo =
        new("repo-plugins", "jsdotnet/claude-plugins") { Colour = 4 };

    // --- Readiness ---------------------------------------------------------

    public static readonly IntegrationReadiness GitHubNotConnected = IntegrationReadiness.NotAuthorized("GitHub");

    public static readonly IntegrationReadiness VsCodeNotInstalled = IntegrationReadiness.NotInstalled("VS Code");

    public static readonly IntegrationReadiness Offline = IntegrationReadiness.Offline();

    public static readonly IntegrationReadiness GitHubTurnedOff = IntegrationReadiness.FeatureOff("The GitHub integration");

    /// <summary>The cluster readiness the panel's precedence story turns on: one
    /// expired token blocks every act that goes through it, and none of the
    /// references it already read.</summary>
    public static readonly IntegrationReadiness TokenExpired =
        new(IntegrationAvailability.NotAuthorized, "GitHub", "The GitHub token expired on 14 August.", "Reconnect GitHub");

    // --- The six acts ------------------------------------------------------

    /// <summary>Primary, which is the entire mechanism behind "invokable from any
    /// section": it is pinned by the collapse rule rather than mounted by each
    /// surface, so no host has to remember it.
    /// <para>No provider, and that is the rule rather than an omission: asking
    /// this product a question about this entry is a feature of this product.
    /// Nothing crosses out, so nothing is stamped.</para></summary>
    public static readonly IntegrationActionSpec AskAi = new(
        "ask-ai",
        "Ask AI",
        Description: "Ask a question about this entry and everything linked to it.",
        Prominence: IntegrationProminence.Primary);

    /// <summary>The one act with a confirm step, because it is the one that
    /// creates something other people will see.</summary>
    public static readonly IntegrationActionSpec CreateIssue = new(
        "create-issue",
        "Create GitHub issue",
        IntegrationProvider.GitHub,
        Description: "Open an issue in jsdotnet/backlog from this entry.",
        ConfirmLabel: "Create the issue");

    public static readonly IntegrationActionSpec OpenInVsCode = new(
        "open-vscode",
        "Open in VS Code",
        IntegrationProvider.VsCode,
        Description: "Open the working folder for this entry.");

    public static readonly IntegrationActionSpec RunInCopilot = new(
        "run-copilot",
        "Run in Copilot CLI",
        IntegrationProvider.Copilot,
        Description: "Hand the prompt to a local Copilot CLI session.");

    /// <summary>Unmarked, like <see cref="AskAi"/> and for the same reason: the
    /// remark, the reply and the decision all stay in this application.</summary>
    public static readonly IntegrationActionSpec ResolveComments = new(
        "resolve-comments",
        "Resolve comments with AI",
        Description: "Draft a reply to every open remark on this entry.");

    public static readonly IntegrationActionSpec RewritePart = new(
        "rewrite",
        "Rewrite part with AI",
        Description: "Suggest a replacement for one block of the body.");

    // --- The two acts that hand work out -----------------------------------

    /// <summary>The outward pair. Forwarding a section to an agent session in
    /// another repository is the act the marks exist for: the work leaves this
    /// application, arrives somewhere provider-specific, and comes back as a
    /// session on the References page that somebody has to track.
    /// <para>Same shape, same repository, one field apart — so a reader looking
    /// at the two of them can see that the mark is the only difference, and that
    /// it is carrying the whole of the provider-specific part.</para></summary>
    public static readonly IntegrationActionSpec SendToClaude = new(
        "send-claude",
        "Send to Claude in Desktop",
        IntegrationProvider.Claude,
        Description: "Hand this section to a Claude session in jsdotnet/backlog-desktop to implement.",
        ConfirmLabel: "Send it");

    public static readonly IntegrationActionSpec SendToCopilot = new(
        "send-copilot",
        "Send to Copilot in Desktop",
        IntegrationProvider.Copilot,
        Description: "Hand this section to a Copilot session in jsdotnet/backlog-desktop to implement.",
        ConfirmLabel: "Send it");

    /// <summary>The two hand-offs on their own, for the story that draws them
    /// against the unmarked in-app acts.</summary>
    public static IReadOnlyList<IntegrationActionSpec> HandOffs { get; } =
    [
        SendToClaude,
        SendToCopilot
    ];

    /// <summary>The three acts this product performs with its own AI. Not one of
    /// them carries a provider, which is what the mark rule looks like as
    /// data.</summary>
    public static IReadOnlyList<IntegrationActionSpec> InAppAi { get; } =
    [
        AskAi,
        ResolveComments,
        RewritePart
    ];

    /// <summary>Both kinds in one bar, in the order a surface would give them:
    /// what stays in, then what goes out. The marks fall on the second half and
    /// nowhere else, and that is the whole story of the boundary in one row.</summary>
    public static IReadOnlyList<IntegrationActionSpec> InAppAiAndHandOffs { get; } =
    [
        AskAi,
        ResolveComments,
        RewritePart,
        SendToClaude,
        SendToCopilot
    ];

    /// <summary>The exception. <c>CopyText</c> set is what makes a bar render it
    /// with <c>CopyButton</c> instead of as an act, and it is why a copy can never
    /// end up in the overflow menu.</summary>
    public static readonly IntegrationActionSpec CopyPrompt = new(
        "copy-prompt",
        "Copy prompt",
        CopyText: PromptText,
        Description: "Copy the prompt for this entry to the clipboard.");

    /// <summary>Explicitly <see cref="IntegrationProminence.Overflow"/>: it is
    /// removed unconditionally and is never promoted back out, even when it is the
    /// only thing in the menu.</summary>
    public static readonly IntegrationActionSpec UnlinkIssue = new(
        "unlink",
        "Unlink the GitHub issue",
        IntegrationProvider.GitHub,
        Description: "Forget the projection. The issue itself is untouched.",
        Prominence: IntegrationProminence.Overflow,
        Destructive: true);

    /// <summary>The six acts, in the order a surface would give them: the question
    /// first, then the two that reach outside, then the three that write.</summary>
    public static IReadOnlyList<IntegrationActionSpec> Acts { get; } =
    [
        AskAi,
        CreateIssue,
        OpenInVsCode,
        RunInCopilot,
        ResolveComments,
        RewritePart
    ];

    /// <summary>The six plus the copy, which is what a real surface mounts.</summary>
    public static IReadOnlyList<IntegrationActionSpec> ActsWithCopy { get; } =
    [
        AskAi,
        CopyPrompt,
        CreateIssue,
        OpenInVsCode,
        RunInCopilot,
        ResolveComments,
        RewritePart
    ];

    /// <summary>Five acts — one over the Toolbar budget once the Primary is
    /// pinned, which is the case rule 6 collapses back to zero.</summary>
    public static IReadOnlyList<IntegrationActionSpec> FiveActs { get; } =
    [
        AskAi,
        CreateIssue,
        OpenInVsCode,
        RunInCopilot,
        ResolveComments
    ];

    /// <summary>The same six with GitHub unreachable on the two acts that go
    /// through it. Nothing moves: readiness has no effect on placement.</summary>
    public static IReadOnlyList<IntegrationActionSpec> ActsWithGitHubDown { get; } =
    [
        AskAi,
        CreateIssue with { Readiness = GitHubNotConnected },
        OpenInVsCode with { Readiness = VsCodeNotInstalled },
        RunInCopilot,
        ResolveComments,
        RewritePart
    ];

    /// <summary>A bar at the Compact budget with one act in flight. The running
    /// act is third, so without rule 8 it would be the one pushed under the
    /// trigger — which is the whole point of the story it appears in.</summary>
    public static IReadOnlyList<IntegrationActionSpec> ActsWithOneRunning { get; } =
    [
        AskAi,
        CreateIssue,
        RunInCopilot with { State = IntegrationActionState.Running },
        ResolveComments,
        RewritePart
    ];

    // --- References --------------------------------------------------------

    /// <summary>One pull request, used by the tri-state story in all three of its
    /// shapes. Held once so the three cells are provably one reference rendered
    /// three ways rather than three references that happen to match.</summary>
    public static readonly IntegrationLinkRef PullRequest = IntegrationLinkRef.PullRequest(
        "pr-74",
        "PR #74",
        IntegrationArtifactState.Open,
        "Absorb the hand-rolled GitHub badges into one family",
        "https://github.com/jsdotnet/backlog/pull/74",
        repository: ProductRepo);

    /// <summary>A Copilot CLI session: a local process with nothing addressable
    /// to link to, so it arrives with no URL and renders as a button.</summary>
    public static readonly IntegrationLinkRef CopilotSession = IntegrationLinkRef.Session(
        "session-4a1c",
        "session 4a1c",
        IntegrationProvider.Copilot,
        IntegrationSessionState.Running,
        "Migrating BacklogPane onto IntegrationLink",
        repository: ProductRepo);

    public static readonly IntegrationLinkRef ClaudeSession = IntegrationLinkRef.Session(
        "session-9f30",
        "session 9f30",
        IntegrationProvider.Claude,
        IntegrationSessionState.Running,
        "Drafting the storybook section",
        repository: ProductRepo);

    /// <summary>What the Desktop hand-off comes back as. The act that sent the
    /// section out and this reference are the two halves of one crossing, which
    /// is why both carry a mark: you hand work out, and what you get back is a
    /// session somebody has to track.</summary>
    public static readonly IntegrationLinkRef DesktopClaudeSession = IntegrationLinkRef.Session(
        "session-7e12",
        "session 7e12",
        IntegrationProvider.Claude,
        IntegrationSessionState.Running,
        "Absorbing the improvised badges in the desktop pane",
        repository: DesktopRepo);

    /// <summary>The same crossing through the other provider, so the pair on the
    /// Actions page differs in one field and nothing else.</summary>
    public static readonly IntegrationLinkRef DesktopCopilotSession = IntegrationLinkRef.Session(
        "session-3c85",
        "session 3c85",
        IntegrationProvider.Copilot,
        IntegrationSessionState.Waiting,
        "Absorbing the improvised badges in the desktop pane",
        repository: DesktopRepo);

    /// <summary>The drift case with a note the host wrote. The default sentence
    /// is general on purpose; this one names the entry.</summary>
    public static readonly IntegrationLinkRef DriftedIssue = IntegrationLinkRef.Issue(
        "issue-128",
        "#128",
        IntegrationArtifactState.Open,
        "Integration affordances are improvised per surface",
        "https://github.com/jsdotnet/backlog/issues/128",
        IntegrationDrift.LocalAhead,
        "This entry was marked done on 12 August; the issue is still open.",
        ProductRepo);

    /// <summary>Exactly one reference — the case where a repository heading would
    /// be a heading over nothing.</summary>
    public static IReadOnlyList<IntegrationLinkRef> OneLink { get; } = [PullRequest];

    /// <summary>Four references, all in one repository. Grouping is on and the
    /// heading is still suppressed, which is the rule stated as data.</summary>
    public static IReadOnlyList<IntegrationLinkRef> OneRepository { get; } =
    [
        DriftedIssue,
        PullRequest,
        CopilotSession,
        ClaudeSession
    ];

    /// <summary>Eleven references across four repositories, one of them with no
    /// repository at all — which sorts last under its own heading rather than
    /// being dropped.</summary>
    public static IReadOnlyList<IntegrationLinkRef> ManyLinks { get; } =
    [
        DriftedIssue,
        PullRequest,
        IntegrationLinkRef.Issue("issue-131", "#131", IntegrationArtifactState.Closed,
            "Badge slugs are computed in three places", "https://github.com/jsdotnet/backlog/issues/131",
            repository: ProductRepo),

        IntegrationLinkRef.PullRequest("pr-58", "PR #58", IntegrationArtifactState.Merged,
            "Ship the provider marks", "https://github.com/jsdotnet/backlog-desktop/pull/58",
            repository: DesktopRepo),
        IntegrationLinkRef.PullRequest("pr-61", "PR #61", IntegrationArtifactState.Draft,
            "Compact density on the entry list", "https://github.com/jsdotnet/backlog-desktop/pull/61",
            repository: DesktopRepo),

        IntegrationLinkRef.Issue("issue-12", "#12", IntegrationArtifactState.Open,
            "Projection refs need a repo_id", "https://github.com/jsdotnet/backlog-sync/issues/12",
            IntegrationDrift.RemoteAhead, "The issue was closed on 15 August; this entry is not done.",
            SyncRepo),
        IntegrationLinkRef.PullRequest("pr-19", "PR #19", IntegrationArtifactState.Unknown,
            "Read projection state on demand", "https://github.com/jsdotnet/backlog-sync/pull/19",
            repository: SyncRepo),

        IntegrationLinkRef.Issue("issue-7", "#7", IntegrationArtifactState.Open,
            "Orchestration gate wording", "https://github.com/jsdotnet/claude-plugins/issues/7",
            IntegrationDrift.Detached, "The projection points at an issue that is no longer there.",
            PluginsRepo),

        CopilotSession,
        ClaudeSession,

        // A session with nowhere to live: a local process started from a scratch
        // folder that is in no repository at all.
        IntegrationLinkRef.Session("session-2b77", "session 2b77", IntegrationProvider.Copilot,
            IntegrationSessionState.Waiting, "Asked which branch to base the change on")
    ];

    // --- Readings ----------------------------------------------------------

    public static readonly IntegrationReading ReadRecently = new("4 minutes ago");

    public static readonly IntegrationReading Reading = new("4 minutes ago", InFlight: true);

    /// <summary>A read that could not finish, keeping the timestamp of the one
    /// that did. "We could not check" does not make what we knew untrue.</summary>
    public static readonly IntegrationReading ReadFailed =
        new("4 minutes ago", FailureReason: "Could not reach github.com.");

    // --- AI ----------------------------------------------------------------

    /// <summary>What "Copy prompt" puts on the clipboard, and what "Run in Copilot
    /// CLI" hands over. One string for both, because they are the same prompt
    /// going two ways — which is exactly why one Usage Event would record them
    /// with the same subject and a different action.</summary>
    public const string PromptText = """
        Migrate BacklogPane.razor off the hand-rolled .badge--gh-{state} anchors
        and onto IntegrationLink, keeping the existing test ids.
        """;

    /// <summary>Written by this product's own AI, so no provider, no model and no
    /// session: the request never left the application, and the card attributes
    /// it to "AI". Naming a vendor here would be the sample telling a reader the
    /// paragraph went somewhere it did not go.</summary>
    public static readonly AiProposal RewriteProposal = new(
        "proposal-rewrite-1",
        AiProposalKind.Rewrite,
        "The badge markup is improvised on every surface that shows it, so a state "
        + "reads one way on the entry list and another in the knowledge pane. Absorbing "
        + "it into one family is what makes the two agree.",
        Timestamp: "16 Aug, 09:41",
        Original: "The badge markup is a bit messy in places and should probably be tidied up "
        + "at some point.",
        BlockIndex: 2);

    public static readonly AiProposal CommentProposal = new(
        "proposal-comment-1",
        AiProposalKind.CommentResolution,
        "Grouping only kicks in above one repository, so a single-repository entry "
        + "renders a flat list and no heading. The heading appears the moment a second "
        + "repository does.",
        Timestamp: "16 Aug, 09:44",
        CommentId: "comment-2");

    /// <summary>A proposal pointing past the end of the document — the block it
    /// was written against has since been deleted. In-app like the other two:
    /// losing its anchor is not the same as leaving the building.</summary>
    public static readonly AiProposal OrphanedProposal = new(
        "proposal-rewrite-2",
        AiProposalKind.Rewrite,
        "Drift is a second chip beside the first rather than a colour on it, "
        + "because the artifact's own state is still correct.",
        Timestamp: "16 Aug, 10:02",
        Original: "Drift should probably recolour the badge.",
        BlockIndex: 42);

    /// <summary>The only proposal here that carries a provider, and the reason it
    /// does is the whole rule: this one came back from the Claude session the
    /// Desktop hand-off started. It was written outside, by a named tool, in a
    /// session that is itself a tracked reference — so it is attributed to the
    /// tool that wrote it and marked accordingly.</summary>
    public static readonly AiProposal ForwardedProposal = new(
        "proposal-forwarded-1",
        AiProposalKind.Rewrite,
        "BacklogPane draws GitHub state with .badge--gh-{state} anchors of its own. "
        + "Replacing them with IntegrationLink keeps the existing test ids and drops "
        + "the GitHubStateClass helper entirely.",
        IntegrationProvider.Claude,
        "claude-opus-5",
        "session-7e12",
        "16 Aug, 10:14",
        "The badge markup is a bit messy in places and should probably be tidied up "
        + "at some point.",
        BlockIndex: 2);

    // --- The document the AI page annotates --------------------------------

    public const string Document = """
        ## Absorbing the improvised badges

        Two surfaces already draw GitHub state by hand, and they disagree with each
        other about what open looks like.

        The badge markup is a bit messy in places and should probably be tidied up at
        some point.

        Grouping references by repository is the part nobody has written down.
        """;

    /// <summary>Parsed once. These are constants in practice, and re-parsing per
    /// render would put a parser call in the middle of every interaction on the
    /// page.</summary>
    public static IReadOnlyList<MdBlock> DocumentBlocks { get; } = MarkdownPreview.ParseDocument(Document);

    public static IReadOnlyList<MarkdownComment> Comments { get; } =
    [
        new("comment-1", 1, "Which two surfaces?", "Jos", "15 Aug, 16:20"),
        new("comment-2", 3, "Does this hold when there is only one repository?", "Jos", "16 Aug, 09:12")
    ];
}
