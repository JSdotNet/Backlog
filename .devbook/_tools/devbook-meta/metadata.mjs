// metadata.mjs — parsing and validation for the chapter/file `meta` YAML
// blocks defined in devbook-chapter-metadata.md.
//
// Dependency-free ESM against node built-ins, like everything else executable
// here: `node:crypto` is what fingerprints an approved chapter's content.
import { createHash } from "node:crypto";
//
// The schema used across .devbook/{arc42,domain,tech,design,ai} is intentionally small and
// flat (single-line scalars, null, or bracket lists), so we parse it with a
// tiny hand-written reader instead of pulling in a YAML dependency.

// The approval gate's rung, sitting on top of every folder's own ladder. A
// chapter reaches it when a person has approved the content — the decision the
// flow layer's gate makes before a chapter becomes work. It is deliberately one
// rung shared by every folder rather than a per-folder word: what is being
// approved is the chapter, and the ladder underneath it says what kind of thing
// the chapter is.
//
// It is never a resting value. A chapter states `approved` while the approval
// stands, and drops back to its ordinary rung the moment the content changes —
// an approval is of what was read, not of the heading.
/**
 * The five devbook folders, by kind. A repository adopts any subset, and every
 * one of them lives under the one `.devbook/` parent: `.devbook/arc42`,
 * `.devbook/domain`, and so on. The parent already carries the "hidden support
 * directory" signal, so the subfolders drop the dot. There is no other layout
 * (record 80); five root-level dot-folders are reported, never indexed.
 */
export const DEVBOOK_FOLDER_NAMES = ["arc42", "domain", "tech", "design", "ai"];

/** The one parent folder, and the prefix that identifies a path inside it. */
export const DEVBOOK_ROOT = ".devbook";
export const DEVBOOK_PREFIX = `${DEVBOOK_ROOT}/`;

const APPROVED_STATUS = "approved";

// One rung above `approved`, and the two are a stack rather than a choice:
// `approved` says the specification is right, `accepted` says what was built
// satisfies it. They are usually stated by different people on different days,
// which is why the acceptance never replaces the approval record — a chapter
// at `accepted` carries both. Acceptance is of the chapter's content, not of a
// commit; which pull request delivered it is the tracker's business.
const ACCEPTED_STATUS = "accepted";

// The two decision rungs sit on `domain/`'s ladder and on no other. What they
// record is that a person agreed the model, and then that what was built
// satisfies it — a question the domain folder is the only one currently asked.
// The other four ladders rate content or a technology, and a rung on them was
// surface nothing used.
const STATUS_BY_FOLDER = {
    domain: ["draft", "proposed", "active", "deprecated", APPROVED_STATUS, ACCEPTED_STATUS],
    arc42: ["draft", "proposed", "active", "deprecated"],
    tech: ["candidate", "trial", "adopted", "hold", "retired"],
    design: ["draft", "active", "deprecated"],
    // `.ai` deliberately reuses `.tech`'s ladder: a reader learns one
    // adoption vocabulary. What is on the ladder differs — `.tech` rates a
    // technology, `.ai` rates a way of working with one.
    ai: ["candidate", "trial", "adopted", "hold", "retired"],
};

// Who approved, and on what day. The gate writes both; they exist so the
// decision travels with the content and lands in the git history, rather than
// living in flow configuration or in someone's memory.
const APPROVAL_FIELDS = ["approved-by", "approved-at"];

// What was approved, as a fingerprint of the content itself. Deliberately
// *not* in APPROVAL_FIELDS: those two are written together and are missing
// together, while this one is optional everywhere. A repository that omits it
// keeps the rule "the rung comes off when the content changes" as something a
// person remembers; a repository that writes it has the checker say so.
//
// The rung already claimed the content had not changed since `approved-at`,
// and nothing could establish it: git answers per file, not per chapter, so a
// chapter in a busy file reads as stale and a chapter in a quiet one reads as
// current whatever either actually is. A fingerprint of the chapter needs no
// git and is exact.
const CONTENT_HASH_FIELD = "approved-hash";

// Who accepted the built work against this chapter, and on what day — the
// approval triad's shape, one rung up, for the same reason: the statement
// travels with the content instead of living in a tracker this repository
// cannot read. `accepted-hash` is the same fingerprint as `approved-hash`,
// computed by the same function, and the two are equal whenever both are
// written — an acceptance is of the approved content.
const ACCEPTANCE_FIELDS = ["accepted-by", "accepted-at"];
const ACCEPTED_HASH_FIELD = "accepted-hash";

// The rungs' six record fields, scoped to `domain/` with them. Kept as one
// list so the folder that has the rungs and the folder that has the fields can
// never drift apart.
const DECISION_FIELDS = [
    ...APPROVAL_FIELDS,
    CONTENT_HASH_FIELD,
    ...ACCEPTANCE_FIELDS,
    ACCEPTED_HASH_FIELD,
];

// `sha256:` names the algorithm so a later one can be told apart, and eight
// hex characters is the whole digest a reader ever compares: this detects an
// edit, it does not defend against one, and nobody is forging a chapter past
// their own approval gate.
const CONTENT_HASH_PATTERN = /^sha256:[0-9a-f]{8}$/;

// Where a chapter's review stands, who owes the next move, and since when. The
// triad mirrors the approval triad on purpose — a chapter reads the same way on
// its way to a decision as it does past one — and, like it, is devbook's
// vocabulary written by the review workflow layered on top (record 77). Each
// state names who is waiting: `requested` the reviewer, `changes-requested`
// the author, `cleared` nobody. The notes in the chapter body are the evidence
// a state stands on, so the two are checked against each other below.
const REVIEW_FIELD = "review";
const REVIEW_STATES = ["requested", "changes-requested", "cleared"];
const REVIEW_FIELDS = [REVIEW_FIELD, "reviewer", "review-at"];

// The value a folder's content settles on, which is therefore *omitted* rather
// than written. A folder listed here makes `status` optional: absence means the
// resting value, and writing it out restates what absence already says.
//
// Only the three editorial folders have such a value. In `.domain`, `.arc42`,
// and `.design` `status` records how settled the writing is, and `active` — "no
// longer in transition" — is the state most chapters sit in forever. The other
// two folders have no resting value to omit: in `.tech` and `.ai` the value is
// a *rating* whose whole purpose is to be stated, so an absent status would be
// indistinguishable from `candidate` ("nobody has rated this") and a radar built
// from omissions renders blank.
//
// Note what is *not* resting: `deprecated` is a standing warning and stays
// written, as do `draft` and `proposed`, which say the content is in transition.
const RESTING_STATUS_BY_FOLDER = {
    domain: "active",
    arc42: "active",
    design: "active",
};

// Allowed `type` values per folder, split by block level. `type` records *what
// kind of thing* a chapter or file is — the classification that used to be
// written as a heading prefix (`## Aggregate: Order`). Heading text now carries
// the name alone, so anchors are slugs of the bare name.
//
// A folder whose lists are empty defines no kind distinction of its own: in
// `.arc42` and `.design` the only such distinction (chapter vs section) is
// already carried by heading level, so inventing
// values there would restate the document structure. `type` is omitted in those
// folders and reported when used.
const TYPE_BY_FOLDER = {
    domain: {
        chapter: [
            // `context-map.md`'s own sections: one per bounded context. The
            // strategic view names contexts, so a chapter elsewhere can address
            // one — `.devbook/domain/context-map.md#order-management` — the same way it
            // addresses an aggregate.
            "bounded-context",
            "aggregate",
            "entity",
            "value-object",
            "enum",
            "shared-value-objects",
            "shared-enums",
            "ubiquitous-language",
            "domain-service",
            "domain-event",
            "feature",
            "sub-feature",
            // `requirements.md` and `domain.invariants.md`: behaviour, one rule per
            // chapter, out of the prose the rest of the context is written in.
            // The plural is the per-feature or per-aggregate grouping chapter,
            // whose `related` names the prose chapter it belongs to; the
            // singular is one rule. A `requirement` promises something to
            // someone outside the model and keeps OpenSpec's word along with
            // its `### Requirement:` / `#### Scenario:` heading shape; an
            // `invariant` is what a type guarantees no matter who calls it and
            // takes DDD's, because the aggregate is what answers for it.
            "requirements",
            "requirement",
            "invariants",
            "invariant",
            // `actors.md`: who works with this context. An actor is the
            // EventStorming and Domain Storytelling actor — the one that issues
            // a command — and never a persona, which is a UX archetype and
            // belongs in `.design`. A `user` operates the context and holds a
            // right, an `organisation` is acted toward without operating it,
            // and a `technical` actor is a system or timer that triggers a use
            // case from outside — named for what it triggers, its contract
            // staying in `dependencies.md`.
            "user",
            "organisation",
            "technical",
            "term",
            // `context.md`: a `feature-flag` is a switch decided at release,
            // from configuration; a `setting` is a value a person chooses at
            // runtime, whoever its `scope` names, whether it turns a capability
            // on or shapes it. Both carry `key` — the identifier as the code
            // spells it — and are what a feature chapter's `feature-flag` and
            // `setting` references resolve to.
            "feature-flag",
            "setting",
        ],
        file: [
            "context-map",
            "context",
            "domain",
            "actors",
            "features",
            "skills",
            // The same two words at file level, meaning the same thing one
            // scope up: the file holds a context's, a `##` chapter holds one
            // feature's or one aggregate's.
            "requirements",
            "invariants",
            "model",
            "flow",
            "dependencies",
        ],
    },
    tech: {
        chapter: [
            "language",
            "runtime",
            "framework",
            "library",
            "package",
            "tool",
            "service",
            "platform",
            "protocol",
            "format",
        ],
        file: [],
    },
    ai: {
        chapter: [
            "practice",
            "agent",
            "skill",
            "plugin",
            "mcp-server",
            "hook",
            "workflow",
            "model",
            "concept",
            "guardrail",
        ],
        file: ["adoption-map", "stage", "concepts"],
    },
    arc42: { chapter: [], file: [] },
    design: { chapter: [], file: [] },
};

// `.tech` spelled this concept `kind` before `type` was unified across folders.
// The old name keeps working so an existing repository is not broken by a
// generator sync, but it lints as a warning and is not documented any more.
const LEGACY_TYPE_FIELD_BY_FOLDER = { tech: "kind" };

// The extension namespace. A plugin layered on top of devbook may persist its
// own state on a chapter under `ext`, and this schema deliberately says nothing
// about what it holds: the generator carries every `ext` key through untouched,
// validates none of it, and produces no edges from it. That is the whole point.
// Without it, every extension would force a devbook schema bump and a migration
// in every consuming repository. Reserved and currently unused: the first
// extension's state became schema fields instead (record 77).
//
// The block grammar is flat single-line scalars, so the namespace is spelled
// with dotted keys — `ext.<plugin>.<key>: <value>` — rather than by nesting.
// Namespacing by the owning plugin is a convention this file states and does
// not enforce; enforcing it would be validating the one field that must not be
// validated.
const EXT_FIELD = "ext";
const EXT_PREFIX = "ext.";

/** Whether a metadata key belongs to the opaque extension namespace. */
export function isExtensionField(key) {
    return key === EXT_FIELD || key.startsWith(EXT_PREFIX);
}

// Fields every folder's chapter/file block may carry, plus folder-specific
// extras layered in below.
const COMMON_OPTIONAL_FIELDS = [
    "type",
    "related",
    "issue",
    "effort",
    "roadmap",
    "date",
    "tests",
    ...REVIEW_FIELDS,
];

// A feature flag is a switch, so its `default` is one of two words. A setting's
// `default` is whatever value the product ships with and is not enumerated.
const FLAG_DEFAULTS = ["on", "off"];

// Who may change a setting at runtime: the person it belongs to, an
// administrator for the whole tenant, or an operator for the whole system.
const SETTING_SCOPES = ["user", "tenant", "system"];

// How a bounded context ships: as its own deployable `service`, or as a
// `module` inside a modular monolith that hosts other contexts beside it.
const CONTEXT_DEPLOYMENTS = ["service", "module"];

// `roadmap` entries are lowercase kebab-case tag slugs, not chapter references.
const ROADMAP_TAG_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/;

// `tests` links a chapter or file to the test cases that assert what it claims.
// Entries are `<level>:<runner>:<selector>` — coarse to fine, so a consumer can
// group by level, pick a command from the runner, and hand the selector to that
// runner verbatim.
//
// The level vocabulary is deliberately tiny and about *reach*, not about which
// tool ran it: a unit test pins a rule inside one unit, an integration test
// crosses a process or a store, an end-to-end test drives the product the way a
// user does. That is the distinction a reader of a chapter wants ("is this
// covered end to end?") and the one a runner cannot supply, since the same
// runner routinely hosts all three.
const TEST_LEVELS = ["unit", "integration", "e2e"];

// How each known runner turns a selector into an argv.
//
// This mapping is the reason a test reference carries a runner rather than a
// bare path: a consumer that knows the runner can *run* the test. That is also
// what makes this field admissible where a `code-path` field is not — see
// "Why a test link and not a code link" in
// devbook-chapter-metadata.md. A selector that stops resolving
// fails a run out loud; a source path in a metadata block rots in silence.
//
// Selectors are runner-native, because a runner-native selector is exactly what
// a person pastes into a terminal. Where a runner needs a file *and* a title,
// the two are joined with `#`, matching the `<path>#<slug>` shape this schema
// already uses for chapter references.
const TEST_RUNNERS = {
    dotnet: {
        selector: "fully-qualified test class or method name",
        argv: (selector) => ["dotnet", "test", "--filter", `FullyQualifiedName~${selector}`],
    },
    playwright: {
        selector: "<spec path>, optionally #<test title>",
        argv: (selector) => {
            const [spec, title] = splitSpecAndTitle(selector);
            return ["npx", "playwright", "test", spec, ...(title ? ["-g", title] : [])];
        },
    },
    vitest: {
        selector: "<spec path>, optionally #<test name>",
        argv: (selector) => {
            const [spec, title] = splitSpecAndTitle(selector);
            return ["npx", "vitest", "run", spec, ...(title ? ["-t", title] : [])];
        },
    },
    jest: {
        selector: "<spec path>, optionally #<test name>",
        argv: (selector) => {
            const [spec, title] = splitSpecAndTitle(selector);
            return ["npx", "jest", spec, ...(title ? ["-t", title] : [])];
        },
    },
    pytest: {
        selector: "pytest node id",
        argv: (selector) => ["pytest", selector],
    },
};

// A `tests` entry that starts like a devbook path is a chapter reference
// pasted into a field that takes test identifiers. Worth its own message,
// because the author's intent is obvious and the fix is to move it to `related`.
const DEVBOOK_PATH_PREFIX = /^\.(?:domain|arc42|tech|design|ai)\//;

// What proves a behaviour chapter, by the file it sits in. The level of proof
// follows the kind of promise: a requirement is made to someone outside the
// model, so what proves it is the product driven the way that someone drives
// it; an invariant is what the type guarantees, so a test that has to start
// the product to reach it is asserting something else.
//
// This is the one place the schema reads `tests` against the chapter's `type`.
// Everywhere else the field is a record of what asserts a chapter and carries
// no expectation, and that stays true: these two kinds are checked because the
// file they live in *is* the claim about their level, so a mismatch means one
// of the two is wrong.
const BEHAVIOUR_TEST_LEVELS = {
    requirement: {
        levels: ["e2e", "integration"],
        reason:
            "a requirement is proved `e2e` — it promises something to someone outside the model, so what proves it is the product driven the way that someone drives it — or `integration` where it is a policy no user triggers",
    },
    invariant: {
        levels: ["unit"],
        reason:
            "an invariant is proved `unit` — it is what the type guarantees no matter who calls it, and a test that has to start the product to reach it is not asserting the guarantee",
    },
};

// The cases that prove one rule. They are structural headings one level under
// the rule's own, so they are found by their text rather than by a block.
const SCENARIO_HEADING = /^Scenario:/i;

// Fields that steer how this document appears in the generated outline, and so
// describe the document's place in its directory rather than a chapter inside
// it. Valid on the file-level block only.
const FILE_ONLY_FIELDS = ["index", "number"];

// What `index` may say. `root` makes this document its directory's entry point;
// `exclude` keeps it out of the outline. Absent means an ordinary listed
// document, which is the case for nearly every file.
const INDEX_VALUES = ["root", "exclude"];

// `date` is a calendar date, deliberately not a timestamp: it records when the
// thing the document describes was decided or logged, which is a fact about the
// content, not about the last time someone touched the file.
const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

const NUMBER_PATTERN = /^\d+$/;

// A leading number in a filename, with an optional label before it, so
// `01-introduction.md`, `0007-use-postgres.md`, and `ADR-0007-use-postgres.md`
// all yield their number. The separator is required, so `2024-review.md` reads
// as number 2024 but `introduction.md` yields nothing.
const FILENAME_NUMBER_PATTERN = /^(?:[A-Za-z]+[-_ ])?(\d+)(?:[-_. ]|$)/;

// Fields the schema once defined and no longer does. Reported by name rather
// than as a generic unrecognized field, because a repository that adopted the
// convention earlier still carries them and the author needs to be told what
// replaced them, not just that the field is unknown.
const REMOVED_FIELDS = {
    order:
        "reading order is generated from the folder convention plus what each " +
        "document says about itself. Delete the field; where the generated order is " +
        "not what you want, give the documents a `number` or mark the directory's " +
        "entry point with `index: root`. See " +
        "devbook-chapter-metadata.md.",
};

const FOLDER_EXTRA_FIELDS = {
    domain: [
        "depends-on", "aliases", "feature-flag", "setting", "role", "key", "default", "scope",
        "deployment",
        ...DECISION_FIELDS,
    ],
    arc42: [],
    tech: ["kind", "version", "depends-on", "alternatives"],
    design: [],
    ai: ["depends-on", "stage"],
};

/**
 * The stages of the DevOps loop, in loop order: the first four are the dev
 * half, the last four the ops half, and `monitor` feeds `plan`. A `.ai` chapter
 * says where it sits with `stage`, a list of these words. The vocabulary is
 * fixed rather than the repository's own so that a tool draws one loop for
 * every repository and a reader can compare two; a stage a flow does not use
 * stays empty. Exported for the consumer that draws the loop.
 */
export const AI_STAGES = ["plan", "code", "build", "test", "release", "deploy", "operate", "monitor"];

// The folder-specific fields that describe a chapter and never a document, per
// devbook-chapter-metadata.md: "a file's overall relationships are expressed
// through `related` only". A file has no dependencies, no version, no feature
// flag, no aliases and no role — the chapters inside it do. `.ai`'s `stage` is
// here for the same reason: a file groups chapters and places none of them on
// the loop, so a file-level `stage` would place chapters by implication, which
// is the one thing the field exists to make explicit.
const CHAPTER_ONLY_EXTRA_FIELDS = [
    "depends-on",
    "aliases",
    "feature-flag",
    "setting",
    "role",
    "key",
    "default",
    "scope",
    "deployment",
    "version",
    "alternatives",
    "stage",
];

// Which chapters inside a folder may carry one of that folder's extra fields.
// `FOLDER_EXTRA_FIELDS` says the field exists in the folder; this says which
// `type` values may carry it.
//
// The scopes are the folder rules restated. Worth enforcing rather than
// trusting, because a mis-scoped field here is silently wrong rather than
// visibly wrong: `depends-on` on an aggregate generates a real graph edge the
// model never claimed.
//
// A field a folder rule gives to every chapter stays out of this table.
// `.domain`'s `aliases` is the case in point: a modelled concept carries its
// surface names on its own chapter and a `term` chapter exists only for a word
// that has no chapter to carry them, so the field is legal on any chapter and
// only the file-level prohibition above applies.
const FIELD_TYPE_SCOPE = {
    // `.domain`: delivery order, the feature flag that gates a capability,
    // and the setting that gates or configures it all belong to that
    // capability. `domain.md` chapters describe standing structure and
    // relate through `model.md`, the context's dependencies, and `related`
    // instead.
    // `role` is the authorization role an actor holds: the fourth beat of a
    // `user` chapter made addressable. An `organisation` or `technical` actor
    // rarely has one but may; no other chapter does.
    // `key` and `default` describe the switch itself, so they sit on the
    // `feature-flag` and `setting` chapters a feature points at; `scope` says
    // who may change a setting, and a flag has no such person — it is decided
    // at release.
    domain: {
        "depends-on": ["feature", "sub-feature"],
        "feature-flag": ["feature", "sub-feature"],
        setting: ["feature", "sub-feature"],
        role: ["user", "organisation", "technical"],
        key: ["feature-flag", "setting"],
        default: ["feature-flag", "setting"],
        scope: ["setting"],
        // How the context ships is the context's own: `context-map.md`'s
        // `bounded-context` chapter stands for it on the map, and the
        // context's `context.md` carries it on its file-level block (see
        // `FILE_FIELD_TYPE_SCOPE`). The graph build holds the two equal.
        deployment: ["bounded-context"],
    },
    arc42: {},
    tech: {},
    design: {},
    ai: {},
};

// The exceptions to `CHAPTER_ONLY_EXTRA_FIELDS`: a chapter-only field a file
// may carry after all, on the file-level block of the `type` values named.
// `context.md` is the one document that *is* its subject — the bounded context
// as a whole — so how the context ships describes that document, not a chapter
// missing from it.
const FILE_FIELD_TYPE_SCOPE = {
    domain: {
        deployment: ["context"],
    },
};

/** Determine which devbook folder a repo-relative path belongs to. */
export function folderKindForPath(relPath) {
    const normalized = String(relPath).replace(/\\/g, "/");
    // An address is just a repository path, and every devbook path starts with
    // the one parent. Nothing else in the schema knows about the layout.
    if (!normalized.startsWith(DEVBOOK_PREFIX)) return null;
    const subject = normalized.slice(DEVBOOK_PREFIX.length);
    for (const name of DEVBOOK_FOLDER_NAMES) {
        if (subject.startsWith(`${name}/`)) return name;
    }
    return null;
}

/**
 * The status a folder's content rests at and therefore omits, or null when the
 * folder has none and every block must state its status.
 */
export function restingStatusFor(folder) {
    return RESTING_STATUS_BY_FOLDER[folder] ?? null;
}

/**
 * The effective status of a block, and whether the file actually said it.
 *
 * Every consumer resolves through here rather than reading `meta.status`, so
 * "at rest" and "nobody said" stay distinguishable: an omitted status in an
 * editorial folder resolves to that folder's resting value with
 * `declared: false`, while an omitted status anywhere else stays null — which
 * validateDocument reports as an error, and no viewer should paper over.
 */
export function resolveStatus(folder, meta) {
    const declared = meta?.status ?? null;
    if (declared !== null) return { status: declared, declared: true };
    return { status: restingStatusFor(folder), declared: false };
}

/**
 * The `type` values a folder allows on a block at this level, or `[]` when the
 * folder defines no kind distinction. `level` is "file" for the level-1 block
 * and "chapter" for every other heading.
 */
export function typeValuesFor(folder, level) {
    return TYPE_BY_FOLDER[folder]?.[level] ?? [];
}

/**
 * The effective `type` of a block, falling back to the folder's legacy field
 * name where one exists. Returns null when the block declares no type.
 */
export function resolveType(folder, meta) {
    if (!meta) return null;
    if (meta.type !== undefined && meta.type !== null && meta.type !== "") return meta.type;
    const legacy = LEGACY_TYPE_FIELD_BY_FOLDER[folder];
    const value = legacy ? meta[legacy] : null;
    return value === undefined || value === "" ? null : value;
}

function parseScalar(raw) {
    const value = raw.trim();
    if (value === "null" || value === "") return null;
    if (value.startsWith("[") && value.endsWith("]")) {
        const inner = value.slice(1, -1).trim();
        if (inner === "") return [];
        return inner
            .split(",")
            .map((entry) => stripQuotes(entry.trim()))
            .filter((entry) => entry.length > 0);
    }
    return stripQuotes(value);
}

function stripQuotes(value) {
    if (
        (value.startsWith('"') && value.endsWith('"')) ||
        (value.startsWith("'") && value.endsWith("'"))
    ) {
        return value.slice(1, -1);
    }
    return value;
}

/** Normalize a scalar-or-list metadata value to a list. */
function toList(value) {
    if (value === null || value === undefined) return [];
    return Array.isArray(value) ? value : [value];
}

/** Parse the body of a fenced ```meta block (without the fences) into an object. */
export function parseMetaBody(body) {
    const result = {};
    for (const line of body.split("\n")) {
        if (!line.trim()) continue;
        const idx = line.indexOf(":");
        if (idx === -1) continue;
        const key = line.slice(0, idx).trim();
        const value = line.slice(idx + 1);
        result[key] = parseScalar(value);
    }
    return result;
}

/**
 * Split a markdown document into its headings and, for each heading, the
 * immediately-following `meta` block (if present).
 *
 * Returns `{ fileMeta, fileTitle, chapters }` where `chapters` covers every
 * `#`/`##`/`###` heading found (level 1 is also exposed as `fileMeta` /
 * `fileTitle` for convenience, matching the "file-level block sits under the
 * top-level heading" convention).
 */
export function parseDocument(markdown) {
    const lines = markdown.split(/\r?\n/);
    const chapters = [];
    let fileTitle = null;
    let fileMeta = null;

    for (let i = 0; i < lines.length; i++) {
        const headingMatch = /^(#{1,6})\s+(.*)$/.exec(lines[i]);
        if (!headingMatch) continue;

        const level = headingMatch[1].length;
        const text = headingMatch[2].trim();
        const slug = slugify(text);

        // Look ahead past blank lines for a ```meta fence.
        let j = i + 1;
        while (j < lines.length && lines[j].trim() === "") j++;

        let meta = null;
        let metaRaw = null;
        if (j < lines.length && /^```meta\s*$/.test(lines[j].trim())) {
            const bodyLines = [];
            let k = j + 1;
            while (k < lines.length && lines[k].trim() !== "```") {
                bodyLines.push(lines[k]);
                k++;
            }
            metaRaw = bodyLines.join("\n");
            meta = parseMetaBody(metaRaw);
        }

        const entry = { level, text, slug, line: i + 1, meta, metaRaw };
        chapters.push(entry);

        if (level === 1 && fileTitle === null) {
            fileTitle = text;
            fileMeta = meta;
        }
    }

    return { fileTitle, fileMeta, chapters };
}

// GitHub's anchor algorithm lowercases, strips punctuation, then replaces each
// remaining whitespace character with a hyphen — it does *not* collapse runs.
// "Organizational & Process Constraints" therefore anchors as
// "organizational--process-constraints" (double hyphen where the & was).
//
// What survives the strip is letters, digits, and underscores in *any* script,
// which is why the class is `\p{L}\p{N}_` under the `u` flag and not `\w`:
// `\w` is ASCII-only, so "Café Ordering" would slug to "caf-ordering" here
// while GitHub renders "café-ordering", and a `related` link written against
// one lands somewhere the reader is not.
export function slugify(text) {
    return text
        .toLowerCase()
        .trim()
        .replace(/[^\p{L}\p{N}_\s-]/gu, "")
        .replace(/\s/g, "-");
}

const SUMMARY_MAX_LENGTH = 300;

/** Reduce a Markdown run to the plain text a list view can show on one line. */
function toPlainText(markdown) {
    return markdown
        .replace(/!\[([^\]]*)\]\([^)]*\)/g, "$1") // image → its alt text
        .replace(/\[([^\]]+)\]\([^)]*\)/g, "$1") // link → its label
        .replace(/`([^`]+)`/g, "$1") // code span → its content
        .replace(/(\*\*|__|\*|_)(?=\S)(.+?)(?<=\S)\1/g, "$2") // emphasis markers
        .replace(/\s+/g, " ")
        .trim();
}

/**
 * The document's lede and how many diagrams it embeds — the two things a
 * viewer needs to render a folder's list view, and the two things it would
 * otherwise have to open every file to learn.
 *
 * Both are by-products of a parse the generator is doing anyway, so carrying
 * them on the derived index is what lets a consumer list a devbook folder
 * without reading a single Markdown file.
 *
 * `summary` is the blockquote that
 * `devbook-chapter-metadata.md` places directly after the
 * file-level `meta` block, falling back to the first paragraph of prose when
 * the file has no blockquote. Either way it is the text *before* the first
 * `##`, reduced to plain text and capped at ~300 characters on a word
 * boundary. `null` when the document opens straight into a chapter.
 *
 * `diagrams` counts embedded diagrams across the whole document: fenced
 * ```mermaid blocks plus Markdown image embeds. Both are diagrams to a reader,
 * and the devbook folders use images for nothing else.
 */
export function documentDigest(markdown) {
    const lines = markdown.split(/\r?\n/);

    let diagrams = 0;
    let inFence = false;
    let fenceChar = null;

    // The lede lives before the first `##`; `blockquote` wins over `paragraph`
    // whichever order they appear in, per the metadata convention.
    let seenTitle = false;
    let ledeDone = false;
    const blockquote = [];
    const paragraph = [];
    let collecting = null;

    for (const line of lines) {
        const fence = line.match(/^\s*(`{3,}|~{3,})\s*([^\s`~]*)/);
        if (fence) {
            if (!inFence) {
                inFence = true;
                fenceChar = fence[1][0];
                if (fence[2].toLowerCase() === "mermaid") diagrams++;
            } else if (fence[1][0] === fenceChar) {
                inFence = false;
                fenceChar = null;
            }
            collecting = null;
            continue;
        }
        if (inFence) continue;

        for (const _ of line.matchAll(/!\[[^\]]*\]\([^)]*\)/g)) diagrams++;

        if (ledeDone) continue;

        const heading = /^(#{1,6})\s+/.exec(line);
        if (heading) {
            // The first `#` opens the lede region; anything deeper closes it.
            if (heading[1].length === 1 && !seenTitle) seenTitle = true;
            else if (seenTitle) ledeDone = true;
            collecting = null;
            continue;
        }
        if (!seenTitle) continue;

        if (line.trim() === "") {
            collecting = null;
            continue;
        }
        if (/^\s*>/.test(line)) {
            if (collecting !== "blockquote" && blockquote.length) continue; // keep the first only
            collecting = "blockquote";
            blockquote.push(line.replace(/^\s*>\s?/, ""));
        } else {
            if (collecting !== "paragraph" && paragraph.length) continue;
            collecting = "paragraph";
            paragraph.push(line);
        }
    }

    const source = blockquote.length ? blockquote : paragraph;
    let summary = source.length ? toPlainText(source.join(" ")) : null;
    if (summary && summary.length > SUMMARY_MAX_LENGTH) {
        const clipped = summary.slice(0, SUMMARY_MAX_LENGTH);
        const lastSpace = clipped.lastIndexOf(" ");
        summary = `${(lastSpace > 0 ? clipped.slice(0, lastSpace) : clipped).replace(/[.,;:—-]$/, "")}…`;
    }

    return { summary: summary || null, diagrams };
}

/**
 * Validate one block's `type` against its folder's vocabulary.
 *
 * Messages are sentence fragments beginning with a verb, so each caller can
 * prefix its own subject. Shared by the document lint and by graph
 * construction, so the canvas, the CLI, and CI all report the same thing.
 */
export function typeIssues(folder, blockLevel, meta, fileBase = null) {
    const issues = [];
    if (!meta) return issues;

    const allowed = typeValuesFor(folder, blockLevel);
    const declared = resolveType(folder, meta);
    if (allowed.length) {
        if (declared === null) {
            issues.push({
                severity: "error",
                message: `is missing required \`type\`. Expected one of: ${allowed.join(", ")}.`,
            });
        } else if (!allowed.includes(declared)) {
            // A bounded context may carry a file the convention does not name —
            // whatever that context has to record and no other file holds. The
            // listed values are the files with documented responsibilities, not
            // the only files permitted, so an unlisted one is accepted on the
            // rule every listed one already follows: a file's `type` is its
            // filename. That still catches the typo, which is what the closed
            // list was actually buying.
            if (folder === "domain" && blockLevel === "file" && fileBase && declared === fileBase) {
                // An additional page, named for itself. Nothing to report.
            } else {
                issues.push({
                    severity: "error",
                    message: `has type "${declared}", expected one of: ${allowed.join(", ")}${
                        folder === "domain" && blockLevel === "file"
                            ? `, or "${fileBase}" to match this file's own name`
                            : ""
                    }.`,
                });
            }
        }
    } else if (declared !== null) {
        issues.push({
            severity: "warning",
            message: `sets \`type\` to "${declared}", but the ${folder} folder defines no \`type\` value set at ${blockLevel} level — heading level already carries that distinction. Omit the field.`,
        });
    }

    const legacy = LEGACY_TYPE_FIELD_BY_FOLDER[folder];
    if (legacy && meta[legacy] != null) {
        issues.push({
            severity: "warning",
            message: `uses \`${legacy}\`, which has been renamed to \`type\`. Rename the field; \`${legacy}\` still works but is no longer documented.`,
        });
    }

    return issues;
}

/** Split a `<spec>#<title>` selector; title is null when the entry has none. */
function splitSpecAndTitle(selector) {
    const idx = selector.indexOf("#");
    if (idx === -1) return [selector, null];
    return [selector.slice(0, idx).trim(), selector.slice(idx + 1).trim() || null];
}

/** Every test level this schema defines, in reach order. */
export const testLevels = () => [...TEST_LEVELS];

/**
 * Every runner the tooling can build a command for, each with the selector shape
 * it expects — what a viewer needs to explain the field to whoever is filling it
 * in.
 */
export const testRunners = () =>
    Object.entries(TEST_RUNNERS).map(([runner, { selector }]) => ({ runner, selector }));

/**
 * Split one `tests` entry into `{ level, runner, selector }`, or null when it is
 * not in `<level>:<runner>:<selector>` form.
 *
 * Only the first two colons delimit: a selector routinely contains its own
 * (`pytest` node ids, a `file:line`), and everything after the runner belongs to
 * the runner.
 */
export function parseTestReference(ref) {
    const raw = String(ref ?? "").trim();
    const first = raw.indexOf(":");
    if (first <= 0) return null;
    const second = raw.indexOf(":", first + 1);
    if (second <= first + 1) return null;
    const selector = raw.slice(second + 1).trim();
    if (!selector) return null;
    return { level: raw.slice(0, first).trim(), runner: raw.slice(first + 1, second).trim(), selector };
}

/**
 * The command that runs one `tests` entry, as `{ level, runner, selector,
 * command }` with `command` an argv array — or null when the entry is malformed
 * or names a runner this tooling has no mapping for.
 *
 * The argv is meant to be run from the repository root. A repository whose
 * runner needs a different working directory, a project path, or a config flag
 * wraps this rather than reshaping the reference: the reference identifies the
 * test, and how this repository invokes its runners is a property of the
 * repository.
 *
 * This is the seam a UI "run this test" affordance sits on. It is deliberately
 * a pure function that returns an argv and executes nothing.
 */
export function testCommand(ref) {
    const parsed = parseTestReference(ref);
    if (!parsed) return null;
    const runner = TEST_RUNNERS[parsed.runner];
    if (!runner) return null;
    return { ...parsed, command: runner.argv(parsed.selector) };
}

/**
 * Validate a block's `tests` entries.
 *
 * Messages are sentence fragments beginning with a verb, matching `typeIssues`,
 * so the document lint, the graph build, and the canvas all report the same
 * thing with their own subject prefixed.
 *
 * An unknown runner is a warning rather than an error: the level and the
 * selector still say what covers this chapter, and a repository on a stack this
 * tooling has never heard of should not be blocked from recording that. What it
 * loses is the run command, which the message says.
 */
export function testIssues(meta) {
    const issues = [];
    if (!meta || meta.tests == null) return issues;

    for (const entry of toList(meta.tests)) {
        if (DEVBOOK_PATH_PREFIX.test(entry)) {
            issues.push({
                severity: "error",
                message: `has \`tests\` entry "${entry}", which is a chapter reference — \`tests\` holds \`<level>:<runner>:<selector>\` test identifiers. A link to another chapter belongs in \`related\`.`,
            });
            continue;
        }

        const parsed = parseTestReference(entry);
        if (!parsed) {
            issues.push({
                severity: "error",
                message: `has \`tests\` entry "${entry}", which is not \`<level>:<runner>:<selector>\` — e.g. \`unit:dotnet:Ordering.Domain.Tests.OrderTests\`. Entries cannot contain a comma, since that separates the list.`,
            });
            continue;
        }

        if (!TEST_LEVELS.includes(parsed.level)) {
            issues.push({
                severity: "error",
                message: `has \`tests\` entry "${entry}" with level "${parsed.level}", expected one of: ${TEST_LEVELS.join(", ")}.`,
            });
        }

        if (!(parsed.runner in TEST_RUNNERS)) {
            issues.push({
                severity: "warning",
                message: `has \`tests\` entry "${entry}" naming runner "${parsed.runner}", which this tooling has no command mapping for (known: ${Object.keys(TEST_RUNNERS).join(", ")}), so nothing can offer to run it. The entry is kept as written.`,
            });
        }
    }

    return issues;
}

/**
 * How many `#### Scenario:` headings sit under the chapter at `index`.
 *
 * A scenario belongs to the rule above it by position, exactly as an
 * annotation does: the count stops at the first heading back at or above the
 * rule's own level, and only headings one level deeper count — a scenario of a
 * scenario is not a thing, and a `#####` under one is its own business.
 */
export function scenarioCount(chapters, index) {
    const rule = chapters[index];
    if (!rule) return 0;
    let count = 0;
    for (let i = index + 1; i < chapters.length; i++) {
        if (chapters[i].level <= rule.level) break;
        if (chapters[i].level === rule.level + 1 && SCENARIO_HEADING.test(chapters[i].text)) count++;
    }
    return count;
}

/**
 * Coverage warnings for one behaviour chapter — a `requirement` or an
 * `invariant`. Every other type returns nothing.
 *
 * All of these are warnings, deliberately. Each reports a chapter that is
 * incomplete rather than wrong, and an error would be counter-productive in
 * the exact way this convention exists to avoid: refusing the document teaches
 * people to leave `tests` off and to write the rule back into prose, where
 * nothing reports it at all. A warning names the gap and leaves the rule
 * recorded.
 *
 * Messages are sentence fragments beginning with a verb, matching `typeIssues`,
 * so each caller can prefix its own subject.
 */
export function behaviourIssues(type, meta, scenarios = 0) {
    const expected = BEHAVIOUR_TEST_LEVELS[type];
    if (!expected) return [];
    const issues = [];

    if (scenarios === 0) {
        issues.push({
            severity: "warning",
            message: `is a \`${type}\` chapter with no \`#### Scenario:\` under it — a rule with no case that exercises it is one nobody can tell has been broken, and a brief can derive no acceptance check from it.`,
        });
    }

    // Absence still carries no claim: a chapter with no `tests` is one nobody
    // has linked, which is the schema's standing rule for the field and is not
    // narrowed here. Only a chapter that *does* claim coverage is held to the
    // level its file implies.
    const levels = toList(meta?.tests)
        .map((entry) => parseTestReference(entry)?.level)
        .filter((level) => TEST_LEVELS.includes(level));
    if (levels.length && !levels.some((level) => expected.levels.includes(level))) {
        const found = [...new Set(levels)].map((level) => `\`${level}\``).join(", ");
        issues.push({
            severity: "warning",
            message: `is a \`${type}\` chapter whose \`tests\` reach only ${found}, where ${expected.reason}. Either the link is at the wrong level, or the rule is in the wrong file.`,
        });
    }

    return issues;
}

/**
 * Flag literal escape sequences sitting in Markdown body text.
 *
 * An agent writing a file through a shell can emit the escape itself rather
 * than the newline it stands for — a PowerShell here-string that was single-
 * quoted when it needed interpolation, say. The failure is silent and
 * disproportionate: a `## Heading` glued onto the end of the previous line
 * stops being a heading, so the chapter disappears from the outline, from the
 * graph, and from every check that reasons about headings. Nothing else here
 * can catch it, because by the time those checks run the heading is prose.
 *
 * Warning rather than error: a document legitimately discussing escape
 * sequences would otherwise have no way to say so. Only newline escapes are
 * matched — `\t` was deliberately left out, because it breaks no structure and
 * collides with unformatted Windows paths. A bare `C:\temp\new` written
 * outside backticks is still a known false positive; formatting paths as code
 * avoids it.
 */
export function escapeSequenceIssues(markdown) {
    const issues = [];
    let inFence = false;
    let fenceChar = null;

    markdown.split(/\r?\n/).forEach((line, index) => {
        const fence = line.match(/^\s*(`{3,}|~{3,})/);
        if (fence) {
            if (!inFence) {
                inFence = true;
                fenceChar = fence[1][0];
            } else if (fence[1][0] === fenceChar) {
                inFence = false;
                fenceChar = null;
            }
            return;
        }
        if (inFence) return;

        // The PowerShell escape is checked against the line with double-backtick
        // code spans removed: that is how Markdown quotes a run containing
        // backticks, so a doubly-quoted occurrence is a document *describing*
        // the escape rather than one corrupted by it. Single-backtick spans are
        // left in place, because those backticks are part of the corrupted
        // token itself. The C-style escapes drop single-backtick spans too,
        // since a backticked \n is ordinary documentation.
        const quoted = line.replace(/``.+?``/g, "");
        const found = new Set();
        if (quoted.includes("`r`n")) found.add("`r`n");
        for (const match of quoted.replace(/`[^`]*`/g, "").matchAll(/\\r\\n|\\n/g)) {
            found.add(match[0]);
        }
        if (!found.size) return;

        const glued = /(?:`r`n|\\r\\n|\\n)\s*#{1,6}\s/.test(quoted);
        issues.push({
            severity: "warning",
            line: index + 1,
            message:
                `has a literal ${[...found].map((s) => `"${s}"`).join(" and ")} ` +
                `escape sequence in body text on line ${index + 1}` +
                (glued
                    ? ", with a heading immediately after it — that heading does not start a line, so it is not being parsed as a heading."
                    : ". If a line break was intended, the escape was not interpreted.")
        });
    });

    return issues;
}

/**
 * The number in a file or directory name, or `null` when it carries none.
 *
 * Takes the basename, so it works for `01-introduction.md` and for a numbered
 * subdirectory alike.
 */
export function fileNumberFromPath(relPath) {
    const name = relPath.replace(/\\/g, "/").split("/").pop() ?? "";
    const match = FILENAME_NUMBER_PATTERN.exec(name);
    return match ? Number(match[1]) : null;
}

/**
 * This document's number, from its `number` field if it declares one and from
 * its filename otherwise.
 *
 * The authored field wins, so a document can be renumbered without renaming the
 * file — and a file whose name already carries the number needs no field at
 * all. `numberIssues` reports the two disagreeing.
 */
export function documentNumber(relPath, meta) {
    const declared = meta?.number;
    if (typeof declared === "string" && NUMBER_PATTERN.test(declared)) return Number(declared);
    return fileNumberFromPath(relPath);
}

/**
 * How this document's `index` field steers the outline: `"root"`, `"exclude"`,
 * or `null` for an ordinary listed document.
 */
export function indexRole(meta) {
    const value = meta?.index;
    return typeof value === "string" && INDEX_VALUES.includes(value) ? value : null;
}

/**
 * Lint the fields that steer outline generation — `index`, `number`, `date`.
 *
 * Exported so the graph build reports them, not just the canvas: these fields
 * decide what `index.json` looks like, and a typo in one silently generates a
 * different outline rather than failing.
 *
 * `level` is "file" for the level-1 block and "chapter" for every other heading.
 */
export function outlineFieldIssues(relPath, meta, level) {
    const issues = [];
    if (!meta) return issues;

    if (level !== "file") {
        for (const field of FILE_ONLY_FIELDS) {
            if (meta[field] != null) {
                issues.push({
                    severity: "error",
                    message: `has \`${field}\`, which belongs on the file-level block only — it places the document in its directory, not a chapter in its document.`,
                });
            }
        }
        // The remaining checks are about the file-level fields above plus
        // `date`, which is legal here; fall through for `date` only.
    }

    if (level === "file" && meta.index != null) {
        if (indexRole(meta) === null) {
            issues.push({
                severity: "error",
                message: `has \`index\` "${meta.index}", expected one of: ${INDEX_VALUES.join(", ")}. Omit the field for an ordinary listed document.`,
            });
        }
    }

    if (level === "file" && meta.number != null) {
        const raw = meta.number;
        if (typeof raw !== "string" || !NUMBER_PATTERN.test(raw)) {
            issues.push({
                severity: "error",
                message: `has \`number\` "${Array.isArray(raw) ? raw.join(", ") : raw}" — a document's number is a single non-negative integer.`,
            });
        } else {
            const fromName = fileNumberFromPath(relPath);
            if (fromName !== null && fromName !== Number(raw)) {
                issues.push({
                    severity: "warning",
                    message: `declares \`number: ${raw}\` but its filename reads ${fromName}. The field wins; rename the file or drop the field so a reader sees one number.`,
                });
            }
        }
    }

    if (meta.date != null) {
        const raw = meta.date;
        if (typeof raw !== "string" || !DATE_PATTERN.test(raw)) {
            issues.push({
                severity: "error",
                message: `has \`date\` "${Array.isArray(raw) ? raw.join(", ") : raw}" — a date is a single calendar day in \`YYYY-MM-DD\` form.`,
            });
        }
    }

    return issues;
}

/**
 * Lint where a folder-specific field may sit: on a chapter rather than the
 * file-level block, and on the chapter `type` values its folder rule names.
 *
 * Exported alongside `typeIssues` and reported by the graph build for the same
 * reason: the field parses, so nothing else in the pipeline objects, and the
 * only thing that catches it is a rule that knows which blocks may carry it.
 */
export function fieldScopeIssues(folder, blockLevel, meta) {
    const issues = [];
    if (!meta) return issues;

    const folderFields = FOLDER_EXTRA_FIELDS[folder];
    if (!folderFields) return issues;

    if (blockLevel === "file") {
        const fileType = resolveType(folder, meta);
        for (const field of CHAPTER_ONLY_EXTRA_FIELDS) {
            if (!folderFields.includes(field) || meta[field] == null) continue;
            if (FILE_FIELD_TYPE_SCOPE[folder]?.[field]?.includes(fileType)) continue;
            issues.push({
                severity: "error",
                message:
                    field === "stage"
                        ? `has \`stage\` on the file-level block — a file groups chapters and places none of them on the loop; each chapter says its own stages. See devbook-ai.md.`
                        : `has \`${field}\` on the file-level block, where it describes a chapter that is not there — a document states its own relationships through \`related\` only. See devbook-chapter-metadata.md.`,
            });
        }
        return issues;
    }

    // A chapter with no resolvable `type` is left alone: `typeIssues` has
    // already reported that, and guessing a scope from a missing type would
    // report one mistake twice.
    const declared = resolveType(folder, meta);
    if (declared === null) return issues;

    for (const [field, allowed] of Object.entries(FIELD_TYPE_SCOPE[folder] ?? {})) {
        if (meta[field] == null || allowed.includes(declared)) continue;
        issues.push({
            severity: "error",
            message: `has \`${field}\` on a chapter of type "${declared}" — .${folder} scopes the field to ${allowed.map((value) => `\`${value}\``).join(", ")} chapters. See devbook-${folder}.md.`,
        });
    }

    return issues;
}

/**
 * Lint the approval record: `status: approved` plus `approved-by` and
 * `approved-at`.
 *
 * Exported so the graph build reports it too. The point of putting an approval
 * in the chapter is that the decision is auditable — a rung with nobody's name
 * on it, or a name with no rung, is the one shape that defeats that.
 */
export function approvalIssues(meta, contentHash = null) {
    if (!meta) return [];
    const issues = [];
    // `accepted` stands on the approval and keeps its record, so the approval
    // fields are at home under either rung. This is the one place the orphan
    // rule widens.
    const accepted = meta.status === ACCEPTED_STATUS;
    const approved = meta.status === APPROVED_STATUS || accepted;

    for (const field of APPROVAL_FIELDS) {
        const raw = meta[field];
        if (raw == null) continue;
        if (Array.isArray(raw) || String(raw).trim() === "") {
            issues.push({
                severity: "error",
                message: `has \`${field}\` set to an empty or list value — it records one approver and one day.`,
            });
            continue;
        }
        if (!approved) {
            issues.push({
                severity: "warning",
                message: `carries \`${field}\` without \`status: ${APPROVED_STATUS}\`. Either the approval is current, and the status says so, or it has lapsed and the record comes out with it.`,
            });
        }
    }

    if (meta["approved-at"] != null && !DATE_PATTERN.test(String(meta["approved-at"]))) {
        issues.push({
            severity: "error",
            message: `has \`approved-at\` "${meta["approved-at"]}" — an approval date is a single calendar day in \`YYYY-MM-DD\` form.`,
        });
    }

    // The fingerprint is optional, so its absence says nothing. Present, it is
    // checked: a value that does not match the content is the one case the
    // rung alone could never report, which is the reason the field exists.
    const recorded = meta[CONTENT_HASH_FIELD];
    if (recorded != null) {
        if (Array.isArray(recorded) || String(recorded).trim() === "") {
            issues.push({
                severity: "error",
                message: `has \`${CONTENT_HASH_FIELD}\` set to an empty or list value — it records one fingerprint of the content approved.`,
            });
        } else if (!CONTENT_HASH_PATTERN.test(String(recorded).trim())) {
            issues.push({
                severity: "error",
                message: `has \`${CONTENT_HASH_FIELD}\` "${recorded}" — a content fingerprint is \`sha256:\` followed by eight lowercase hex characters, written by the approval gate and never by hand.`,
            });
        } else if (!approved) {
            issues.push({
                severity: "warning",
                message: `carries \`${CONTENT_HASH_FIELD}\` without \`status: ${APPROVED_STATUS}\`. Either the approval is current, and the status says so, or it has lapsed and the record comes out with it.`,
            });
        } else if (contentHash != null && String(recorded).trim() !== contentHash) {
            issues.push({
                severity: "error",
                message: `states \`status: ${APPROVED_STATUS}\` over content that has changed since \`approved-at\` — \`${CONTENT_HASH_FIELD}\` records ${recorded}, the content now fingerprints as ${contentHash}. Re-approve the chapter, or take the rung off.`,
            });
        }
    }

    if (approved) {
        for (const field of APPROVAL_FIELDS) {
            if (meta[field] == null) {
                issues.push({
                    severity: "warning",
                    message: `states \`status: ${meta.status}\` without \`${field}\`. An approval nobody signed and dated is not a record of a decision.`,
                });
            }
        }
    }

    for (const issue of acceptanceIssues(meta, contentHash)) issues.push(issue);

    return issues;
}

/**
 * Lint the acceptance record: the `accepted` rung and its three fields.
 *
 * The rung says a person saw the built work against this chapter and accepted
 * it — a different statement from `approved`, which says the chapter itself is
 * right, and usually made by a different person on a different day. So the two
 * stack: an accepted chapter carries both records, and a content change drops
 * both, because a build was accepted against the text that was approved.
 *
 * Called from `approvalIssues`, since every rule here is about how the two
 * records sit together and splitting them across two callers would let a
 * repository get one without the other.
 */
function acceptanceIssues(meta, contentHash = null) {
    const issues = [];
    const accepted = meta.status === ACCEPTED_STATUS;

    for (const field of ACCEPTANCE_FIELDS) {
        const raw = meta[field];
        if (raw == null) continue;
        if (Array.isArray(raw) || String(raw).trim() === "") {
            issues.push({
                severity: "error",
                message: `has \`${field}\` set to an empty or list value — it records one acceptor and one day.`,
            });
            continue;
        }
        if (!accepted) {
            issues.push({
                severity: "warning",
                message: `carries \`${field}\` without \`status: ${ACCEPTED_STATUS}\`. Either the acceptance is current, and the status says so, or it has lapsed and the record comes out with it.`,
            });
        }
    }

    if (meta["accepted-at"] != null && !DATE_PATTERN.test(String(meta["accepted-at"]))) {
        issues.push({
            severity: "error",
            message: `has \`accepted-at\` "${meta["accepted-at"]}" — an acceptance date is a single calendar day in \`YYYY-MM-DD\` form.`,
        });
    }

    if (accepted) {
        for (const field of ACCEPTANCE_FIELDS) {
            if (meta[field] == null) {
                issues.push({
                    severity: "warning",
                    message: `states \`status: ${ACCEPTED_STATUS}\` without \`${field}\`. An acceptance nobody signed and dated is not a record of a decision.`,
                });
            }
        }

        // The rung it stands on. Without the approval record there is nothing
        // saying the chapter the build was accepted against was ever agreed.
        for (const field of APPROVAL_FIELDS) {
            if (meta[field] == null) {
                issues.push({
                    severity: "error",
                    message: `states \`status: ${ACCEPTED_STATUS}\` without \`${field}\` — an acceptance stands on an approval. Record who approved the chapter and when, or write \`status: ${APPROVED_STATUS}\` first.`,
                });
            }
        }
    }

    // A build cannot be accepted against a chapter before that chapter was
    // approved, so the two dates are ordered whenever both are readable.
    const approvedAt = meta["approved-at"];
    const acceptedAt = meta["accepted-at"];
    if (
        approvedAt != null && acceptedAt != null &&
        DATE_PATTERN.test(String(approvedAt)) && DATE_PATTERN.test(String(acceptedAt)) &&
        String(acceptedAt) < String(approvedAt)
    ) {
        issues.push({
            severity: "error",
            message: `has \`accepted-at\` ${acceptedAt} before \`approved-at\` ${approvedAt} — the build was accepted against a chapter that had not been approved yet. One of the two dates is wrong.`,
        });
    }

    const recorded = meta[ACCEPTED_HASH_FIELD];
    if (recorded != null) {
        if (Array.isArray(recorded) || String(recorded).trim() === "") {
            issues.push({
                severity: "error",
                message: `has \`${ACCEPTED_HASH_FIELD}\` set to an empty or list value — it records one fingerprint of the content accepted.`,
            });
        } else if (!CONTENT_HASH_PATTERN.test(String(recorded).trim())) {
            issues.push({
                severity: "error",
                message: `has \`${ACCEPTED_HASH_FIELD}\` "${recorded}" — a content fingerprint is \`sha256:\` followed by eight lowercase hex characters, written by the acceptance gate and never by hand.`,
            });
        } else if (!accepted) {
            issues.push({
                severity: "warning",
                message: `carries \`${ACCEPTED_HASH_FIELD}\` without \`status: ${ACCEPTED_STATUS}\`. Either the acceptance is current, and the status says so, or it has lapsed and the record comes out with it.`,
            });
        } else {
            const approvedHash = meta[CONTENT_HASH_FIELD];
            if (
                approvedHash != null && CONTENT_HASH_PATTERN.test(String(approvedHash).trim()) &&
                String(approvedHash).trim() !== String(recorded).trim()
            ) {
                issues.push({
                    severity: "error",
                    message: `records \`${CONTENT_HASH_FIELD}\` ${approvedHash} and \`${ACCEPTED_HASH_FIELD}\` ${recorded} — an acceptance is of the approved content, so the two are one value. The chapter changed between the two decisions.`,
                });
            } else if (contentHash != null && String(recorded).trim() !== contentHash) {
                issues.push({
                    severity: "error",
                    message: `states \`status: ${ACCEPTED_STATUS}\` over content that has changed since \`accepted-at\` — \`${ACCEPTED_HASH_FIELD}\` records ${recorded}, the content now fingerprints as ${contentHash}. Accept the chapter again, or take the rung off.`,
                });
            }
        }
    }

    return issues;
}

/**
 * Lint the review record: `review`, `reviewer`, and `review-at`, written
 * together or not at all, against the open notes on the chapter.
 *
 * `openNotes` is how many unresolved annotation fences the chapter carries; the
 * fences are the evidence a verdict stands on, so `changes-requested` over none
 * and `cleared` over one are both a verdict written without its findings.
 * Review state never survives the decision: an approved chapter carries the
 * decision, not the road to it.
 */
export function reviewIssues(meta, openNotes = 0) {
    if (!meta) return [];
    const issues = [];
    const present = REVIEW_FIELDS.filter((field) => meta[field] != null);
    if (!present.length) return issues;

    for (const field of present) {
        const raw = meta[field];
        if (Array.isArray(raw) || String(raw).trim() === "") {
            issues.push({
                severity: "error",
                message: `has \`${field}\` set to an empty or list value — a review names one state, one reviewer, and one day.`,
            });
        }
    }

    const missing = REVIEW_FIELDS.filter((field) => meta[field] == null);
    if (missing.length) {
        issues.push({
            severity: "error",
            message: `carries ${present.map((f) => `\`${f}\``).join(", ")} without ${missing.map((f) => `\`${f}\``).join(", ")} — the three are written together or not at all.`,
        });
    }

    const state = meta[REVIEW_FIELD];
    if (state != null && !REVIEW_STATES.includes(String(state))) {
        issues.push({
            severity: "error",
            message: `has \`review\` "${state}" — one of ${REVIEW_STATES.map((s) => `\`${s}\``).join(", ")}.`,
        });
    }

    if (meta["review-at"] != null && !DATE_PATTERN.test(String(meta["review-at"]))) {
        issues.push({
            severity: "error",
            message: `has \`review-at\` "${meta["review-at"]}" — a review date is a single calendar day in \`YYYY-MM-DD\` form.`,
        });
    }

    if (meta.status === APPROVED_STATUS || meta.status === ACCEPTED_STATUS) {
        issues.push({
            severity: "error",
            message: `states \`status: ${meta.status}\` while carrying review state — the decision clears \`review\`, \`reviewer\`, and \`review-at\` in the same change, because the decision is the record.`,
        });
    }

    if (state === "changes-requested" && openNotes === 0) {
        issues.push({
            severity: "error",
            message: `states \`review: changes-requested\` with no open annotation — a verdict without its findings. Write the objections as fences, or set \`cleared\`.`,
        });
    }
    if (state === "cleared" && openNotes > 0) {
        issues.push({
            severity: "error",
            message: `states \`review: cleared\` over ${openNotes} open annotation${openNotes === 1 ? "" : "s"} — cleared means no open note remains. Resolve them, or set \`changes-requested\`.`,
        });
    }

    return issues;
}

/**
 * Fields this block carries that the schema used to define and no longer does.
 *
 * Exported so the graph build reports them the same way it reports `typeIssues`
 * — a repository that adopted an earlier version of the convention still has
 * these fields in its Markdown, and CI is where it needs to be told.
 */
export function removedFieldIssues(meta) {
    if (!meta) return [];
    return Object.keys(meta)
        .filter((key) => key in REMOVED_FIELDS)
        .map((key) => ({
            severity: "error",
            message: `has \`${key}\`, which is no longer part of the metadata schema — ${REMOVED_FIELDS[key]}`,
        }));
}

/**
 * What a `.domain` filename says about its file. `base` is the name before any
 * split suffix — `context.md` is `context`, and `domain.order.md` is `domain`,
 * because a split file is the kind of the file it is named after. A trailing
 * `.invariants` makes the file an invariants subpage instead:
 * `domain.invariants.md` and `domain.order.invariants.md` are `invariants`,
 * and `page` names the domain page each belongs to. `legacy` marks the
 * `invariants.md` and `invariants.<name>.md` of contract 16 and before.
 */
export function domainFileName(relPath) {
    const name = (String(relPath).replace(/\\/g, "/").split("/").pop() ?? "").replace(/\.md$/i, "");
    const segments = name.split(".");
    if (segments.length > 1 && segments.at(-1) === "invariants") {
        return { base: "invariants", page: `${segments.slice(0, -1).join(".")}.md`, legacy: false };
    }
    return { base: segments[0], page: null, legacy: segments[0] === "invariants" };
}

/**
 * Heuristic lint of a document's metadata blocks against
 * chapter-metadata.instructions.md. Not a full structural validator (it does
 * not know which headings are "addressable chapters" per folder — see that
 * folder's own instructions file) — it checks the blocks that *are* present
 * plus the file-level block, which covers the common authoring mistakes.
 */
export function validateDocument(relPath, markdown) {
    const kind = folderKindForPath(relPath);
    const named = domainFileName(relPath);
    const fileBase = named.base;
    const issues = [];
    if (!kind) {
        issues.push({
            severity: "info",
            message: `${relPath} is not under .devbook/arc42/, domain/, tech/, design/, or ai/ — no metadata rules apply.`,
        });
        return issues;
    }

    const { fileTitle, fileMeta, chapters } = parseDocument(markdown);
    for (const issue of escapeSequenceIssues(markdown)) {
        issues.push({ severity: issue.severity, message: `${relPath} ${issue.message}` });
    }
    // Annotations are authored Markdown in the same file, so they are linted
    // here rather than by a second pass a repository could forget to run.
    for (const issue of annotationIssues(markdown)) {
        issues.push({ severity: issue.severity, message: `${relPath} ${issue.message}` });
    }
    const allowedStatus = STATUS_BY_FOLDER[kind];
    const resting = restingStatusFor(kind);
    const optionalFields = new Set([
        ...COMMON_OPTIONAL_FIELDS,
        ...FILE_ONLY_FIELDS,
        ...FOLDER_EXTRA_FIELDS[kind],
    ]);

    if (!fileTitle) {
        issues.push({
            severity: "error",
            message: "No top-level `#` heading found — every file needs one file-level chapter.",
        });
    } else if (!fileMeta) {
        issues.push({
            severity: "error",
            message: `File-level heading "${fileTitle}" is missing its \`meta\` block.`,
        });
    }

    // Invariants are a subpage of the domain page whose aggregates enforce
    // them. The name is the whole pairing, so a subpage of anything else, or
    // one that declares another type, is a file nobody can place.
    if (kind === "domain") {
        const declared = fileMeta ? resolveType(kind, fileMeta) : null;
        if (named.page && named.page.split(".")[0] !== "domain") {
            issues.push({
                severity: "error",
                message: `${relPath} is an invariants subpage of ${named.page}, which is not a domain page — invariants sit beside the aggregates that enforce them, as \`domain.invariants.md\` or \`domain.<name>.invariants.md\`.`,
            });
        } else if (named.page && declared !== null && declared !== "invariants") {
            issues.push({
                severity: "error",
                message: `${relPath} is an invariants subpage and has type "${declared}" — a \`*.invariants.md\` file is \`type: invariants\`.`,
            });
        }
        if (named.legacy) {
            issues.push({
                severity: "warning",
                message: `${relPath} is where invariants lived before contract 17. They are now a subpage of the domain page they belong to — \`domain.invariants.md\`, or \`domain.<name>.invariants.md\` beside a split \`domain.<name>.md\`. Run the \`017-invariants-under-domain\` migration.`,
            });
        }
    }

    // Where an open question sits, keyed by the line of the heading it is
    // attached to. Position is the anchor, so a note under `### Sub` is Sub's
    // question and never its parent's — the same rule the annotation grammar
    // states, applied here rather than re-derived.
    const openQuestions = new Map();
    // How many open notes each chapter carries, keyed the same way, so the
    // review state can be held to the findings it claims to stand on.
    const openNotes = new Map();
    for (const note of parseAnnotations(markdown)) {
        const fields = note.fields ?? {};
        const kindOf = fields.kind ?? "comment";
        const statusOf = fields.status ?? "open";
        if (statusOf !== "open" || !note.chapter) continue;
        openNotes.set(note.chapter.line, (openNotes.get(note.chapter.line) ?? 0) + 1);
        if (kindOf !== "question" || openQuestions.has(note.chapter.line)) continue;
        openQuestions.set(note.chapter.line, note.line);
    }

    for (const [index, chapter] of chapters.entries()) {
        const label = `${"#".repeat(chapter.level)} ${chapter.text} (line ${chapter.line})`;
        if (!chapter.meta) {
            // Level-1 heading already reported above as the file-level block.
            if (chapter.level > 1) {
                issues.push({
                    severity: "warning",
                    message: `${label} has no \`meta\` block. Add one if this heading is an addressable chapter for this folder.`,
                });
            }
            continue;
        }

        // `status` is required only in the folders that have no resting value.
        // Where a folder does have one, absence *is* the statement, so an
        // omitted status is correct and the resting value written out is the
        // thing worth reporting — otherwise the corpus ends up with two
        // spellings of one state and neither reader knows which to expect.
        const declaresStatus = "status" in chapter.meta;
        if (!declaresStatus || chapter.meta.status === null) {
            if (resting === null) {
                issues.push({
                    severity: "error",
                    message: `${label} is missing required \`status\`.`,
                });
            } else if (declaresStatus) {
                // An absence is spelled by leaving the field out, never by
                // writing the word `null` — same discipline as `issue: null`.
                issues.push({
                    severity: "warning",
                    message: `${label} sets \`status\` to a null value — omit the field instead to mean the resting value \`${resting}\`.`,
                });
            }
        } else if (!allowedStatus.includes(chapter.meta.status)) {
            issues.push({
                severity: "error",
                message: `${label} has status "${chapter.meta.status}", expected one of: ${allowedStatus.join(", ")}.`,
            });
        } else if (chapter.meta.status === resting) {
            issues.push({
                severity: "warning",
                message: `${label} states \`status: ${resting}\`, which is the resting value in .${kind} — omit the field instead, per the omit-when-empty rule.`,
            });
        }

        // `type` records what kind of thing this chapter or file is, in the
        // vocabulary its folder defines. Folders that define no vocabulary
        // (`.arc42`, `.design`) omit the field entirely.
        const blockLevel = chapter.level === 1 ? "file" : "chapter";
        for (const issue of typeIssues(kind, blockLevel, chapter.meta, fileBase)) {
            issues.push({ severity: issue.severity, message: `${label} ${issue.message}` });
        }

        // Which of the folder's own fields this particular block may carry.
        for (const issue of fieldScopeIssues(kind, blockLevel, chapter.meta)) {
            issues.push({ severity: issue.severity, message: `${label} ${issue.message}` });
        }

        // `feature-flag` and `setting` on a feature point at the chapter that
        // describes the switch, in the context's `context.md`. Until contract
        // 10 `feature-flag` held the bare application key; a bare key is now
        // the shape `011-context-md` rewrites, so it is reported by name.
        // Whether the reference resolves, and to a chapter of the right type,
        // is the graph build's to say.
        if (kind === "domain") {
            for (const field of ["feature-flag", "setting"]) {
                for (const entry of toList(chapter.meta[field])) {
                    if (!entry.includes("#")) {
                        issues.push({
                            severity: "error",
                            message: `${label} has \`${field}\` entry "${entry}", which is not a \`<path>#<slug>\` reference — the field points at the \`${field}\` chapter in the context's \`context.md\` that carries the key. A bare key is the pre-011 shape; run the \`011-context-md\` migration.`,
                        });
                    }
                }
            }
        }

        // A switch chapter carries the identifier the code checks. Without
        // `key` there is nothing for a flag check or a configuration read to
        // resolve to, which is the one thing the chapter is for.
        if (kind === "domain") {
            const declared = resolveType(kind, chapter.meta);
            if (declared === "feature-flag" || declared === "setting") {
                const key = chapter.meta.key;
                if (key == null || key === "" || Array.isArray(key)) {
                    issues.push({
                        severity: "error",
                        message: `${label} is a \`${declared}\` chapter without a single \`key\` — write the identifier as the code spells it.`,
                    });
                }
                const fallback = chapter.meta.default;
                if (declared === "feature-flag" && fallback != null && !FLAG_DEFAULTS.includes(fallback)) {
                    issues.push({
                        severity: "error",
                        message: `${label} has \`default\` "${fallback}" on a feature flag, expected one of: ${FLAG_DEFAULTS.join(", ")}.`,
                    });
                }
                const scope = chapter.meta.scope;
                if (declared === "setting" && scope != null && !SETTING_SCOPES.includes(scope)) {
                    issues.push({
                        severity: "error",
                        message: `${label} has \`scope\` "${scope}", expected one of: ${SETTING_SCOPES.join(", ")}.`,
                    });
                }
            }
        }

        if (kind === "domain") {
            const deployment = chapter.meta.deployment;
            if (deployment != null && !CONTEXT_DEPLOYMENTS.includes(deployment)) {
                issues.push({
                    severity: "error",
                    message: `${label} has \`deployment\` "${deployment}", expected one of: ${CONTEXT_DEPLOYMENTS.join(", ")}.`,
                });
            }
        }

        // `effort` is a story-point estimate, so it is a single non-negative
        // integer. A list, a fraction, a negative number, or a word such as
        // "large" is not an estimate this schema can total or compare.
        if (chapter.meta.effort != null) {
            const raw = chapter.meta.effort;
            const isInteger = typeof raw === "string" && /^\d+$/.test(raw);
            if (!isInteger) {
                issues.push({
                    severity: "error",
                    message: `${label} has \`effort\` "${Array.isArray(raw) ? raw.join(", ") : raw}" — effort is a story-point estimate and must be a single non-negative integer.`,
                });
            }
        }

        // A `.ai` chapter says where it sits on the DevOps loop with `stage`,
        // and the file it is in says nothing about that — so the field is on
        // every chapter, checked against the fixed vocabulary rather than the
        // repository's own words. The one chapter that may omit it is a
        // `concept` that applies throughout, drawn in the middle of the loop;
        // any other chapter without it is off the picture, which is reported
        // as a warning rather than an error so an existing folder keeps
        // validating while its chapters are placed.
        if (kind === "ai" && blockLevel === "chapter") {
            if (chapter.meta.stage != null) {
                for (const word of toList(chapter.meta.stage)) {
                    if (!AI_STAGES.includes(word)) {
                        issues.push({
                            severity: "error",
                            message: `${label} has \`stage\` entry "${word}", expected one of: ${AI_STAGES.join(", ")} — the stages are the DevOps loop's own, so every repository draws the same loop.`,
                        });
                    }
                }
            } else if (resolveType(kind, chapter.meta) !== "concept") {
                issues.push({
                    severity: "warning",
                    message: `${label} has no \`stage\` — a usage says which stages of the loop it applies at, or it is off the picture. Only a \`concept\` applied throughout omits it.`,
                });
            }
        }

        // A `.ai` usage that rests on a registered technology names it in
        // `depends-on`, which is the edge the loop picture draws the tool from.
        // The same reference in `related` resolves, builds, and draws nothing —
        // so it is reported here, where the author's intent is obvious.
        if (kind === "ai" && blockLevel === "chapter" && chapter.meta.related != null) {
            for (const ref of toList(chapter.meta.related)) {
                if (typeof ref === "string" && ref.startsWith(`${DEVBOOK_PREFIX}tech/`)) {
                    issues.push({
                        severity: "warning",
                        message: `${label} has \`related\` entry "${ref}" reaching into .tech — a usage names the technology it rests on in \`depends-on\`, which is what puts the tool on the loop picture; \`related\` draws nothing there.`,
                    });
                }
            }
        }

        // Roadmap entries are tag slugs an application groups work by, not
        // `<path>#<slug>` chapter references, so the tag vocabulary lives in the
        // consuming repository and only the slug shape is checked here.
        if (chapter.meta.roadmap != null) {
            for (const tag of toList(chapter.meta.roadmap)) {
                if (!ROADMAP_TAG_PATTERN.test(tag)) {
                    issues.push({
                        severity: "warning",
                        message: `${label} has \`roadmap\` entry "${tag}" — roadmap tags are lowercase kebab-case slugs, not chapter references or free text.`,
                    });
                }
            }
        }

        // `tests` names the test cases that assert what this chapter claims,
        // as `<level>:<runner>:<selector>` identifiers a runner can resolve.
        for (const issue of testIssues(chapter.meta)) {
            issues.push({ severity: issue.severity, message: `${label} ${issue.message}` });
        }

        // A rule chapter is one rule plus the cases that prove it, and the
        // level of proof follows the file it sits in. Both are reported as
        // coverage warnings — see `behaviourIssues`. Whether the chapter's
        // `related` reaches the prose half it belongs to is the graph build's
        // to say, since only it can resolve across files.
        if (kind === "domain" && blockLevel === "chapter") {
            for (const issue of behaviourIssues(
                resolveType(kind, chapter.meta),
                chapter.meta,
                scenarioCount(chapters, index)
            )) {
                issues.push({ severity: issue.severity, message: `${label} ${issue.message}` });
            }
        }

        // The approval gate writes into the chapter, so the chapter is where
        // the record is checked. The content is only fingerprinted when the
        // chapter claims one — most do not, and hashing every block to learn
        // that would be work for nothing.
        // Only `domain/` has the rungs, so only there is there a record to
        // lint. Elsewhere the six fields are not in that folder's vocabulary at
        // all, and the unrecognized-field check below reports each one once —
        // running this too would report one mistake twice.
        if (kind === "domain") {
            const claimsHash =
                chapter.meta[CONTENT_HASH_FIELD] != null || chapter.meta[ACCEPTED_HASH_FIELD] != null;
            const contentHash = claimsHash ? chapterHash(markdown, chapter.line) : null;
            for (const issue of approvalIssues(chapter.meta, contentHash)) {
                issues.push({ severity: issue.severity, message: `${label} ${issue.message}` });
            }
        }

        // The review workflow writes into the chapter too, and its state is
        // only consistent against the notes beside it.
        for (const issue of reviewIssues(chapter.meta, openNotes.get(chapter.line) ?? 0)) {
            issues.push({ severity: issue.severity, message: `${label} ${issue.message}` });
        }

        // An open question means the chapter is not agreed, so an approval
        // standing over one is a false record: a person signed for content
        // that still has an unanswered question in it. Reported here, and only
        // here — an open question on any other rung is the state the fence
        // exists for, and a gate that warned on every one would be teaching
        // people to ignore it.
        if (
            (chapter.meta.status === APPROVED_STATUS || chapter.meta.status === ACCEPTED_STATUS) &&
            openQuestions.has(chapter.line)
        ) {
            issues.push({
                severity: "error",
                message: `${label} states \`status: ${chapter.meta.status}\` while carrying an open \`kind: question\` annotation (line ${openQuestions.get(chapter.line)}) — an open question means the chapter is not agreed. Resolve and sweep the note, or take the rung off.`,
            });
        }

        for (const issue of removedFieldIssues(chapter.meta)) {
            issues.push({ severity: issue.severity, message: `${label} ${issue.message}` });
        }

        for (const issue of outlineFieldIssues(
            relPath,
            chapter.meta,
            chapter.level === 1 ? "file" : "chapter"
        )) {
            issues.push({ severity: issue.severity, message: `${label} ${issue.message}` });
        }

        for (const [key, value] of Object.entries(chapter.meta)) {
            if (isExtensionField(key)) continue; // opaque by contract — never validated
            if (key === "status") continue; // recognized, and fully reported above
            if (key in REMOVED_FIELDS) continue; // already reported above
            if (!optionalFields.has(key)) {
                issues.push({
                    severity: "warning",
                    message: `${label} has unrecognized field \`${key}\` for the ${kind} folder.`,
                });
                continue;
            }
            const isEmptyList = Array.isArray(value) && value.length === 0;
            if (isEmptyList || value === null) {
                issues.push({
                    severity: "warning",
                    message: `${label} sets \`${key}\` to an empty/null value — omit the field instead per the omit-when-empty rule.`,
                });
            }
        }
    }

    return issues;
}

// ---------------------------------------------------------------------------
// Annotations — the second fenced block
// ---------------------------------------------------------------------------
//
// A note on a chapter belongs in the chapter. An annotation is the same device
// as `meta`, one word different, placed in the body where the note belongs:
//
//     ```annotation
//     author: jobsc
//     date: 2026-09-02
//     body: Is this still true now the outline rollup is a view?
//     ```
//
// Writing a note as authored Markdown rather than as a row in the derived
// database is what makes `devbook.db` wholly derived again — position is the
// anchor, git is the sync and the backup, the pull request is the review, and
// `git blame` is the authorship record. None of that has to be built.
//
// The block grammar is richer than `meta`'s deliberately flat one: a thread
// needs a `body` that runs to a paragraph, an ordered `replies` list, and a
// nested `ext` mapping. So annotations get their own small indentation-aware
// reader below rather than bending `parseMetaBody`, which stays flat because
// every field it carries is a scalar or a bracket list.

const ANNOTATION_KINDS = ["comment", "question", "suggestion", "flag"];

// Two states, and `resolved` is short-lived. An annotation is an open loop, not
// a record: `resolved` lives for the rest of the branch so a reviewer sees the
// exchange in the pull request that raised it, and the sweep deletes it before
// the merge. git holds the history — the commit that removed the note, and the
// change that answered it.
const ANNOTATION_STATUSES = ["open", "resolved"];

const ANNOTATION_DEFAULTS = { kind: "comment", status: "open" };

// Closed core set. Anything a workflow above L0 wants to remember goes under
// `ext:` in its own namespace, which is validated as a mapping and never read
// into — the same seam the chapter's `meta` block already offers.
const ANNOTATION_FIELDS = [
    "kind",
    "status",
    "author",
    "date",
    "quote",
    "body",
    "replies",
    "ext",
];

// `author` is written, never inferred: a note survives rewrites that `git blame`
// does not follow, so the name has to travel inside the fence.
const ANNOTATION_REQUIRED_FIELDS = ["author", "date", "body"];

const ANNOTATION_REPLY_FIELDS = ["author", "date", "body"];

const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

function lineIndent(line) {
    return /^ */.exec(line)[0].length;
}

function isBlankLine(line) {
    return line.trim() === "";
}

/**
 * Parse the body of a fenced ```annotation block into a nested object.
 *
 * Supports exactly the shapes the annotation schema uses: `key: scalar`, a `|`
 * block scalar, a list of mappings (`replies`), and a nested mapping (`ext`).
 * Anything else in the file is not part of this schema and is reported by
 * `annotationIssues` rather than silently absorbed.
 */
export function parseAnnotationBody(body) {
    const lines = body.split("\n");
    let first = 0;
    while (first < lines.length && isBlankLine(lines[first])) first++;
    if (first >= lines.length) return {};
    const [value] = parseAnnotationMapping(lines, first, lines.length, lineIndent(lines[first]));
    return value;
}

function parseAnnotationMapping(lines, start, end, indent) {
    const map = {};
    let i = start;
    while (i < end) {
        if (isBlankLine(lines[i])) {
            i++;
            continue;
        }
        if (lineIndent(lines[i]) < indent) break;
        const match = /^([^:\s][^:]*):[ \t]?(.*)$/.exec(lines[i].slice(indent));
        if (!match) {
            i++;
            continue;
        }
        const key = match[1].trim();
        const rest = match[2].trim();
        if (rest === "|" || rest === "|-" || rest === ">") {
            const [text, next] = parseAnnotationBlockScalar(lines, i + 1, end, indent);
            map[key] = text;
            i = next;
        } else if (rest === "") {
            const [child, next] = parseAnnotationNested(lines, i + 1, end, indent);
            map[key] = child;
            i = next;
        } else {
            map[key] = stripQuotes(rest);
            i++;
        }
    }
    return [map, i];
}

/** A `|` body: every more-indented line, dedented by the block's own indent. */
function parseAnnotationBlockScalar(lines, start, end, parentIndent) {
    const collected = [];
    let i = start;
    let blockIndent = null;
    while (i < end) {
        if (isBlankLine(lines[i])) {
            collected.push("");
            i++;
            continue;
        }
        const indent = lineIndent(lines[i]);
        if (indent <= parentIndent) break;
        if (blockIndent === null) blockIndent = indent;
        collected.push(lines[i].slice(Math.min(blockIndent, indent)));
        i++;
    }
    while (collected.length && collected[collected.length - 1] === "") collected.pop();
    return [collected.join("\n"), i];
}

/** What follows a bare `key:` — a list of items, or a nested mapping. */
function parseAnnotationNested(lines, start, end, parentIndent) {
    let i = start;
    while (i < end && isBlankLine(lines[i])) i++;
    if (i >= end || lineIndent(lines[i]) <= parentIndent) return [null, i];
    const childIndent = lineIndent(lines[i]);
    if (/^-\s/.test(lines[i].slice(childIndent))) {
        return parseAnnotationList(lines, i, end, childIndent);
    }
    return parseAnnotationMapping(lines, i, end, childIndent);
}

function parseAnnotationList(lines, start, end, indent) {
    const items = [];
    let i = start;
    while (i < end) {
        if (isBlankLine(lines[i])) {
            i++;
            continue;
        }
        if (lineIndent(lines[i]) < indent) break;
        if (!/^-\s/.test(lines[i].slice(indent))) break;

        // Gather this item's lines, then re-indent its first line so the whole
        // item parses as an ordinary mapping two columns in from the dash.
        const itemLines = [`${" ".repeat(indent + 2)}${lines[i].slice(indent + 2)}`];
        let j = i + 1;
        while (j < end) {
            if (isBlankLine(lines[j])) {
                itemLines.push(lines[j]);
                j++;
                continue;
            }
            const childIndent = lineIndent(lines[j]);
            if (childIndent <= indent) break;
            itemLines.push(lines[j]);
            j++;
        }
        while (itemLines.length && isBlankLine(itemLines[itemLines.length - 1])) itemLines.pop();

        const head = itemLines[0].slice(indent + 2);
        if (/^[^:\s][^:]*:/.test(head)) {
            const [value] = parseAnnotationMapping(itemLines, 0, itemLines.length, indent + 2);
            items.push(value);
        } else {
            items.push(stripQuotes(head.trim()));
        }
        i = j;
    }
    return [items, i];
}

const FENCE_PATTERN = /^(\s*)(`{3,}|~{3,})\s*([^\s`~]*)\s*$/;

/**
 * The fingerprint of one block's content, for `approved-hash` and
 * `accepted-hash`.
 *
 * What is hashed is the block a reader would say they read: its heading text
 * and everything under it, down to the next heading at the same or a higher
 * level. So a `#` file block covers the whole file, a `##` chapter covers its
 * `###` subsections, and approving a nested chapter and its parent leaves two
 * records that both lapse when the nested one is edited. Boundaries come from
 * `parseDocument`, so they are the same boundaries every other check uses.
 *
 * Three things are excluded, each for its own reason. The `meta` blocks go
 * because the hash lives in one, and a value cannot be part of what it
 * fingerprints. The `annotation` fences go because a note written after the
 * approval is not a change to the content — an open question standing over an
 * approved chapter is already its own error, and lapsing the rung for a
 * resolved note would report the sweep as an edit. Whitespace goes because a
 * reflowed paragraph reads identically, and a rung that came off every time
 * someone rewrapped a line would be taken off for good.
 *
 * Heading *text* is in, the `#` markers are not: renaming a chapter changes
 * what it claims and should lapse the approval, while promoting one changes
 * its address and not a word of what was read.
 *
 * `line` is the block's 1-based heading line, as `parseDocument` reports it.
 */
export function chapterHash(markdown, line = 1) {
    const lines = markdown.split(/\r?\n/);
    const { chapters } = parseDocument(markdown);

    const index = chapters.findIndex((entry) => entry.line === line);
    const start = index === -1 ? Math.max(0, line - 1) : chapters[index].line - 1;
    const level = index === -1 ? 1 : chapters[index].level;

    let end = lines.length;
    if (index !== -1) {
        const next = chapters.slice(index + 1).find((entry) => entry.level <= level);
        if (next) end = next.line - 1;
    }

    const kept = [];
    const heading = /^#{1,6}\s+(.*)$/.exec(lines[start] ?? "");
    if (heading) kept.push(heading[1]);

    for (let i = start + 1; i < end; i++) {
        const fence = FENCE_PATTERN.exec(lines[i]);
        if (fence) {
            const marker = fence[2];
            const label = fence[3].toLowerCase();
            const closer = new RegExp(`^\\s*\\${marker[0]}{${marker.length},}\\s*$`);
            let k = i + 1;
            while (k < lines.length && !closer.test(lines[k])) k++;
            if (label === "meta" || label === "annotation") {
                i = k;
                continue;
            }
            // Any other fence is content: a diagram or a code sample is part
            // of what was approved, so it is kept whole, closer included.
            for (let j = i; j <= k && j < end; j++) kept.push(lines[j]);
            i = k;
            continue;
        }
        kept.push(lines[i]);
    }

    const normalised = kept
        .map((entry) => entry.replace(/\s+/g, " ").trim())
        .filter((entry) => entry !== "")
        .join("\n");

    return `sha256:${createHash("sha256").update(normalised, "utf8").digest("hex").slice(0, 8)}`;
}

/**
 * Every annotation fence in a document, in reading order.
 *
 * Position is the anchor. A fence annotates the block immediately above it;
 * placed directly after a heading's `meta` block it annotates the chapter as a
 * whole. That single rule replaces content hashes, relocation prompts, and a
 * repository-wide orphaned view — the note is not pointing at the passage from
 * a distance, it is in the passage's neighbourhood, so every ordinary text
 * operation carries it along.
 *
 * Consecutive fences all attach to the last block that is not itself an
 * annotation, so a passage can carry several threads without the second one
 * claiming to annotate the first.
 *
 * Each entry carries `{ line, endLine, raw, fields, chapter, ordinal, target,
 * attachedTo }`. `ordinal` is 1-based within the chapter: a tool addresses a
 * thread as `<path>#<slug>` plus its ordinal, because devbook assigns no ids.
 */
export function parseAnnotations(markdown) {
    const lines = markdown.split(/\r?\n/);
    const annotations = [];
    const ordinals = new Map();
    let chapter = null;
    let attachedTo = null;
    let i = 0;

    while (i < lines.length) {
        const line = lines[i];

        const heading = /^(#{1,6})\s+(.*)$/.exec(line);
        if (heading) {
            const text = heading[2].trim();
            chapter = { level: heading[1].length, text, slug: slugify(text), line: i + 1 };
            attachedTo = { kind: "heading", text, line: i + 1 };
            i++;
            continue;
        }

        const fence = FENCE_PATTERN.exec(line);
        if (fence) {
            const marker = fence[2];
            const info = fence[3];
            const closer = new RegExp(`^\\s*\\${marker[0]}{${marker.length},}\\s*$`);
            const bodyLines = [];
            let k = i + 1;
            while (k < lines.length && !closer.test(lines[k])) {
                bodyLines.push(lines[k]);
                k++;
            }
            const raw = bodyLines.join("\n");
            const label = info.toLowerCase();

            if (label === "annotation") {
                const key = chapter ? chapter.slug : "";
                const ordinal = (ordinals.get(key) ?? 0) + 1;
                ordinals.set(key, ordinal);
                annotations.push({
                    line: i + 1,
                    endLine: k + 1,
                    raw,
                    fields: parseAnnotationBody(raw),
                    chapter,
                    ordinal,
                    target: attachedTo && attachedTo.kind === "text" ? "block" : "chapter",
                    attachedTo,
                });
                // `attachedTo` is deliberately not reassigned: the next fence
                // attaches to the same passage, not to this note.
            } else if (label === "meta") {
                attachedTo = { kind: "meta", text: raw, line: i + 1 };
            } else {
                attachedTo = { kind: "text", text: raw, line: i + 1 };
            }
            i = k + 1;
            continue;
        }

        if (isBlankLine(line)) {
            i++;
            continue;
        }

        // An ordinary run of prose, table, or list lines is one block.
        const blockStart = i;
        const blockLines = [];
        while (
            i < lines.length &&
            !isBlankLine(lines[i]) &&
            !FENCE_PATTERN.test(lines[i]) &&
            !/^#{1,6}\s+/.test(lines[i])
        ) {
            blockLines.push(lines[i]);
            i++;
        }
        attachedTo = { kind: "text", text: blockLines.join("\n"), line: blockStart + 1 };
    }

    return annotations;
}

/** A note's fields with the two defaulted ones filled in. */
export function resolveAnnotation(fields) {
    const stated = Object.fromEntries(
        Object.entries(fields ?? {}).filter(([, value]) => value != null)
    );
    return { ...ANNOTATION_DEFAULTS, ...stated };
}

export const annotationKinds = () => [...ANNOTATION_KINDS];
export const annotationStatuses = () => [...ANNOTATION_STATUSES];

/**
 * Lint every annotation fence in a document against the schema and the
 * placement rule.
 *
 * `quote` is checked against the block the note attaches to, and a quote that
 * matches nothing above is a *warning*, never an error: it is the tell that a
 * passage moved out from under its note, and a person decides what to do about
 * it. It is never used to resolve the attachment — position already did that.
 */
export function annotationIssues(markdown) {
    const issues = [];
    for (const note of parseAnnotations(markdown)) {
        const where = `annotation at line ${note.line}`;
        const fields = note.fields ?? {};

        if (!note.chapter) {
            issues.push({
                severity: "error",
                message: `${where} sits before the first heading — an annotation lives inside an addressable chapter.`,
            });
        }

        for (const field of ANNOTATION_REQUIRED_FIELDS) {
            const value = fields[field];
            if (value == null || (typeof value === "string" && value.trim() === "")) {
                issues.push({
                    severity: "error",
                    message: `${where} is missing required \`${field}\`.`,
                });
            }
        }

        if (fields.kind != null && !ANNOTATION_KINDS.includes(fields.kind)) {
            issues.push({
                severity: "error",
                message: `${where} has kind "${fields.kind}", expected one of: ${ANNOTATION_KINDS.join(", ")}.`,
            });
        }

        if (fields.status != null && !ANNOTATION_STATUSES.includes(fields.status)) {
            issues.push({
                severity: "error",
                message: `${where} has status "${fields.status}", expected one of: ${ANNOTATION_STATUSES.join(", ")}.`,
            });
        }

        if (typeof fields.date === "string" && !ISO_DATE_PATTERN.test(fields.date)) {
            issues.push({
                severity: "error",
                message: `${where} has date "${fields.date}" — an annotation date is \`YYYY-MM-DD\`.`,
            });
        }

        if (fields.quote != null) {
            const passage = note.attachedTo?.kind === "text" ? note.attachedTo.text : "";
            const normalize = (text) => String(text).replace(/\s+/g, " ").trim();
            if (!normalize(passage).includes(normalize(fields.quote))) {
                issues.push({
                    severity: "warning",
                    message: `${where} quotes "${fields.quote}", which matches nothing in the block above it — the passage may have moved out from under the note.`,
                });
            }
        }

        if (fields.replies != null) {
            if (!Array.isArray(fields.replies)) {
                issues.push({
                    severity: "error",
                    message: `${where} has \`replies\` that is not a list — one fence is one thread, and its replies are an ordered list of {author, date, body}.`,
                });
            } else {
                fields.replies.forEach((reply, index) => {
                    if (typeof reply !== "object" || reply === null || Array.isArray(reply)) {
                        issues.push({
                            severity: "error",
                            message: `${where} reply ${index + 1} is not a mapping of {author, date, body}.`,
                        });
                        return;
                    }
                    for (const field of ANNOTATION_REPLY_FIELDS) {
                        if (reply[field] == null || String(reply[field]).trim() === "") {
                            issues.push({
                                severity: "error",
                                message: `${where} reply ${index + 1} is missing required \`${field}\`.`,
                            });
                        }
                    }
                    for (const key of Object.keys(reply)) {
                        if (!ANNOTATION_REPLY_FIELDS.includes(key)) {
                            issues.push({
                                severity: "warning",
                                message: `${where} reply ${index + 1} has unrecognized field \`${key}\`.`,
                            });
                        }
                    }
                });
            }
        }

        // Opaque by contract: L0 checks that `ext` is a mapping of namespaces
        // and then never looks inside one. That is the whole reason an
        // extension can add a field and ship on its own release cadence.
        if (fields.ext != null) {
            const isMapping =
                typeof fields.ext === "object" &&
                !Array.isArray(fields.ext) &&
                fields.ext !== null;
            if (!isMapping) {
                issues.push({
                    severity: "error",
                    message: `${where} has \`ext\` that is not a mapping of namespaces.`,
                });
            }
        }

        for (const key of Object.keys(fields)) {
            if (!ANNOTATION_FIELDS.includes(key)) {
                issues.push({
                    severity: "warning",
                    message: `${where} has unrecognized field \`${key}\` — the core set is closed, and extension state goes under \`ext.<namespace>\`.`,
                });
            }
        }
    }
    return issues;
}
