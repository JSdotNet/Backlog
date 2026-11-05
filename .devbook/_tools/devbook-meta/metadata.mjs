// metadata.mjs — parsing and validation for the chapter/file `meta` YAML
// blocks defined in devbook-chapter-metadata.md.
//
// Dependency-free ESM against node built-ins, like everything else executable
// here: `node:crypto` is what fingerprints a chapter's content.
import { createHash } from "node:crypto";
//
// The schema used across .devbook/{arc42,domain,tech,design,ai} is intentionally small and
// flat (single-line scalars, null, or bracket lists), so we parse it with a
// tiny hand-written reader instead of pulling in a YAML dependency.
/**
 * The five devbook folders, by kind. A repository adopts any subset, and every
 * one of them lives under the one `.devbook/` parent: `.devbook/arc42`,
 * `.devbook/domain`, and so on. The parent already carries the "hidden support
 * directory" signal, so the subfolders drop the dot. There is no other layout
 * (the chapter-schema decision); five root-level dot-folders are reported, never indexed.
 */
export const DEVBOOK_FOLDER_NAMES = ["arc42", "domain", "tech", "design", "ai"];

/** The one parent folder, and the prefix that identifies a path inside it. */
export const DEVBOOK_ROOT = ".devbook";
export const DEVBOOK_PREFIX = `${DEVBOOK_ROOT}/`;

/**
 * The change folder: where a proposed change to the devbook lives until it is
 * merged. It sits outside `.devbook/` because OpenSpec, whose change lane it
 * is, resolves `changes/` only under a folder literally named `openspec/` —
 * the spike recorded in the repository's devbook-openspec building block. The
 * names are OpenSpec's and fixed. A repository adopts it like any folder, by
 * having it; `archive/` inside it is history and never indexed.
 *
 * Of each change, `proposal.md` and every file under `devbook-delta/` are
 * devbook chapters. `solution.md` and `tasks.md` are the change's own working
 * files, and nothing in them lands in the devbook.
 */
export const CHANGES_FOLDER = "changes";
export const CHANGES_ROOT = "openspec/changes";
export const CHANGES_ARCHIVE = `${CHANGES_ROOT}/archive`;
export const DELTA_FOLDER = "devbook-delta";

/**
 * Where a path sits in the change folder, or null outside it and inside
 * `archive/`. `part` is `proposal`, `delta`, or `other`; a delta's `target` is
 * the devbook file it changes — its path under `devbook-delta/`, re-rooted at
 * `.devbook/`.
 */
export function changePathParts(relPath) {
    const normalized = String(relPath).replace(/\\/g, "/");
    if (!normalized.startsWith(`${CHANGES_ROOT}/`)) return null;
    const [name, ...rest] = normalized.slice(CHANGES_ROOT.length + 1).split("/");
    if (!name || name === "archive") return null;
    const inner = rest.join("/");
    if (inner === "proposal.md") return { name, part: "proposal", target: null };
    if (rest[0] === DELTA_FOLDER && rest.length > 1) {
        return { name, part: "delta", target: `${DEVBOOK_PREFIX}${rest.slice(1).join("/")}` };
    }
    return { name, part: "other", target: null };
}

// Workflow state is not content (the chapter-schema decision, contract 38). A
// branch's devbook states what is true of its code, so a merged chapter is in
// force and carries no status. A change proposal, a decision record under
// discussion, and a debt record have state, and the tool that runs the
// workflow keeps it outside git, answering `devbook.decision@1`.
//
// The one decision a chapter still records is a `tech/` or `ai/` rating: where
// a technology, or a way of working with one, stands on the adoption ladder.
// The branch makes that decision, so it stays, and it is always stated — an
// unrated technology is not a `candidate` one. `ai/` reuses `tech/`'s ladder,
// so a reader learns one adoption vocabulary. It was spelled `status` until
// contract 38; renaming it keeps one word from meaning two things.
export const RATING_FIELD = "rating";
const RATINGS = ["candidate", "trial", "adopted", "hold", "retired"];
const RATING_BY_FOLDER = { tech: RATINGS, ai: RATINGS };

/** The ratings a folder's blocks take, or `[]` in a folder that rates nothing. */
export const ratingsFor = (folder) => [...(RATING_BY_FOLDER[folder] ?? [])];

// A debt record under `arc42/tdr/` is debt on its way to being paid. Its state
// is the tool's; its `severity`, what the debt costs while it stands, is
// content and sits on its top-level block. Contract 33.
const DEBT_FOLDER = `${DEVBOOK_PREFIX}arc42/tdr/`;

// What a debt costs while it stands, on a debt record's top-level block only.
export const DEBT_SEVERITIES = ["low", "medium", "high", "critical"];

// A decision record's `## History` table header, fixed so a reader can list
// one concern's decisions across every record.
const DECISION_FOLDER = `${DEVBOOK_PREFIX}arc42/adr/`;
export const HISTORY_HEADER = ["Date", "Change", "Why"];

/**
 * Lint a decision record's `## History` section, per `devbook-arc42.md`: an
 * `arc42/adr/` file other than the folder's index holds the section, and its
 * table is headed `Date | Change | Why`. Every finding is a warning: a record
 * written before the header was fixed needs a person to split each row's
 * reason into its own column, and keeps validating until one does.
 */
export function historyHeaderIssues(relPath, markdown, fileMeta) {
    const normalized = String(relPath).replace(/\\/g, "/");
    if (!normalized.startsWith(DECISION_FOLDER) || !normalized.endsWith(".md") || fileMeta?.index === "root") return [];
    const expected = HISTORY_HEADER.join(" | ");
    const warn = (message) => [{ severity: "warning", message }];

    // A table is a header row with a pipe, then a delimiter row; a leading
    // pipe is optional in GFM, and `\|` is a pipe inside a cell.
    const DELIMITER_ROW = /^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$/;
    const lines = String(markdown).split(/\r?\n/);
    let fence = null;
    let inHistory = false;
    for (let index = 0; index < lines.length; index++) {
        const text = lines[index];
        const marker = /^\s{0,3}(`{3,}|~{3,})/.exec(text);
        if (marker) {
            const run = marker[1];
            if (!fence) fence = run;
            else if (run[0] === fence[0] && run.length >= fence.length && !text.slice(text.indexOf(run) + run.length).trim()) fence = null;
            continue;
        }
        if (fence) continue;
        if (/^\s{0,3}#{1,2}\s/.test(text)) {
            if (inHistory) break;
            inHistory = /^\s{0,3}##\s+History\s*#*\s*$/.test(text);
            continue;
        }
        if (!inHistory || !text.includes("|") || !DELIMITER_ROW.test(lines[index + 1] ?? "") || !lines[index + 1].includes("-")) continue;
        const header = text.trim().replace(/^\|/, "").replace(/(?<!\\)\|\s*$/, "").split(/(?<!\\)\|/).map((cell) => cell.trim());
        if (header.join(" | ") === expected) return [];
        return warn(`has a \`## History\` table headed \`${header.join(" | ")}\` on line ${index + 1} — a decision record's History is headed \`${expected}\`, so a reader can list a concern's decisions in order. Move each row's reason into the Why column. See devbook-arc42.md.`);
    }
    return inHistory
        ? warn(`has a \`## History\` section with no table — head one \`${expected}\`, newest row first. See devbook-arc42.md.`)
        : warn(`has no \`## History\` section — a decision record ends on a table headed \`${expected}\`, newest row first. See devbook-arc42.md.`);
}

/**
 * Whether this block is a debt record's top-level block: a file under
 * `arc42/tdr/` other than the folder's index, at file level.
 */
export function isDebtRecord(relPath, blockLevel, fileMeta) {
    const normalized = String(relPath).replace(/\\/g, "/");
    return blockLevel === "file" && normalized.startsWith(DEBT_FOLDER) && normalized.endsWith(".md") && fileMeta?.index !== "root";
}

/** Whether a `code` entry is a folder written from the repository root: no leading `/`, drive, `.`, `..`, or anchor. */
export function isRepoFolder(entry) {
    const clean = String(entry ?? "").replace(/\\/g, "/").replace(/\/+$/, "");
    if (!clean || clean.startsWith("/") || /^[A-Za-z]:/.test(clean) || clean.includes("#")) return false;
    return !clean.split("/").some((part) => part === ".." || part === ".");
}

// Allowed `type` values per folder, split by block level. `type` records *what
// kind of thing* a chapter or file is — the classification that used to be
// written as a heading prefix (`## Aggregate: Order`). Heading text now carries
// the name alone, so anchors are slugs of the bare name.
//
// A folder whose lists are empty defines no kind distinction of its own: in
// `.arc42` the only such distinction (chapter vs section) is already carried
// by heading level, so inventing values there would restate the document
// structure. `type` is omitted there and reported when used. `.design` is the
// same apart from its one `requirement` kind.
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
            // `scenarios.md`: one end-to-end journey per `##`, its `###`
            // parts owing no block. A requirement's case points at a part, so
            // a journey proves the requirements of as many features as it
            // walks through.
            "scenario",
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
            "scenarios",
            "model",
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
            // The `.ai` words, meaning the same thing: a technology that is a
            // Claude Code plugin, skill, or agent. `.ai` records how it is
            // used; `.tech` registers and rates the thing itself.
            "plugin",
            "skill",
            "agent",
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
    // `.design` defines one kind and nothing else: a rule a component either
    // keeps or breaks is a `### Requirement:` under the component's chapter,
    // typed so a tool that reads OpenSpec reads it. Every other `.design`
    // chapter is a guideline and stays untyped — see `OPTIONAL_TYPE_FOLDERS`.
    design: { chapter: ["requirement"], file: [] },
    // A change's `proposal.md` is a `change`. Its sections are sections, and a
    // delta's chapters are typed by the folder they land in, never by this one.
    [CHANGES_FOLDER]: { chapter: [], file: ["change"] },
};

// Folders whose value set marks out a few chapters rather than classifying
// every one. A block there may omit `type`, and a declared one must still be
// in the set.
const OPTIONAL_TYPE_FOLDERS = ["design"];

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
// extension's state became schema fields instead, and then moved to the hosts
// (the annotations decision).
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
    "date",
    "tests",
    // The places in a click demo that show what the chapter claims. The
    // addresses are resolved against each demo's `demo-model` by demo.mjs.
    "demo",
];

// A feature flag is a switch, so its `default` is one of two words. A setting's
// `default` is whatever value the product ships with and is not enumerated.
const FLAG_DEFAULTS = ["on", "off"];

// Who may change a setting at runtime: the person it belongs to, an
// administrator for the whole tenant, or an operator for the whole system.
const SETTING_SCOPES = ["user", "tenant", "system"];

// What kind of change a proposal is, named once because it decides which flow
// applies a step: new functionality, a change to behaviour that exists, or a
// defect. OpenSpec's `devbook` schema asks for it in the same three words.
export const CHANGE_CATEGORIES = ["feature", "behaviour-change", "defect"];

// A change's name as its folder spells it, and so as `change` spells it in a
// delta's header.
const CHANGE_NAME_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/;

// How a bounded context ships: as its own deployable `service`, or as a
// `module` inside a modular monolith that hosts other contexts beside it.
const CONTEXT_DEPLOYMENTS = ["service", "module"];

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
// Every devbook path sits under `.devbook/`; the root-level dot-folder spelling
// of the retired layout is still recognized, since the intent reads the same.
const DEVBOOK_PATH_PREFIX = /^(?:\.devbook\/|\.)(?:domain|arc42|tech|design|ai)\//;

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
//
// Keyed by folder, then type: `.design`'s `requirement` is a rule a component
// keeps or breaks on screen, so it is held to a different level than a
// bounded context's.
const BEHAVIOUR_TEST_LEVELS = {
    domain: {
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
    },
    design: {
        requirement: {
            levels: ["e2e"],
            reason:
                "a design requirement is proved `e2e` — a component keeps or breaks it in what the user sees and operates, so what proves it is the rendered component driven by keyboard or pointer, or compared by a visual test",
        },
    },
};

// The cases that prove one rule. They are structural headings one level under
// the rule's own, so they are found by their text rather than by a block.
const SCENARIO_HEADING = /^Scenario:/i;

// A journey: one end-to-end walk through the product, written as a `##`
// chapter of a scenarios file — `scenarios.md`, a split `scenarios.<name>.md`,
// or its subpage `scenarios.<name>.<page>.md` — in the bounded context holding most of the requirements it proves. Its
// `###` headings are its parts, sections that owe no block and stay
// addressable. Two words stay apart in every rule — a *journey* is the
// `scenario` chapter; a scenario *case* is the `#### Scenario:` under a
// requirement, which may point at one of a journey's parts. Before contract 32
// a journey was a whole file typed `scenario`; `032-scenarios-md` folds each
// such page into its context's `scenarios.md`.
export const SCENARIO_TYPE = "scenario";

/**
 * The fields that say where a journey starts and in which configuration it
 * runs, legal on a journey's block and nowhere else. `start`
 * is the route, `[portal:]/route`; `actor` the actor chapter signed in,
 * `[portal:]<path>#<slug>`; `data` the named data sets imported first;
 * `profile` the one configuration in `.devbook/scenarios/profiles.json`;
 * `flags` the feature-flag chapters switched on (`-` switches one off);
 * `settings` the setting chapters set, as `<path>#<slug>=<value>`.
 */
export const SCENARIO_SETUP_FIELDS = ["start", "actor", "data", "profile", "flags", "settings"];

// The words a step opens with. A journey is English only, and the keyword
// set is the one place that is checked.
const STEP_KEYWORDS = ["Given", "When", "Then", "And"];

// A `#### Scenario:` that a journey proves instead of spelling out its own
// Given/When/Then: one line, `Proved by: <scenarios file>.md#<part>`.
const PROVED_BY_LINE = /^\s*Proved by:\s*(.*?)\s*$/;
const GWT_LINE = /^\s*(?:[-*+]|\d+[.)])\s+\*\*(?:given|when|then|and)\*\*/i;
const SETUP_PORTAL = /^([a-z0-9][a-z0-9-]*):(.+)$/;
const SETUP_NAME = /^[a-z0-9]+(-[a-z0-9]+)*$/;

/**
 * Whether a document is a scenarios file: `scenarios.md`, a split
 * `scenarios.<name>.md`, or a split's subpage `scenarios.<name>.<page>.md`,
 * directly in a bounded context folder.
 */
export function isScenariosFile(relPath) {
    if (folderKindForPath(relPath) !== "domain") return false;
    const segments = String(relPath).replace(/\\/g, "/").slice(DEVBOOK_PREFIX.length + "domain".length + 1).split("/");
    const { base, page } = domainFileName(relPath);
    return segments.length === 2 && base === "scenarios" && !page && segments[1].split(".").length <= 4;
}

/** Split a portal prefix off a setup entry: `admin:/route` → `{ portal: "admin", rest: "/route" }`. */
export function splitPortal(entry) {
    const match = SETUP_PORTAL.exec(String(entry));
    return match ? { portal: match[1], rest: match[2] } : { portal: null, rest: String(entry) };
}

/**
 * Every top-level list item in a journey's part — below a `###` of a
 * scenarios file, until the next `##` — whose opening is not a bold Given,
 * When, Then or And. Fences are content and are skipped; a nested item belongs
 * to the step above it.
 */
export function scenarioStepIssues(markdown) {
    const issues = [];
    let fence = null;
    let inPart = false;
    markdown.split(/\r?\n/).forEach((line, index) => {
        const marker = /^\s*(`{3,}|~{3,})/.exec(line);
        if (fence) {
            if (marker && marker[1][0] === fence[0] && marker[1].length >= fence.length) fence = null;
            return;
        }
        if (marker) {
            fence = marker[1];
            return;
        }
        if (/^##\s/.test(line)) inPart = false;
        if (/^###\s/.test(line)) inPart = true;
        if (!inPart) return;
        const item = /^ ?(?:[-*+]|\d+[.)])\s+(.*)$/.exec(line);
        if (!item) return;
        const bold = /^\*\*([^*]+)\*\*/.exec(item[1]);
        if (bold && STEP_KEYWORDS.includes(bold[1].trim())) return;
        issues.push({
            severity: "error",
            message: bold
                ? `line ${index + 1} opens a step with "**${bold[1]}**" — a step opens with a bold ${STEP_KEYWORDS.join(", ")}, and a journey is English only.`
                : `line ${index + 1} is a list item in a part that opens with no bold ${STEP_KEYWORDS.join(", ")} — every list item in a journey's part is a step.`,
        });
    });
    return issues;
}

/**
 * The `#### Scenario:` cases under each `requirement` chapter, with any
 * `Proved by:` pointers their bodies hold and whether they also spell out
 * Given/When/Then. One entry per case: `{ requirement, heading, line,
 * pointers: [{ target, line }], gwt }`, `requirement` being the chapter's
 * `<path>#<slug>` id.
 */
export function provedByCases(relPath, markdown) {
    const folder = folderKindForPath(relPath);
    const { chapters } = parseDocument(markdown);
    const lines = markdown.split(/\r?\n/);
    const cases = [];
    chapters.forEach((rule, index) => {
        if (!rule.meta || resolveType(folder, rule.meta) !== "requirement") return;
        for (let i = index + 1; i < chapters.length && chapters[i].level > rule.level; i++) {
            const heading = chapters[i];
            if (heading.level !== rule.level + 1 || !SCENARIO_HEADING.test(heading.text)) continue;
            const end = chapters[i + 1]?.line ?? lines.length + 1;
            const pointers = [];
            let gwt = false;
            let fence = null;
            for (let n = heading.line; n < end - 1; n++) {
                const line = lines[n];
                const marker = /^\s*(`{3,}|~{3,})/.exec(line);
                if (fence) {
                    if (marker && marker[1][0] === fence[0] && marker[1].length >= fence.length) fence = null;
                    continue;
                }
                if (marker) {
                    fence = marker[1];
                    continue;
                }
                const proved = PROVED_BY_LINE.exec(line);
                if (proved) pointers.push({ target: proved[1], line: n + 1 });
                else if (GWT_LINE.test(line)) gwt = true;
            }
            cases.push({ requirement: `${relPath}#${rule.slug}`, heading: heading.text, line: heading.line, pointers, gwt });
        }
    });
    return cases;
}

// What each setup field holds, checked on the page alone. Whether a reference
// resolves, and to the right kind of chapter, is the graph build's to say.
function setupFieldIssues(field, value) {
    const issues = [];
    const error = (message) => issues.push({ severity: "error", message });
    const single = ["start", "profile"];
    if (single.includes(field) && Array.isArray(value)) {
        error(`has \`${field}\` as a list — a page has one ${field === "start" ? "starting route" : "profile"}; a journey that must hold under another is a page of its own.`);
        return issues;
    }
    for (const entry of toList(value)) {
        if (field === "start") {
            const { rest } = splitPortal(entry);
            if (!rest.startsWith("/")) error(`has \`start\` "${entry}" — a starting point is a route, \`/path\` or \`<portal>:/path\`, relative to the profile's base URL.`);
        } else if (field === "profile" || field === "data") {
            if (!SETUP_NAME.test(entry)) error(`has \`${field}\` entry "${entry}" — a ${field === "profile" ? "profile" : "data set"} is named in lowercase kebab-case.`);
        } else if (field === "actor") {
            if (!splitPortal(entry).rest.includes("#")) error(`has \`actor\` entry "${entry}", which is not a \`[portal:]<path>#<slug>\` reference to an actor chapter.`);
        } else if (field === "flags") {
            if (!String(entry).replace(/^-/, "").includes("#")) error(`has \`flags\` entry "${entry}", which is not a \`[-]<path>#<slug>\` reference to a feature-flag chapter.`);
        } else if (field === "settings") {
            const at = String(entry).indexOf("=");
            if (at <= 0 || !String(entry).slice(0, at).includes("#") || at === String(entry).length - 1) {
                error(`has \`settings\` entry "${entry}", which is not \`<path>#<slug>=<value>\` — a setting chapter and the value this journey sets it to.`);
            }
        }
    }
    return issues;
}

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
    // A review in progress is workflow state: the host that keeps the chapter's
    // state holds its notes, and who owes the next move belongs to the pull
    // request or the tracker. Contract 21.
    ...Object.fromEntries(
        ["review", "reviewer", "review-at"].map((field) => [
            field,
            "a review in progress is the host's: its notes are comment threads read through " +
                "`devbook.decision@1`, and who owes the next move lives in the pull request or the tracker. " +
                "Run the `021-no-review-triad` migration, which deletes it.",
        ])
    ),
    // Workflow state left the chapter in contract 38: the tool that runs the
    // workflow keeps it, and `038-workflow-state-leaves-the-chapter` deletes
    // each of these. The messages say where each went.
    status: movedToTooling(
        "a merged chapter is in force and carries no status. A change proposal's, a decision " +
            "record's, and a debt record's state is kept by the tool that runs the workflow, through " +
            "`devbook.decision@1`, and a chapter that should no longer be followed is removed by a " +
            "change's `REMOVED` delta"
    ),
    ...Object.fromEntries(
        ["approved-by", "approved-at", "approved-hash", "accepted-by", "accepted-at", "accepted-hash"].map((field) => [
            field,
            movedToTooling(
                "approval and acceptance are decisions on a change proposal, kept by the tool that " +
                    "runs the workflow over the proposal's fingerprint, through `devbook.decision@1`; a chapter keeps none"
            ),
        ])
    ),
    sync: movedToTooling("which units a sweep levels, and which way, is Backlog's direction map per repository and folder"),
    issue: movedToTooling("the tracker keeps its own reference to the chapter's address"),
    effort: movedToTooling("an estimate belongs to a change proposal's tasks and the work item each becomes"),
    roadmap: movedToTooling("dropped — Backlog's goals and roadmap plans group the work"),
    change: movedToTooling("dropped — git history and the archived change folder record the merge"),
};

/** The message for a field contract 38 moved out of the chapter. */
function movedToTooling(where) {
    return `it moved to tooling: ${where}. Run the \`038-workflow-state-leaves-the-chapter\` migration, which deletes it.`;
}

// `status` in `tech/` and `ai/` was a rating, and is renamed rather than moved.
const RENAMED_RATING =
    "a `tech/` or `ai/` rating is the `rating` field since contract 38. Run the " +
    "`038-workflow-state-leaves-the-chapter` migration, which renames it.";

// The C4 model's two fields: the workspace elements a chapter is about, and
// the ones its consequences name. Each entry is a bare identifier of
// `.devbook/arc42/workspace.dsl`, resolved by the graph build. Contract 33.
export const C4_FIELDS = ["c4", "c4-touches"];
const C4_IDENTIFIER = /^[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*$/;
const BUILDING_BLOCK_FILE = /^\.devbook\/arc42\/building-blocks\/[^/]+\.md$/;

const FOLDER_EXTRA_FIELDS = {
    domain: [
        "depends-on", "aliases", "feature-flag", "setting", "role", "key", "default", "scope",
        "deployment",
        // The playthrough a feature's page shows: a demo walkthrough that plays
        // a whole journey, resolved by demo.mjs. Contract 36.
        "playthrough",
        ...C4_FIELDS,
    ],
    arc42: [...C4_FIELDS, "code", "severity"],
    tech: [RATING_FIELD, "kind", "version", "depends-on", "alternatives"],
    design: [],
    ai: [RATING_FIELD, "depends-on", "stage", "substage", "layer", "feeds", "evidence"],
    [CHANGES_FOLDER]: ["category"],
};

// The flow page of `domain/`, retired by contract 37: a diagram opens the
// chapter of what it draws. Named only to point at the migration.
const RETIRED_FLOW_TYPE = "flow";

// The DevOps loop's words, retired by contract 35; named only to point at the migration.
const LOOP_WORDS = ["plan", "build", "test", "deploy", "monitor"];

/**
 * The seven stages of the delivery path, in path order: the first four sit
 * before the pull request, the last three after it, and `operate` feeds
 * `decide`. A `.ai` chapter says where it sits with `stage`, a list of these
 * words. The vocabulary is fixed rather than the repository's own so that a
 * tool draws one path for every repository and a reader can compare two; a
 * stage a flow does not use stays empty. Exported for the consumer that draws
 * the views, with the display names and the pull request's position beside it.
 */
export const AI_STAGES = ["decide", "specify", "code", "verify", "integrate", "release", "operate"];
export const AI_STAGE_NAMES = {
    decide: "Decide",
    specify: "Specify & plan",
    code: "Code",
    verify: "Verify",
    integrate: "Integrate",
    release: "Release & deploy",
    operate: "Operate & feedback",
};
/** How many stages sit before the pull request: `decide` through `verify`. */
export const AI_STAGES_BEFORE_PULL_REQUEST = 4;

/**
 * The three layers underneath the path — not stages, but what runs under every
 * one of them: `thread` keeps spec and code level, `upkeep` keeps the
 * repository healthy on a cadence, `harness` is what every session stands on.
 */
export const AI_LAYERS = ["thread", "upkeep", "harness"];

/** How much a chapter's rating rests on, strongest first. */
export const AI_EVIDENCE = ["recorded", "partial", "fired", "none"];

// A sub-stage is the stage it splits, a slash, and the repository's own word.
const AI_SUBSTAGE = /^([a-z]+)\/([a-z0-9]+(?:-[a-z0-9]+)*)$/;
// The body bullet that says what would move a `trial` or `candidate` rating.
const MOVES_UP_WHEN = /^\s*[-*]\s+\*\*Moves up when\*\*/;
const AI_UNPROVEN_RATINGS = ["trial", "candidate"];

/**
 * Split one `feeds` entry, `"<path>#<anchor>: <label>"`, into the reference
 * the graph resolves and the arrow's label. Returns `null` when the entry has
 * no `: <label>` part — the label is required.
 */
export function parseFeed(entry) {
    if (typeof entry !== "string") return null;
    const at = entry.indexOf(": ");
    if (at <= 0) return null;
    const ref = entry.slice(0, at).trim();
    const label = entry.slice(at + 2).trim();
    if (!ref || !label || /\s/.test(ref)) return null;
    return { ref, label };
}

/**
 * The placement rules of one `.ai` chapter, per devbook-ai.md: `stage` and
 * `layer` from their fixed vocabularies, each `substage` under a stage the
 * chapter lists, every `feeds` entry labelled, `evidence` one word, and the
 * **Moves up when** bullet on exactly the chapters still below `adopted`.
 * `body` is the chapter's own lines, fences already stepped over. Returns
 * issues without the chapter label; the caller prefixes it.
 */
export function aiChapterIssues(meta, type, body) {
    const issues = [];
    const stages = toList(meta.stage);
    for (const word of stages) {
        if (!AI_STAGES.includes(word)) {
            issues.push({
                severity: "error",
                message: `has \`stage\` entry "${word}", expected one of: ${AI_STAGES.join(", ")} — the stages are the delivery path's own, so every repository draws the same path.${LOOP_WORDS.includes(word) ? " A DevOps loop word: migration `035-delivery-path-stages` maps it onto the path." : ""}`,
            });
        }
    }
    for (const word of toList(meta.layer)) {
        if (!AI_LAYERS.includes(word)) {
            issues.push({
                severity: "error",
                message: `has \`layer\` entry "${word}", expected one of: ${AI_LAYERS.join(", ")} — the layers are the three things underneath the path.`,
            });
        }
    }
    if (meta.stage == null && meta.layer == null && type !== "concept") {
        issues.push({
            severity: "warning",
            message: "has no `stage` and no `layer` — a usage says which stages of the path or which layer underneath it works at, or it is off the picture. Only a `concept` applied throughout omits both.",
        });
    }
    for (const entry of toList(meta.substage)) {
        const match = AI_SUBSTAGE.exec(entry);
        if (!match) {
            issues.push({
                severity: "error",
                message: `has \`substage\` entry "${entry}" — a sub-stage is written \`<stage>/<word>\`, the word lowercase kebab-case, e.g. \`verify/review\`.`,
            });
        } else if (!stages.includes(match[1])) {
            issues.push({
                severity: "error",
                message: `has \`substage\` entry "${entry}" under "${match[1]}", which its \`stage\` does not list — a sub-stage splits a stage the chapter sits at.`,
            });
        }
    }
    for (const entry of toList(meta.feeds)) {
        if (!parseFeed(entry)) {
            issues.push({
                severity: "error",
                message: `has \`feeds\` entry "${entry}" — each entry is \`"<path>#<anchor>: <label>"\`, the chapter this one hands work to and the arrow's label, which is required.`,
            });
        }
    }
    if (meta.evidence != null && (Array.isArray(meta.evidence) || !AI_EVIDENCE.includes(meta.evidence))) {
        issues.push({
            severity: "error",
            message: `has \`evidence\` "${toList(meta.evidence).join(", ")}", expected one of: ${AI_EVIDENCE.join(", ")}.`,
        });
    }
    const unproven = AI_UNPROVEN_RATINGS.includes(meta.rating);
    const movesUp = body.some((line) => MOVES_UP_WHEN.test(line));
    if (unproven && !movesUp) {
        issues.push({
            severity: "warning",
            message: `is \`${meta.rating}\` and has no \`- **Moves up when** — …\` bullet — a chapter below \`adopted\` says what would move its rating.`,
        });
    } else if (!unproven && movesUp && meta.rating != null) {
        issues.push({
            severity: "warning",
            message: `is \`${meta.rating}\` and still carries a **Moves up when** bullet — it belongs on a \`trial\` or \`candidate\` chapter only.`,
        });
    }
    return issues;
}

/**
 * The lines of the chapter at `index`, up to the next heading at its level or
 * above, with fenced blocks — its `meta` block among them — stepped over.
 */
function chapterBodyLines(lines, chapters, index) {
    const chapter = chapters[index];
    let end = lines.length;
    for (let i = index + 1; i < chapters.length; i++) {
        if (chapters[i].level <= chapter.level) {
            end = chapters[i].line - 1;
            break;
        }
    }
    const body = [];
    let fence = null;
    for (let i = chapter.line; i < end; i++) {
        const marker = /^\s*(`{3,}|~{3,})/.exec(lines[i]);
        if (fence) {
            if (marker && marker[1][0] === fence[0] && marker[1].length >= fence.length) fence = null;
            continue;
        }
        if (marker) {
            fence = marker[1];
            continue;
        }
        body.push(lines[i]);
    }
    return body;
}

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
    "substage",
    "layer",
    "feeds",
    "evidence",
    "playthrough",
];

// `.ai`'s placement fields: a file-level one would place chapters by implication.
const AI_PLACEMENT_FIELDS = ["stage", "substage", "layer", "feeds", "evidence"];

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
        // A playthrough plays a journey end to end, and a journey walks
        // features: the page a person opens to see one is the feature's.
        playthrough: ["feature", "sub-feature"],
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
    [CHANGES_FOLDER]: {},
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

// ---------------------------------------------------------------------------
// Sync units — what one verify pass covers
// ---------------------------------------------------------------------------

// The root chapters of a sync unit in `.domain`, one per converter kind: an
// aggregate, a domain service, a feature, the two switch chapters, and the two
// actors code represents, a `user` and a `technical` actor. An `organisation`
// is modelled and never authenticated, so it roots no unit.
const UNIT_ROOT_TYPES = ["aggregate", "domain-service", "feature", "feature-flag", "setting", "user", "technical"];

// Pages that hold only chapters some unit on another page owns.
const OWNED_PAGE_BASES = ["requirements", "invariants"];

// The one `.design` document whose `##` chapters are components.
const COMPONENT_FILE = "component-libraries.md";

/**
 * Whether this block roots a sync unit: a `domain/` chapter of one of the
 * root kinds on a context page, an `arc42/building-blocks/<name>.md` file, or
 * a `##` component chapter of `design/component-libraries.md`. `units.mjs`
 * groups every other chapter under the unit it belongs to. `blockLevel` is
 * "file" for the level-1 block and "chapter" for every other heading;
 * `headingLevel` is the chapter's heading depth. Which units a sweep levels,
 * and which way, is not the devbook's: no chapter carries a direction.
 */
export function isUnitRoot(relPath, blockLevel, meta, headingLevel = blockLevel === "file" ? 1 : 2) {
    const folder = folderKindForPath(relPath);
    if (!["domain", "arc42", "design"].includes(folder)) return false;
    const subject = String(relPath).replace(/\\/g, "/").slice(DEVBOOK_PREFIX.length + folder.length + 1);
    const type = resolveType(folder, meta);
    if (folder === "domain") {
        if (blockLevel === "file" || subject.split("/").length !== 2) return false;
        if (OWNED_PAGE_BASES.includes(domainFileName(relPath).base)) return false;
        return UNIT_ROOT_TYPES.includes(type);
    }
    if (folder === "arc42") {
        return blockLevel === "file" && /^building-blocks\/[^/]+\.md$/.test(subject) && indexRole(meta) !== "root";
    }
    return subject === COMPONENT_FILE && blockLevel === "chapter" && headingLevel === 2 && type === null;
}

/** Determine which devbook folder a repo-relative path belongs to. */
export function folderKindForPath(relPath) {
    if (changePathParts(relPath)) return CHANGES_FOLDER;
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
        return splitListEntries(inner)
            .map((entry) => stripQuotes(entry.trim()))
            .filter((entry) => entry.length > 0);
    }
    return stripQuotes(value);
}

// A comma inside a quoted entry is part of it: a demo address carries
// `flags=<key>,<key>`, so `["….demo.html#checkout?flags=a,b"]` is one entry.
function splitListEntries(inner) {
    const entries = [];
    let quote = null;
    let current = "";
    for (const char of inner) {
        if (quote) {
            if (char === quote) quote = null;
        } else if (char === '"' || char === "'") {
            if (current.trim() === "") quote = char;
        } else if (char === ",") {
            entries.push(current);
            current = "";
            continue;
        }
        current += char;
    }
    entries.push(current);
    return entries;
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
    // A `#` line inside a fenced block — a Markdown sample, a diagram — is
    // content, not a chapter.
    let fence = null;

    for (let i = 0; i < lines.length; i++) {
        const marker = /^\s*(`{3,}|~{3,})/.exec(lines[i]);
        if (fence) {
            if (marker && marker[1][0] === fence[0] && marker[1].length >= fence.length) fence = null;
            continue;
        }
        if (marker) {
            fence = marker[1];
            continue;
        }
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

        // A `shot:` or `scenario:` target is a screenshot point or a reference
        // to one, resolved by the tool that runs the journey — not a diagram.
        // A `c4:` target is a view of the C4 model, and counts like any image.
        for (const image of line.matchAll(/!\[[^\]]*\]\(([^)]*)\)/g)) {
            if (!/^\s*<?(?:shot|scenario):/i.test(image[1])) diagrams++;
        }

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
    // A journey is a `##` chapter of a scenarios file, and in no folder but
    // `.domain`. Which file and which level is validateDocument's to say,
    // since only it has the path and the heading.
    if (declared === SCENARIO_TYPE) {
        if (folder !== "domain") {
            issues.push({
                severity: "error",
                message: `has type "${SCENARIO_TYPE}" in .${folder} — a journey is a \`##\` of a scenarios file in .devbook/domain/, in the bounded context of the requirements it proves.`,
            });
        } else if (blockLevel === "file") {
            issues.push({
                severity: "error",
                message: `has type "${SCENARIO_TYPE}" on its file-level block — a journey page is the shape before contract 32; a journey is now a \`##\` of the context's scenarios.md. Run the \`032-scenarios-md\` migration.`,
            });
        }
        return issues;
    }
    if (folder === "domain" && blockLevel === "file" && declared === RETIRED_FLOW_TYPE) {
        issues.push({
            severity: "error",
            message: `has type "${RETIRED_FLOW_TYPE}", a flow page — retired by contract 37: each diagram opens the chapter of what it draws. Run the \`037-flows-in-their-chapters\` migration.`,
        });
        return issues;
    }
    if (allowed.length) {
        if (declared === null) {
            if (OPTIONAL_TYPE_FOLDERS.includes(folder)) return issues;
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
 * A scenario belongs to the rule above it by position: the count stops at the first heading back at or above the
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
 * Whether the heading at `index` is a `Scenario:` one level under a
 * `requirement` chapter — a case of that rule, found by its text, and so a
 * section rather than a chapter that owes a block.
 */
function isScenarioOf(chapters, index, folder) {
    const heading = chapters[index];
    if (!SCENARIO_HEADING.test(heading.text)) return false;
    for (let i = index - 1; i >= 0; i--) {
        if (chapters[i].level < heading.level) {
            return chapters[i].level === heading.level - 1 && resolveType(folder, chapters[i].meta) === "requirement";
        }
    }
    return false;
}

/**
 * Coverage warnings for one behaviour chapter — a `requirement` or an
 * `invariant` in `.domain`, a `requirement` in `.design`. Every other type
 * returns nothing.
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
export function behaviourIssues(type, meta, scenarios = 0, folder = "domain") {
    const expected = BEHAVIOUR_TEST_LEVELS[folder]?.[type];
    if (!expected) return [];
    const issues = [];

    // Only a requirement is held to its scenarios. An invariant's claim is
    // already the case and its `unit` test names it, so one with none is
    // complete — and one an older chapter still carries is left alone. A
    // promise that is withdrawn is removed by a change, not kept and exempted.
    if (type === "requirement" && scenarios === 0) {
        issues.push({
            severity: "warning",
            message: `is a \`${type}\` chapter with no \`#### Scenario:\` under it — a promise with no case that exercises it is one nobody can tell has been broken, and a brief can derive no acceptance check from it.`,
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
/**
 * Every Markdown link target in a document's prose, with its line: inline
 * links and images, `[label](target)`, and reference definitions,
 * `[label]: target`.
 *
 * Fenced blocks are skipped whole — the `meta` block, a
 * diagram, a code sample — and so are code spans, so a chapter quoting link
 * syntax is not read as linking. The target comes back as written, `<…>`
 * brackets and a trailing title removed; resolving it is the caller's job.
 */
export function proseLinks(markdown) {
    const links = [];
    let fence = null;
    markdown.split(/\r?\n/).forEach((line, index) => {
        const marker = /^\s*(`{3,}|~{3,})/.exec(line);
        if (fence) {
            if (marker && marker[1][0] === fence[0] && marker[1].length >= fence.length) fence = null;
            return;
        }
        if (marker) {
            fence = marker[1];
            return;
        }
        const prose = line.replace(/(`+)[^`]*?\1/g, (span) => " ".repeat(span.length));
        const definition = /^\s{0,3}\[[^\]]+\]:\s*(<[^>]*>|\S+)/.exec(prose);
        if (definition) links.push({ line: index + 1, target: definition[1].replace(/^<|>$/g, "") });
        for (const match of prose.matchAll(/\]\(\s*(<[^>]*>|[^)\s]+)(?:\s+(?:"[^"]*"|'[^']*'))?\s*\)/g)) {
            links.push({ line: index + 1, target: match[1].replace(/^<|>$/g, "") });
        }
    });
    return links;
}

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
                    folder === "ai" && AI_PLACEMENT_FIELDS.includes(field)
                        ? `has \`${field}\` on the file-level block — a file groups chapters and places none of them on the path; each chapter says its own. See devbook-ai.md.`
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
 * Lint the C4 model's fields on one block, per `devbook-chapter-metadata.md`:
 * `c4` and `c4-touches` hold bare workspace identifiers and never share one;
 * `code` sits on a building block's top-level block and lists repository
 * folders; `severity` sits on a debt record's top-level block, from a closed
 * set, and is reported there when missing. Whether an identifier names an
 * element, and whether a folder exists, is the graph build's to say.
 */
export function c4FieldIssues(relPath, blockLevel, meta, fileMeta) {
    const issues = [];
    if (!meta) return issues;
    const folder = folderKindForPath(relPath);
    const normalized = String(relPath).replace(/\\/g, "/");
    const say = (severity, message) => issues.push({ severity, message });

    if (folder === "arc42" || folder === "domain") {
        for (const field of C4_FIELDS) {
            for (const entry of toList(meta[field])) {
                if (!C4_IDENTIFIER.test(entry)) {
                    say("error", `has \`${field}\` entry "${entry}", which is not a workspace identifier — name an element of .devbook/arc42/workspace.dsl as the DSL spells it, \`system.container.component\`, never a path or an anchor.`);
                }
            }
        }
        const touched = new Set(toList(meta["c4-touches"]).map((entry) => entry.toLowerCase()));
        for (const entry of toList(meta.c4)) {
            if (touched.has(entry.toLowerCase())) {
                say("error", `names \`${entry}\` in both \`c4\` and \`c4-touches\` — an element is what the chapter is about or one its consequences touch, never both.`);
            }
        }
    }

    // `code` and `severity` are arc42/ fields; anywhere else the sweep for
    // unrecognized fields reports them, as it did before they existed.
    if (folder !== "arc42") return issues;

    if (meta.code != null) {
        const onBlock = blockLevel === "file" && BUILDING_BLOCK_FILE.test(normalized) && fileMeta?.index !== "root";
        if (!onBlock) {
            say("error", "has `code`, which sits on an `arc42/building-blocks/<slug>.md` file's top-level block only — the block names the folders that hold its code. See devbook-chapter-metadata.md.");
        }
        for (const entry of toList(meta.code)) {
            if (!isRepoFolder(entry)) {
                say("error", `has \`code\` entry "${entry}", which is not a repository-relative folder — write it from the repository root, with no leading \`/\`, no \`.\` or \`..\`, and no anchor.`);
            }
        }
    }

    const debt = isDebtRecord(relPath, blockLevel, fileMeta);
    if (meta.severity != null) {
        if (!debt) {
            say("error", "has `severity`, which sits on an `arc42/tdr/` debt record's top-level block only — the `tdr/` index carries none. See devbook-arc42.md.");
        } else if (Array.isArray(meta.severity) || !DEBT_SEVERITIES.includes(meta.severity)) {
            say("error", `has \`severity\` "${toList(meta.severity).join(", ")}", expected one of: ${DEBT_SEVERITIES.join(", ")}.`);
        }
    } else if (debt) {
        // Always stated, per the rule; a warning, so a debt set written before
        // the field existed keeps validating until each record is ranked.
        say("warning", `is a debt record without \`severity\` — state what the debt costs while it stands: ${DEBT_SEVERITIES.join(", ")}. See devbook-arc42.md.`);
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
export function removedFieldIssues(meta, folder = null) {
    if (!meta) return [];
    return Object.keys(meta)
        .filter((key) => key in REMOVED_FIELDS)
        .map((key) => ({
            severity: "error",
            message: `has \`${key}\`, which is no longer part of the metadata schema — ${
                key === "status" && ratingsFor(folder).length ? RENAMED_RATING : REMOVED_FIELDS[key]
            }`,
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

// The structural documents: files whose `##` sections are sections, not
// chapters, and carry no block of their own — the file-level block is the only
// metadata they need. Mirrors the folder instruction files that say so; change
// one and change the other in the same edit. `context-map.md` is on the list
// although a `bounded-context` section there takes a block: that section is
// optional, so a section without one is structural, and one with one is still
// validated like any chapter.
const STRUCTURAL_ROOT_FILES = {
    domain: "context-map.md",
    tech: "technology-graph.md",
    ai: "adoption-map.md",
};
const STRUCTURAL_DOMAIN_BASES = ["model", "dependencies"];

/**
 * Whether this document's headings are sections rather than chapters, so a
 * heading without a `meta` block is expected, not a missing block.
 */
export function isStructuralDocument(relPath) {
    const kind = folderKindForPath(relPath);
    if (!kind) return false;
    // A proposal's `## Why`, `## Scope`, and the rest are sections of one
    // document; its file-level block is the change's only block.
    if (kind === CHANGES_FOLDER) return changePathParts(relPath).part === "proposal";
    const subject = String(relPath).replace(/\\/g, "/").slice(DEVBOOK_PREFIX.length + kind.length + 1);
    if (subject === STRUCTURAL_ROOT_FILES[kind]) return true;
    if (kind !== "domain" || subject.split("/").length !== 2) return false;
    const { base, legacy } = domainFileName(relPath);
    // An additional page is structural too: the rule names it beside model.md,
    // and it is whatever file the convention does not prescribe.
    return STRUCTURAL_DOMAIN_BASES.includes(base) || (!legacy && !TYPE_BY_FOLDER.domain.file.includes(base));
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

    // A delta is checked against the chapter it targets, which only the delta
    // merge can resolve; here, only its own header is in reach.
    const changePart = changePathParts(relPath);
    if (changePart?.part === "delta") return deltaHeaderIssues(relPath, markdown);
    if (changePart && changePart.part !== "proposal") {
        issues.push({
            severity: "info",
            message: `${relPath} is a change's own working file — only \`proposal.md\` and \`${DELTA_FOLDER}/\` hold devbook chapters, so no metadata rules apply.`,
        });
        return issues;
    }

    const { fileTitle, fileMeta, chapters } = parseDocument(markdown);
    const documentLines = markdown.split(/\r?\n/);
    for (const issue of escapeSequenceIssues(markdown)) {
        // graph.mjs prefixes every issue with the path; doing it here too printed it twice.
        issues.push({ severity: issue.severity, message: issue.message });
    }
    // The annotation fence retired in contract 38; one left behind is reported
    // here, in the same pass, so a repository cannot forget to look.
    for (const issue of retiredFenceIssues(markdown)) issues.push(issue);
    for (const issue of historyHeaderIssues(relPath, markdown, fileMeta)) issues.push(issue);
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

    // A scenarios file holds one journey per `##`, typed `scenario`; the
    // `###` headings under a journey are its parts — sections, never
    // chapters, so they owe no block and stay addressable by anchor. Every
    // journey and part anchor is unique in the file, because `Proved by:` and
    // a demo's `part` name a part by the file and its anchor alone.
    const scenariosFile = kind === "domain" && isScenariosFile(relPath);
    if (scenariosFile) {
        for (const issue of scenarioStepIssues(markdown)) issues.push(issue);
        const anchors = new Map();
        for (const chapter of chapters) {
            if (chapter.level === 1) continue;
            const label = `${"#".repeat(chapter.level)} ${chapter.text} (line ${chapter.line})`;
            if (chapter.level === 2 && (!chapter.meta || resolveType(kind, chapter.meta) !== SCENARIO_TYPE)) {
                issues.push({ severity: "error", message: `${label} is a \`##\` of a scenarios file without \`type: scenario\` — every \`##\` here is one journey.` });
            }
            if (chapter.level > 2 && chapter.meta) {
                issues.push({ severity: "error", message: `${label} carries a \`meta\` block — a journey's \`###\` parts and the headings under them are sections, and only the journey has a block.` });
            }
            if (chapter.level <= 3) {
                if (anchors.has(chapter.slug)) {
                    issues.push({ severity: "error", message: `${label} has the anchor "#${chapter.slug}" of line ${anchors.get(chapter.slug)} — every journey and part heading is unique in its file, so an address names one.` });
                } else {
                    anchors.set(chapter.slug, chapter.line);
                }
            }
        }
    } else {
        for (const chapter of chapters) {
            if (chapter.level === 1 || !chapter.meta || resolveType(kind, chapter.meta) !== SCENARIO_TYPE || kind !== "domain") continue;
            issues.push({
                severity: "error",
                message: `${"#".repeat(chapter.level)} ${chapter.text} (line ${chapter.line}) has type "${SCENARIO_TYPE}" outside a scenarios file — a journey is a \`##\` of \`scenarios.md\`, \`scenarios.<name>.md\`, or \`scenarios.<name>.<page>.md\` in the bounded context of the requirements it proves.`,
            });
        }
    }

    // A `#### Scenario:` case proved by a journey holds the one pointer
    // and nothing else; the journey is where its Given/When/Then are written.
    for (const scenarioCase of provedByCases(relPath, markdown)) {
        const label = `#### ${scenarioCase.heading} (line ${scenarioCase.line})`;
        if (scenarioCase.pointers.length > 1) {
            issues.push({
                severity: "error",
                message: `${label} holds ${scenarioCase.pointers.length} \`Proved by:\` lines — a case points at one part of one journey; write another \`#### Scenario:\` for another part.`,
            });
        }
        if (scenarioCase.pointers.length && scenarioCase.gwt) {
            issues.push({
                severity: "error",
                message: `${label} holds a \`Proved by:\` line and its own Given/When/Then — the pointer replaces them, so the steps are written once, in the journey.`,
            });
        }
        for (const pointer of scenarioCase.pointers) {
            if (!/^[^#\s]+\.md#[^#\s]+$/.test(pointer.target)) {
                issues.push({
                    severity: "error",
                    message: `${label} has \`Proved by: ${pointer.target}\` — the line names one part of a journey, as \`<scenarios file>.md#<part>\`.`,
                });
            }
        }
    }

    const structural = isStructuralDocument(relPath);
    for (const [index, chapter] of chapters.entries()) {
        const label = `${"#".repeat(chapter.level)} ${chapter.text} (line ${chapter.line})`;
        if (!chapter.meta) {
            // Level-1 heading already reported above as the file-level block;
            // a structural document's headings are sections by rule, and so
            // is a `#### Scenario:` directly under a `requirement` and every
            // heading under a journey. A `##` of a scenarios file without one
            // is reported above.
            const section = structural || isScenarioOf(chapters, index, kind) || (scenariosFile && chapter.level > 1);
            if (chapter.level > 1 && !section) {
                issues.push({
                    severity: "warning",
                    message: `${label} has no \`meta\` block. Add one if this heading is an addressable chapter for this folder.`,
                });
            }
            continue;
        }

        const blockLevel = chapter.level === 1 ? "file" : "chapter";
        const journeyBlock = kind === "domain" && blockLevel === "chapter" && resolveType(kind, chapter.meta) === SCENARIO_TYPE;

        // A `tech/` or `ai/` block states its rating, from the adoption
        // ladder. No other folder rates anything, so `rating` there is reported
        // as unrecognized below. A block still spelling it `status` is reported
        // once, as renamed, by `removedFieldIssues`.
        const ratings = ratingsFor(kind);
        if (ratings.length) {
            const rating = chapter.meta[RATING_FIELD];
            if (rating == null || (Array.isArray(rating) && rating.length === 0)) {
                if (!("status" in chapter.meta)) {
                    issues.push({
                        severity: "error",
                        message: `${label} is missing required \`rating\`, one of: ${ratings.join(", ")}.`,
                    });
                }
            } else if (Array.isArray(rating) || !ratings.includes(rating)) {
                issues.push({
                    severity: "error",
                    message: `${label} has \`rating\` "${toList(rating).join(", ")}", expected one of: ${ratings.join(", ")}.`,
                });
            }
        }

        // `type` records what kind of thing this chapter or file is, in the
        // vocabulary its folder defines. Folders that define no vocabulary
        // (`.arc42`) omit the field entirely; `.design` types only its
        // requirement chapters.
        for (const issue of typeIssues(kind, blockLevel, chapter.meta, fileBase)) {
            issues.push({ severity: issue.severity, message: `${label} ${issue.message}` });
        }

        // Which of the folder's own fields this particular block may carry.
        for (const issue of fieldScopeIssues(kind, blockLevel, chapter.meta)) {
            issues.push({ severity: issue.severity, message: `${label} ${issue.message}` });
        }

        // The C4 model's fields: their shape and where each may sit.
        for (const issue of c4FieldIssues(relPath, blockLevel, chapter.meta, fileMeta)) {
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

        // A proposal names its change's category once, from a closed set,
        // because the category is what picks the flow that applies a step.
        if (kind === CHANGES_FOLDER && blockLevel === "file") {
            const category = chapter.meta.category;
            if (category == null) {
                issues.push({
                    severity: "error",
                    message: `${label} is missing required \`category\`. Expected one of: ${CHANGE_CATEGORIES.join(", ")}.`,
                });
            } else if (!CHANGE_CATEGORIES.includes(category)) {
                issues.push({
                    severity: "error",
                    message: `${label} has \`category\` "${Array.isArray(category) ? category.join(", ") : category}", expected one of: ${CHANGE_CATEGORIES.join(", ")}.`,
                });
            }
        }

        // A `.ai` chapter says where it sits on the delivery path with `stage`
        // or `layer`, and the file it is in says nothing about that — so the
        // fields are on every chapter, checked against the fixed vocabularies
        // rather than the repository's own words. The one chapter that may omit
        // both is a `concept` that applies throughout; any other chapter
        // without either is off the picture, which is reported as a warning
        // rather than an error so an existing folder keeps validating while its
        // chapters are placed. The same holds for a missing **Moves up when**.
        if (kind === "ai" && blockLevel === "chapter") {
            const body = chapterBodyLines(documentLines, chapters, index);
            for (const issue of aiChapterIssues(chapter.meta, resolveType(kind, chapter.meta), body)) {
                issues.push({ severity: issue.severity, message: `${label} ${issue.message}` });
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

        // `tests` names the test cases that assert what this chapter claims,
        // as `<level>:<runner>:<selector>` identifiers a runner can resolve.
        if (journeyBlock && chapter.meta.tests != null) {
            issues.push({
                severity: "error",
                message: `${label} has \`tests\` on a journey — the journey is the test: its spec is derived from it, and a requirement's \`e2e\` entry is derived from the \`Proved by:\` line that points at a part.`,
            });
        } else {
            for (const issue of testIssues(chapter.meta)) {
                issues.push({ severity: issue.severity, message: `${label} ${issue.message}` });
            }
        }

        // A rule chapter is one rule plus the cases that prove it, and the
        // level of proof follows the file it sits in. Both are reported as
        // coverage warnings — see `behaviourIssues`. Whether the chapter's
        // `related` reaches the prose half it belongs to is the graph build's
        // to say, since only it can resolve across files.
        if (blockLevel === "chapter") {
            for (const issue of behaviourIssues(
                resolveType(kind, chapter.meta),
                chapter.meta,
                scenarioCount(chapters, index),
                kind
            )) {
                issues.push({ severity: issue.severity, message: `${label} ${issue.message}` });
            }
        }

        for (const issue of removedFieldIssues(chapter.meta, kind)) {
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
            if (key in REMOVED_FIELDS) continue; // already reported above
            if (key === RATING_FIELD && ratings.length) continue; // reported above
            if (journeyBlock && key === "tests") continue; // reported above
            if (SCENARIO_SETUP_FIELDS.includes(key)) {
                if (!journeyBlock) {
                    issues.push({
                        severity: "error",
                        message: `${label} has \`${key}\`, a setup field — where a journey starts and in which configuration it runs is said on the journey's own block only.`,
                    });
                    continue;
                }
                const isEmpty = value === null || (Array.isArray(value) && value.length === 0);
                if (isEmpty) {
                    issues.push({
                        severity: "warning",
                        message: `${label} sets \`${key}\` to an empty/null value — omit the field instead per the omit-when-empty rule.`,
                    });
                    continue;
                }
                for (const issue of setupFieldIssues(key, value)) {
                    issues.push({ severity: issue.severity, message: `${label} ${issue.message}` });
                }
                continue;
            }
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
// Deltas — a change's chapters, at the path of the file each one changes
// ---------------------------------------------------------------------------

/** The three things a delta does to the chapters it names. */
export const DELTA_KINDS = ["added", "modified", "removed"];

/** The three section headings under a targeted chapter, and what each does. */
export const DELTA_SECTIONS = ["ADDED", "MODIFIED", "REMOVED"];

/**
 * A delta's own `meta` block — the one that opens the file, above any heading
 * — and the line after its closing fence. `null` when the file does not open
 * with one.
 */
export function parseDeltaHeader(markdown) {
    const lines = markdown.split(/\r?\n/);
    let i = 0;
    while (i < lines.length && lines[i].trim() === "") i++;
    if (i >= lines.length || !/^```meta\s*$/.test(lines[i].trim())) return null;
    const body = [];
    let k = i + 1;
    while (k < lines.length && lines[k].trim() !== "```") body.push(lines[k++]);
    if (k >= lines.length) return null;
    return { meta: parseMetaBody(body.join("\n")), end: k + 1 };
}

/**
 * The rules a delta's header answers on its own: it names its change, which is
 * the folder it sits in, and one delta kind, and it carries nothing else — a
 * delta has no status of its own and inherits everything through `change`.
 */
export function deltaHeaderIssues(relPath, markdown) {
    const issues = [];
    const where = changePathParts(relPath);
    const header = parseDeltaHeader(markdown);
    if (!header) {
        issues.push({
            severity: "error",
            message: `opens with no \`meta\` block — a delta starts with one carrying \`change: ${where?.name ?? "<name>"}\` and \`delta\` (${DELTA_KINDS.join(", ")}).`,
        });
        return issues;
    }
    const { change, delta } = header.meta;
    if (change == null) {
        issues.push({ severity: "error", message: `is missing required \`change\` in its opening \`meta\` block.` });
    } else if (Array.isArray(change) || !CHANGE_NAME_PATTERN.test(change)) {
        issues.push({
            severity: "error",
            message: `has \`change\` "${toList(change).join(", ")}" — it names one change, as its folder under ${CHANGES_ROOT}/ spells it: lowercase kebab-case.`,
        });
    } else if (where && change !== where.name) {
        issues.push({
            severity: "error",
            message: `has \`change\` "${change}" but sits in the change folder \`${where.name}\` — a delta names the change it belongs to.`,
        });
    }
    if (delta == null) {
        issues.push({
            severity: "error",
            message: `is missing required \`delta\` in its opening \`meta\` block. Expected one of: ${DELTA_KINDS.join(", ")}.`,
        });
    } else if (!DELTA_KINDS.includes(delta)) {
        issues.push({ severity: "error", message: `has \`delta\` "${delta}", expected one of: ${DELTA_KINDS.join(", ")}.` });
    }
    for (const key of Object.keys(header.meta)) {
        if (key === "change" || key === "delta") continue;
        issues.push({
            severity: "error",
            message: `carries \`${key}\` in its opening \`meta\` block, which holds only \`change\` and \`delta\` — a delta has no status of its own and inherits through its change.`,
        });
    }
    return issues;
}

const FENCE_PATTERN = /^(\s*)(`{3,}|~{3,})\s*([^\s`~]*)\s*$/;

/**
 * The fingerprint of one block's content, per `devbook.chapter.fingerprint@1`
 * — the value a host records a decision on the content at.
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
 * fingerprints. The `annotation` fences go because a review note is not
 * content: `devbook.chapter.fingerprint@1` stated the exclusion before the
 * fence retired in contract 38, and keeping it keeps every recorded value. Whitespace goes because a
 * reflowed paragraph reads identically, and a rung that came off every time
 * someone rewrapped a line would be taken off for good.
 *
 * Heading *text* is in, the `#` markers are not: renaming a chapter changes
 * what it claims and should lapse the approval, while promoting one changes
 * its address and not a word of what was read.
 *
 * `line` is the block's 1-based heading line, as `parseDocument` reports it.
 *
 * The text hashed is `chapterText`, so a host recomputing the value per
 * `resources/chapter-fingerprint.md` can compare the text before the hash.
 */
export function chapterHash(markdown, line = 1) {
    return digest(chapterText(markdown, line));
}

/** The normalised text `chapterHash` digests. */
export function chapterText(markdown, line = 1) {
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

    const heading = /^#{1,6}\s+(.*)$/.exec(lines[start] ?? "");
    return [heading ? heading[1] : "", hashedText(lines, start + 1, end, ["meta", "annotation"])]
        .filter((entry) => entry !== "")
        .join("\n");
}

const digest = (text) => `sha256:${createHash("sha256").update(text, "utf8").digest("hex").slice(0, 8)}`;

/** Whether a path names a click demo: `demo.html`, or any `*.demo.html`. */
export function isDemoPath(relPath) {
    const base = String(relPath).replace(/\\/g, "/").split("/").pop();
    return base === "demo.html" || base.endsWith(".demo.html");
}

/**
 * The page a demo is named for: `context.md` for `demo.html`, `<page>.md` for
 * `<page>.demo.html`, beside it. Null for a path that is no demo. Any name a
 * demo can carry is page-style, so a free-named demo is told apart by whether
 * that page exists, not by its name.
 */
export function demoPagePath(relPath) {
    const normalized = String(relPath).replace(/\\/g, "/");
    if (!isDemoPath(normalized)) return null;
    const slash = normalized.lastIndexOf("/");
    const dir = slash === -1 ? "" : normalized.slice(0, slash + 1);
    const base = normalized.slice(slash + 1);
    return base === "demo.html" ? `${dir}context.md` : `${dir}${base.slice(0, -".demo.html".length)}.md`;
}

/**
 * Whether a demo's name is page-style: `demo.html`, or `<page>.demo.html` whose
 * first segment is a page `domain/` prescribes — `features.demo.html`,
 * `features.checkout.demo.html`. Such a demo exists only beside its page. Any
 * other name belongs to an additional page beside it when one exists, and
 * otherwise to the chapters whose `demo` field names it.
 */
export function isPageNamedDemo(relPath) {
    if (!isDemoPath(relPath)) return false;
    const base = String(relPath).replace(/\\/g, "/").split("/").pop();
    return base === "demo.html" || TYPE_BY_FOLDER.domain.file.includes(base.split(".")[0]);
}

/** The demo named for a page — the inverse of `demoPagePath`. */
export function pageDemoPath(relPath) {
    const normalized = String(relPath).replace(/\\/g, "/");
    if (!normalized.endsWith(".md")) return null;
    const slash = normalized.lastIndexOf("/");
    const dir = slash === -1 ? "" : normalized.slice(0, slash + 1);
    const base = normalized.slice(slash + 1, -".md".length);
    return base === "context" ? `${dir}demo.html` : `${dir}${base}.demo.html`;
}

/**
 * A demo's text as a fingerprint reads it: line endings normalised and every
 * managed region — `template:begin` through `template:end`, both markers
 * included — dropped. The region is the template's, checked against it by
 * `demo-template.mjs`, so `--refresh` rewriting it lifts no approval; what a
 * page's approval covers is the demo's own screens, model, and question.
 */
export function demoFingerprintText(text) {
    return String(text)
        .replace(/\r\n?/g, "\n")
        .replace(/<!--\s*template:begin\b[^>]*?-->[\s\S]*?(?:<!--\s*template:end\b[^>]*?-->|$)/gi, "");
}

/**
 * The fingerprint a host records a decision on a chapter at: `chapterHash`,
 * with every demo the block belongs to folded in, so editing a demo changes it
 * exactly as editing the prose does. A demo's
 * managed region is left out, per `demoFingerprintText`.
 *
 * A block's demos are the ones its `demo` field — or the field of any chapter
 * nested in it — names by path, and for the file block also the demo named for
 * the page: `<page>.demo.html` for `<page>.md`, `demo.html` for `context.md`.
 * `demoText(path)` returns a demo's text, or null when it does not exist. A
 * block with no demo hashes exactly as `chapterHash` does, so no approval
 * recorded before demos existed changes value.
 */
export function chapterFingerprint(relPath, markdown, line = 1, demoText = null) {
    return digest(chapterFingerprintText(relPath, markdown, line, demoText));
}

/**
 * The text `chapterFingerprint` digests: `chapterText` for a block with no
 * demo, else the block's `chapterHash` followed by each demo it folds in.
 */
export function chapterFingerprintText(relPath, markdown, line = 1, demoText = null) {
    if (!demoText) return chapterText(markdown, line);
    const { chapters } = parseDocument(markdown);
    const index = chapters.findIndex((entry) => entry.line === line);
    const level = index === -1 ? 1 : chapters[index].level;
    const paths = new Set();
    if (level === 1) {
        const own = pageDemoPath(relPath);
        if (own) paths.add(own);
    }
    for (let i = Math.max(index, 0); i < chapters.length; i++) {
        if (i > index && index !== -1 && chapters[i].level <= level) break;
        for (const ref of [...toList(chapters[i].meta?.demo), ...toList(chapters[i].meta?.playthrough)]) {
            const target = String(ref).split("#")[0];
            if (isDemoPath(target)) paths.add(target);
        }
    }
    const demos = [];
    for (const demoPath of [...paths].sort()) {
        const text = demoText(demoPath);
        if (text != null) demos.push(`${demoPath}\n${demoFingerprintText(text)}`);
    }
    return demos.length ? [chapterHash(markdown, line), ...demos].join("\n") : chapterText(markdown, line);
}

/** Lines `from` to `end`, minus the fences labelled in `dropped`, whitespace-normalised. */
function hashedText(lines, from, end, dropped) {
    const kept = [];
    for (let i = from; i < end; i++) {
        const fence = FENCE_PATTERN.exec(lines[i]);
        if (fence) {
            const marker = fence[2];
            const label = fence[3].toLowerCase();
            const closer = new RegExp(`^\\s*\\${marker[0]}{${marker.length},}\\s*$`);
            let k = i + 1;
            while (k < lines.length && !closer.test(lines[k])) k++;
            if (dropped.includes(label)) {
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
    return kept
        .map((entry) => entry.replace(/\s+/g, " ").trim())
        .filter((entry) => entry !== "")
        .join("\n");
}

/**
 * The content fingerprint of a whole change — what a host records the
 * change's approval and acceptance at, the way `chapterHash` covers a chapter.
 * A change is decided as one review, so the fingerprint is one value over the
 * proposal and every delta: editing any of them moves it, and a decision
 * recorded at the old value no longer stands.
 *
 * The proposal is hashed as its file block is. A delta keeps its `meta`
 * fences, because in a delta they are content — the header says what kind of
 * change it is, and a `MODIFIED` block is the fields it sets — and drops only
 * its `annotation` fences, as fingerprint@1 states. Each delta is keyed by the devbook file it targets,
 * so moving one to another target is an edit. A demo delta is read as a page
 * fingerprint reads a demo, its managed region left out. `deltas` is a list of
 * `{ target, markdown }`, in any order.
 */
export function changeHash(proposalMarkdown, deltas) {
    return digest(changeText(proposalMarkdown, deltas));
}

/** The text `changeHash` digests. */
export function changeText(proposalMarkdown, deltas) {
    const parts = [chapterHash(proposalMarkdown, parseDocument(proposalMarkdown).chapters.find((c) => c.level === 1)?.line ?? 1)];
    for (const delta of [...deltas].sort((a, b) => (a.target < b.target ? -1 : a.target > b.target ? 1 : 0))) {
        if (isDemoPath(delta.target)) {
            parts.push(`${delta.target}\n${demoFingerprintText(delta.markdown)}`);
            continue;
        }
        const lines = delta.markdown.split(/\r?\n/);
        parts.push(`${delta.target}\n${hashedText(lines, 0, lines.length, ["annotation"])}`);
    }
    return parts.join("\n");
}

/**
 * The `annotation` fences left in a document, each reported as retired.
 *
 * A review note was a fence in the chapter until contract 38. It is a comment
 * thread in the host that keeps the chapter's workflow state now, read and
 * posted through `devbook.decision@1`, and `038-workflow-state-leaves-the-chapter`
 * deletes every fence, open or resolved. Only a fence at the top level is one:
 * an `annotation` opener inside another fence is that fence's content.
 */
export function retiredFenceIssues(markdown) {
    const lines = markdown.split(/\r?\n/);
    const issues = [];
    for (let i = 0; i < lines.length; i++) {
        const fence = FENCE_PATTERN.exec(lines[i]);
        if (!fence) continue;
        const marker = fence[2];
        const closer = new RegExp(`^\\s*\\${marker[0]}{${marker.length},}\\s*$`);
        let k = i + 1;
        while (k < lines.length && !closer.test(lines[k])) k++;
        // An unclosed one is no note either, and the migration leaves it.
        if (k < lines.length && fence[3].toLowerCase() === "annotation") {
            issues.push({
                severity: "error",
                message:
                    `has an \`annotation\` fence at line ${i + 1} — ` +
                    movedToTooling("a review note is a comment thread in the host that keeps the chapter's workflow state, through `devbook.decision@1`"),
            });
        }
        i = k;
    }
    return issues;
}
