namespace Backlog.UI.Components.Integrations;

/// <summary>
/// The outside tool an affordance is about.
/// <para>
/// <see cref="None"/> is a real member and not an oversight: plenty of acts —
/// "Ask AI" on a surface that has not chosen a model yet, a copy — belong to no
/// provider at all, and a mark drawn for them would be a mark that says nothing.
/// <c>ProviderMark</c> renders literally nothing for it, so a bar of
/// provider-less actions has no phantom gap where a glyph would have been.
/// </para>
/// </summary>
public enum IntegrationProvider
{
    None,
    GitHub,
    Copilot,
    Claude,
    VsCode
}

/// <summary>What kind of thing a reference points at. Only used to pick a glyph:
/// an open issue and an open pull request are the same state and the same word,
/// and at Compact density the glyph is the only thing that separates them.</summary>
public enum IntegrationLinkKind
{
    Issue,
    PullRequest,
    Session,
    Other
}

/// <summary>
/// The state of an issue or a pull request.
/// <para>
/// <see cref="Merged"/> is kept apart from <see cref="Closed"/> for the reason
/// the product's own GitHub vocabulary already gives: for a pull request they
/// mean opposite things, and a reader who saw one word for both would have to
/// open the thing to find out which happened.
/// </para>
/// <para>
/// <see cref="Unknown"/> is a state rather than a null. "We have not looked" and
/// "it is closed" are different facts, and a reader is entitled to know which of
/// the two they are being shown.
/// </para>
/// </summary>
public enum IntegrationArtifactState
{
    Unknown,
    Open,
    Draft,
    Merged,
    Closed
}

/// <summary>
/// The state of an agent session — a Copilot CLI run, a Claude session.
/// </summary>
public enum IntegrationSessionState
{
    Unknown,
    Starting,
    Running,

    /// <summary>The session asked a question and nobody has answered it. Correct
    /// behaviour, and the reason <see cref="Stalled"/> exists as a separate
    /// member rather than the two collapsing into one "idle".</summary>
    Waiting,

    /// <summary>Running, but nothing has moved for longer than it should have.
    /// <para>
    /// <b>Computed by the host, never here.</b> A threshold is a policy and a
    /// policy needs a clock; this library has neither, and a timer added to a
    /// component would put the product's monitoring rule in the one place nobody
    /// would think to look for it. The host decides and passes this member in —
    /// so nothing in this family ever advances a session into it on its own.
    /// </para></summary>
    Stalled,

    Finished,
    Failed
}

/// <summary>
/// Why the product cannot perform an act.
/// <para>
/// <see cref="FeatureOff"/> rather than <c>Disabled</c>: <c>Disabled</c> already
/// means something on every button in this library, and the two words next to
/// each other would be read as the same thing when they are not — one is a
/// setting the reader can go and change, the other is a control that is inert.
/// </para>
/// </summary>
public enum IntegrationAvailability
{
    Available,
    NotAuthorized,
    NotInstalled,
    Offline,
    FeatureOff
}

/// <summary>Where one act has got to. The host owns this and hands it in; no
/// component in this family advances it.</summary>
public enum IntegrationActionState
{
    Idle,
    Confirming,
    Running,
    Succeeded,
    Failed
}

/// <summary>
/// How a reference disagrees with what the product believes locally.
/// <para>
/// Drift is always <em>about</em> a specific link, so it has no life away from
/// one: it is an enum and a parameter on <c>IntegrationLink</c> rather than a
/// component of its own, and it draws a second chip beside the artifact's own —
/// which is exactly what "a state distinct from the artifact's" looks like on a
/// screen.
/// </para>
/// </summary>
public enum IntegrationDrift
{
    None,
    LocalAhead,
    RemoteAhead,
    Detached
}

/// <summary>
/// How much room the surface is giving. It selects a button shape, a size and a
/// visible budget together, because those three always move together and a host
/// that could set them separately would eventually set two of them wrong.
/// </summary>
public enum IntegrationDensity
{
    /// <summary>Icon and label, default size, four visible. A section header, an
    /// entry detail pane.</summary>
    Toolbar,

    /// <summary>Icon and label, small, three visible. A row inside content, a
    /// knowledge chapter footer.</summary>
    Inline,

    /// <summary>Icon only, small, two visible. A backlog list row, a roadmap
    /// bar. The label is still there — on the accessible name and the title —
    /// because an icon on its own says nothing.</summary>
    Compact,

    /// <summary>No visible budget at all: the set is one trigger inside a host's
    /// own menu. Pinned acts still show, which is what keeps copy and Ask AI
    /// reachable from a menu-density surface.</summary>
    Menu
}

/// <summary>How an act competes for the visible budget.</summary>
public enum IntegrationProminence
{
    /// <summary>Pinned visible at every density. This is the whole mechanism
    /// behind "Ask AI is invokable from any section": it is a property of the
    /// collapse rule rather than of any component, so no surface has to remember
    /// to mount it.</summary>
    Primary,

    /// <summary>Fills the budget in the order it was given.</summary>
    Standard,

    /// <summary>Removed from the visible list unconditionally.</summary>
    Overflow
}

/// <summary>What an AI wrote, and therefore what accepting it would do.</summary>
public enum AiProposalKind
{
    Rewrite,
    CommentResolution,
    Answer
}

/// <summary>
/// Whether a proposal is still a question.
/// <para>
/// <see cref="Accepted"/> and <see cref="Rejected"/> are kept rather than the
/// card disappearing, because the difference between provenance and a
/// confirmation dialog is what survives the click.
/// </para>
/// </summary>
public enum AiProposalState
{
    Proposed,
    Accepted,
    Rejected
}

/// <summary>
/// Whether the product can perform an act at all, and if not, why — the one
/// primitive behind all four causes.
/// <para>
/// It exists so that <c>IntegrationAction</c> can have no <c>bool Disabled</c>
/// parameter. Every other button in this library takes one, and that is why an
/// unavailable integration looks different on every surface that has one: a
/// greyed control says nothing about why, so each host invents its own tooltip,
/// or its own hidden branch, or nothing at all. The rejected alternative was a
/// <c>Disabled</c> plus an optional <c>DisabledReason</c>, which is the same
/// shape with a nicer name — optional means a host can skip it, and every host
/// eventually will. This record cannot be constructed without a cause, so the
/// bare disabled integration button is not something a host can produce.
/// </para>
/// <para>
/// No <c>EventCallback</c> on it. Callbacks live on components; keeping this a
/// plain value record is what lets a host compare two readinesses, and lets the
/// panel decide precedence without invoking anything.
/// </para>
/// </summary>
/// <param name="Availability">The cause.</param>
/// <param name="Subject">What is unavailable — "GitHub", "VS Code", "Copilot
/// CLI". It reads into the default sentence, so a host that passes only a cause
/// and a subject still gets a sentence rather than a bare disabled control.</param>
/// <param name="Reason">The sentence, where the default one is not right for the
/// surface. Optional on purpose: the default is always available, so the reason
/// can never go missing the way an optional <c>DisabledReason</c> could.</param>
/// <param name="RemedyLabel">What the way out is called. A remedy only renders
/// where a component was also given a delegate to raise — a label with nothing
/// behind it would be a button that refuses.</param>
public sealed record IntegrationReadiness(
    IntegrationAvailability Availability,
    string? Subject = null,
    string? Reason = null,
    string? RemedyLabel = null)
{
    /// <summary>Nothing is in the way. The default for every act that does not
    /// say otherwise.</summary>
    public static readonly IntegrationReadiness Ready = new(IntegrationAvailability.Available);

    public static IntegrationReadiness NotAuthorized(string subject, string? remedy = null) =>
        new(IntegrationAvailability.NotAuthorized, subject, RemedyLabel: remedy);

    public static IntegrationReadiness NotInstalled(string subject, string? remedy = null) =>
        new(IntegrationAvailability.NotInstalled, subject, RemedyLabel: remedy);

    /// <summary>No subject, because offline is not about one tool — and no
    /// remedy either, because there is nothing a reader can press that would put
    /// the network back.</summary>
    public static IntegrationReadiness Offline() => new(IntegrationAvailability.Offline);

    public static IntegrationReadiness FeatureOff(string subject, string? remedy = null) =>
        new(IntegrationAvailability.FeatureOff, subject, RemedyLabel: remedy);

    public bool IsAvailable => Availability is IntegrationAvailability.Available;
}

/// <summary>
/// One outward act, as a value.
/// <para>
/// A surface adds an integration by adding a record to a list, not by adding
/// markup — which is the only reason the same six acts can look the same on a
/// backlog entry, a knowledge chapter and a roadmap bar.
/// </para>
/// </summary>
/// <param name="Id">Stable across renders, and the identifier a host would log
/// against. The domain's <c>Usage Event</c> — the audit record of a prompt copy
/// or a hand-off — has no hook in this library yet, and this is where it attaches
/// when it gets one.</param>
/// <param name="Label">What it is called, in the imperative: "Create GitHub
/// issue", not "GitHub issue".</param>
/// <param name="Provider">Selects the mark. <see cref="IntegrationProvider.None"/>
/// draws nothing at all.</param>
/// <param name="Description">A longer sentence, for a title or a menu row.</param>
/// <param name="Prominence">How it competes for the visible budget.</param>
/// <param name="Readiness">Null means <see cref="IntegrationReadiness.Ready"/>.
/// Nullable rather than defaulted to that static, because a record parameter
/// default has to be a compile-time constant.</param>
/// <param name="State">Where the act has got to. Owned by the host.</param>
/// <param name="ConfirmLabel">Non-null makes the first press a confirmation
/// rather than the act itself.</param>
/// <param name="StatusMessage">The line a succeeded or failed act leaves behind.
/// Null takes a default built from <paramref name="Label"/>.</param>
/// <param name="CopyText">Non-null means this act is a clipboard copy, and a bar
/// renders it with <c>CopyButton</c> rather than as an action. That component
/// already owns the interop, the prerender and <c>JSException</c> swallowing, the
/// <c>role="status"</c> line and the three-second clear; re-implementing any of
/// it here is precisely the duplication this family exists to prevent.</param>
/// <param name="Destructive">Colours the confirmation as a danger rather than a
/// primary. It only reaches the confirm step: an idle destructive act still looks
/// like an act.</param>
public sealed record IntegrationActionSpec(
    string Id,
    string Label,
    IntegrationProvider Provider = IntegrationProvider.None,
    string? Description = null,
    IntegrationProminence Prominence = IntegrationProminence.Standard,
    IntegrationReadiness? Readiness = null,
    IntegrationActionState State = IntegrationActionState.Idle,
    string? ConfirmLabel = null,
    string? StatusMessage = null,
    string? CopyText = null,
    bool Destructive = false)
{
    /// <summary>The readiness with null read as ready. Every component in this
    /// family reads it through here, so "null means ready" is decided once.</summary>
    public IntegrationReadiness ReadinessOrReady => Readiness ?? IntegrationReadiness.Ready;

    /// <summary>Whether this act is a clipboard copy, and so <c>CopyButton</c>'s
    /// rather than <c>IntegrationAction</c>'s.</summary>
    public bool IsCopy => CopyText is not null;
}

/// <summary>Where a linked artifact lives. Carries the projection's
/// <c>repo_id</c>, so a host groups by the same identity it stores.</summary>
public sealed record IntegrationRepositoryRef(
    string Id,
    string FullName,
    string? Alias = null)
{
    /// <summary>What a group heading says: the alias where a host has one,
    /// because "owner/name" is mostly punctuation to a reader who already knows
    /// which repository they are looking at.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? FullName : Alias;

    /// <summary>
    /// Which of the sanctioned identity hues this repository wears, 1 to 5, or null
    /// when the host has not said.
    /// <para>
    /// The number arrives from the caller for the same reason a roadmap group's colour
    /// does: the library declines to pick these. Which repository is which is a
    /// workspace question, and a component that worked it out would be a second answer
    /// to it. Null draws no mark, which is the honest rendering of "nobody said".
    /// </para>
    /// </summary>
    public int? Colour { get; init; }
}

/// <summary>
/// A reference to something that lives outside the product.
/// <para>
/// Two nullable state fields on one flat record, with factories that make the
/// wrong pairing unreachable — rather than a base record with three derived
/// ones. That is the house pattern, and <c>MenuItem</c>'s own docs defend it: a
/// flat record with a discriminator and a factory beats "a second type the
/// caller has to reason about".
/// </para>
/// </summary>
/// <param name="Id">The projection's <c>external_id</c>.</param>
/// <param name="Provider">Selects the mark.</param>
/// <param name="Kind">Selects the glyph for the states that have two of them.</param>
/// <param name="Label">Short and scannable: "#128", "PR #74", "session 4a1c".</param>
/// <param name="Title">The issue, pull request or session title. Dropped at
/// Compact density, where there is no room for it.</param>
/// <param name="Url">Where it is, if it is anywhere addressable. A Copilot CLI
/// session is a local process with nothing to link to, so it arrives with no URL
/// and renders as a button instead — which is why the tri-state renderer earns
/// its keep here rather than being copied for symmetry.</param>
/// <param name="ArtifactState">Set for an issue or a pull request.</param>
/// <param name="SessionState">Set for a session.</param>
/// <param name="Drift">How the artifact disagrees with local truth.</param>
/// <param name="DriftNote">The host's own sentence about the disagreement, where
/// the default is too general to act on.</param>
/// <param name="Repository">Where it lives. Drives grouping, and only becomes a
/// heading when there is more than one.</param>
public sealed record IntegrationLinkRef(
    string Id,
    IntegrationProvider Provider,
    IntegrationLinkKind Kind,
    string Label,
    string? Title = null,
    string? Url = null,
    IntegrationArtifactState? ArtifactState = null,
    IntegrationSessionState? SessionState = null,
    IntegrationDrift Drift = IntegrationDrift.None,
    string? DriftNote = null,
    IntegrationRepositoryRef? Repository = null)
{
    public static IntegrationLinkRef Issue(
        string id,
        string label,
        IntegrationArtifactState state,
        string? title = null,
        string? url = null,
        IntegrationDrift drift = IntegrationDrift.None,
        string? driftNote = null,
        IntegrationRepositoryRef? repository = null) =>
        new(id, IntegrationProvider.GitHub, IntegrationLinkKind.Issue, label, title, url,
            ArtifactState: state, SessionState: null, Drift: drift, DriftNote: driftNote, Repository: repository);

    public static IntegrationLinkRef PullRequest(
        string id,
        string label,
        IntegrationArtifactState state,
        string? title = null,
        string? url = null,
        IntegrationDrift drift = IntegrationDrift.None,
        string? driftNote = null,
        IntegrationRepositoryRef? repository = null) =>
        new(id, IntegrationProvider.GitHub, IntegrationLinkKind.PullRequest, label, title, url,
            ArtifactState: state, SessionState: null, Drift: drift, DriftNote: driftNote, Repository: repository);

    public static IntegrationLinkRef Session(
        string id,
        string label,
        IntegrationProvider provider,
        IntegrationSessionState state,
        string? title = null,
        string? url = null,
        IntegrationDrift drift = IntegrationDrift.None,
        string? driftNote = null,
        IntegrationRepositoryRef? repository = null) =>
        new(id, provider, IntegrationLinkKind.Session, label, title, url,
            ArtifactState: null, SessionState: state, Drift: drift, DriftNote: driftNote, Repository: repository);
}

/// <summary>
/// What one deliberate read of external state produced.
/// <para>
/// State is read on demand and never polled, so a reading is an event with a
/// time on it rather than a value that keeps itself true. That is why the record
/// carries <paramref name="AsOf"/> at all: without it a chip would be claiming to
/// be current, and nothing in this product makes it current.
/// </para>
/// </summary>
/// <param name="AsOf">Already formatted. What "4 minutes ago" is, and in what
/// language, belongs to the host — the same division <c>MarkdownComment</c>
/// already makes for its timestamps.</param>
/// <param name="InFlight">A read is happening now.</param>
/// <param name="FailureReason">The last read did not finish. Still reported as a
/// status and never as an alert: a read that could not reach GitHub is the same
/// class of thing as being offline, which the design principles require be "a
/// calm, persistent status — not an error modal".</param>
public sealed record IntegrationReading(
    string? AsOf = null,
    bool InFlight = false,
    string? FailureReason = null)
{
    /// <summary>Nobody has looked yet. The default everywhere, because it is the
    /// truth on the first render of every surface.</summary>
    public static readonly IntegrationReading Never = new();
}

/// <summary>
/// AI-written content held for review.
/// <para>
/// The act <em>starts</em> the work; the proposal <em>is</em> the result, and it
/// outlives the act's lifecycle — which is why it is a record and a card of its
/// own rather than a sixth state on <c>IntegrationActionSpec</c>.
/// </para>
/// <para>
/// Three properties follow from what it carries. It is <b>attributable</b>: the
/// card renders the provider, the model and the time, visibly and on the
/// article's accessible name. It is <b>reversible</b>: <paramref name="Original"/>
/// is what makes accept-or-reject a review rather than a gamble, and nothing is
/// applied in place. And it is <b>durable</b>: accepting sets the state and keeps
/// the attribution, because six months later "did a person write this paragraph"
/// is a question the document has to be able to answer.
/// </para>
/// <para>
/// The host's mapping onto the domain's AI Work Log needs no library knowledge of
/// it: provider to <c>ai_tool</c>, kind to <c>activity_kind</c>, timestamp to
/// <c>timestamp</c>, session to <c>session_id</c>, and an accepted proposal's id
/// to <c>outcome_ref</c>.
/// </para>
/// </summary>
/// <param name="Id">Unique within the view, and what a callback reports.</param>
/// <param name="Kind">What accepting it would do.</param>
/// <param name="Body">What is being suggested.</param>
/// <param name="Provider">Who suggested it — named only when that is somebody
/// outside this application, because a vendor mark is a passport stamp. A
/// proposal written by the product's own AI carries
/// <see cref="IntegrationProvider.None"/>: no mark is drawn and the card
/// attributes it to "AI", full stop. "Ask AI", "Resolve comments with AI" and
/// "Rewrite part with AI" are features of this product rather than errands run
/// at a vendor, and stamping one with a model vendor's logo would tell a reader
/// the paragraph left the building when it never did. A proposal that came back
/// from a session this product forwarded work to carries the provider it came
/// from, because that one did leave. Hence the default: unmarked, and a host has
/// to say a provider out loud to brand a suggestion with one.</param>
/// <param name="Model">Which model, where the host knows.</param>
/// <param name="SessionId">Lines up with the AI Work Log's <c>session_id</c>.</param>
/// <param name="Timestamp">Already formatted, as everywhere else here.</param>
/// <param name="Original">What it would replace. Optional, because an Answer
/// replaces nothing — but without it a rewrite is a gamble rather than a review.</param>
/// <param name="BlockIndex">Anchors a <see cref="AiProposalKind.Rewrite"/> to a
/// block of the view. A block and not a character range, and that is forced
/// rather than chosen: <c>MarkdownComment</c> records that a range survives
/// nothing, because inserting a word above it moves every offset below. Which is
/// why "rewrite part with AI" means rewrite this paragraph, and can never mean
/// rewrite this sentence.</param>
/// <param name="CommentId">Anchors a <see cref="AiProposalKind.CommentResolution"/>
/// to a comment, so it renders inside that comment rather than against the
/// block.</param>
/// <param name="State">Whether it is still a question.</param>
public sealed record AiProposal(
    string Id,
    AiProposalKind Kind,
    string Body,
    IntegrationProvider Provider = IntegrationProvider.None,
    string? Model = null,
    string? SessionId = null,
    string? Timestamp = null,
    string? Original = null,
    int? BlockIndex = null,
    string? CommentId = null,
    AiProposalState State = AiProposalState.Proposed);
