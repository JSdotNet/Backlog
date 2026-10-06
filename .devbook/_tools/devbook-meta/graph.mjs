// graph.mjs — derives the cross-folder reference graph from the `meta` blocks
// embedded in .devbook/arc42/, domain/, tech/, design/, and ai/.
//
// Markdown stays canonical; this produces the *derived* index. Output shape is
// Cytoscape.js `elements` JSON, which most graph libraries consume natively or
// map from trivially.
//
// Consumed by the `build.mjs` CLI and by the devbook-graph canvas, so
// the CLI output and the live view can never disagree.

import { readFile, readdir, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
    parseDocument,
    domainFileName,
    folderKindForPath,
    resolveType,
    resolveStatus,
    validateDocument,
    slugify,
    documentNumber,
    isExtensionField,
    parseAnnotations,
    proseLinks,
    resolveAnnotation,
    parseDeltaHeader,
    changePathParts,
    changeHash,
    syncLevel,
    syncSources,
    SYNC_DIRECTIONS,
    DEVBOOK_FOLDER_NAMES,
    DEVBOOK_ROOT,
    CHANGES_ROOT,
} from "./metadata.mjs";
import { loadStatusLadder } from "./statuses.mjs";
import { changeDecisionIssues, changeFiles, checkDelta, readChange } from "./delta.mjs";
import { demoProblems, demoReader, requirementScenarios } from "./demo.mjs";

/**
 * Every devbook folder this convention recognizes, as the repository path it
 * lives at: `.devbook/arc42`, `.devbook/domain`, and so on. A repository adopts
 * any subset. A scope is one of these, or the repository rollup.
 */
export const DEVBOOK_FOLDERS = DEVBOOK_FOLDER_NAMES.map((name) => `${DEVBOOK_ROOT}/${name}`);

export { DEVBOOK_FOLDER_NAMES, DEVBOOK_ROOT, CHANGES_ROOT };
// The repo-visible contract: one number covering the metadata schema a
// repository authors and the derived artifacts a consumer reads. It moves only
// when something repo-visible changes shape, which is why a plugin release
// usually leaves it alone — and why the migration ledger keys off it.
//
// Version 11 adds `context.md` to `domain/` as a bounded context's root
// document, with `feature-flag` and `setting` chapters, and changes the shape
// of a feature's `feature-flag` field from a bare application key to a
// reference to one of those chapters — so it produces a `gated-by` edge now,
// beside the new `setting` field and its `configured-by` edge. A repository whose contexts have no
// `context.md`, or whose features still carry bare keys, stops validating, so
// `migrations/011-context-md/` writes the file and rewrites the keys.
// Version 10 removes `naming` from the `domain/` file types and with it the
// optional `naming.md`: a term that is a chapter carries its `aliases` on that
// chapter, and the rest are `term` chapters under `domain.md`'s
// `## Ubiquitous Language` grouping. A repository still carrying the file
// stops validating, so `migrations/010-terms-live-in-domain-md/` folds it in.
// Versions 8 and 9 were shipped by pre-1.0.0 migrations that no longer exist.
// Version 7 is additive over 6: the nested `.devbook/` layout is recognized
// alongside the flat dot-folders, and `bounded-context` joins the `.domain`
// chapter types so a context-map section can be addressed. Nothing that
// validated under 6 stops validating, so there is no migration folder.
// Version 6 removes `.backlog` from the recognized folders and with it the
// `implements` reference field, and adds two things on top of 5: the shared
// `approved` rung with its `approved-by`/`approved-at` record, and the opaque
// `ext` namespace an L1 plugin persists its own state in. Only the removal was
// breaking; its migration predates 1.0.0 and no longer ships. Version 5 was
// additive over 4: `status` may now be resolved from the folder's resting value
// rather than read off the block, and both artifacts gained an optional
// `statusDeclared: false` marking the entries where that happened. Version 4
// was additive over 3, adding the `tests` field carrying the
// `<level>:<runner>:<selector>` test identifiers a chapter or file declares.
// Version 13 adds three things and takes one away. It adds the optional
// `approved-hash` fingerprint, the `accepted` rung with
// `accepted-by`/`accepted-at`/`accepted-hash` above `approved`, and a
// `.domain` bounded context's freedom to carry a page the convention does not
// name, whose file-level `type` is its own filename. It removes both decision
// rungs, and their six record fields, from every folder but `.domain`: the
// question they answer is asked of the model, and a rung on a chapter that
// rates a technology put two unrelated statements in one field. That removal
// is breaking — a chapter outside `.domain` holding `status: approved` stops
// validating — so `migrations/013-decision-rungs-are-domains/` takes the
// record off, and says which `.tech`/`.ai` chapters need a rating no script
// can recover.
// Version 12 changes no chapter shape: it retires the checkout layer of the
// stack-config overlay and the `.gitignore` block devbook materialized for it,
// so the stamp's `materialized` no longer carries `.gitignore#devbook`.
// Version 14 gives a `.domain` bounded context `requirements.md` and
// `invariants.md`, and with them four chapter types — `requirements` and
// `invariants` for the per-feature and per-aggregate grouping chapters,
// `requirement` and `invariant` for one rule — plus the two file types. A
// `requirements`/`invariants` chapter's `related` is held to a
// `feature`/`sub-feature` or `aggregate`/`domain-service` chapter, the way a
// switch reference already is. Every part of it is an added value with a safe
// default: nothing written under 13 stops validating, the aggregate's
// `### Invariants` table is still a legal structural heading, and no state
// exists for a script to move — so there is no `migrations/014-*`. Converting
// a table into chapters is editorial work a repository does when it chooses to.
// Version 15 changes no chapter shape: it renames the skill ids a stack config
// binds — every `install` becomes `init` and `update`, `devbook:check` becomes
// `devbook:validate`, `devbook-config:setup` becomes `devbook-config:init` —
// and the `devbook-check` schedule. A config still naming an old id binds a
// skill that no longer exists, so `migrations/015-openspec-verbs/` rewrites
// the committed config and both overlay layers.
// Version 16 gives `.domain`'s `bounded-context` chapter an optional
// `deployment` — `service` or `module` — for how the context ships, and the
// context's own `context.md` the same field on its file-level block; where a
// chapter's `related` names that `context.md`, the two must agree. An added
// field with no default to assume, so nothing written under 15 stops
// validating and there is no `migrations/016-*`.
// Version 17 moves a context's invariants out of their own `invariants.md` and
// into a subpage of the domain page whose aggregates enforce them:
// `domain.invariants.md`, and `domain.<name>.invariants.md` beside a split
// `domain.<name>.md`. The old names still validate for one release, with a
// warning, and `migrations/017-invariants-under-domain/` moves the files and
// rewrites every reference into them — a moved path is broken in every
// repository until a script moves it.
// Version 18 takes the scenarios off invariants and retitles the behaviour
// files by kind. An `### Invariant:` is a claim, its rejection code, and its
// `Enforced at:` line, proved by the `unit` test in `tests`; only a
// requirement is warned for having no `#### Scenario:`, and a scenario an older
// invariant still carries is tolerated. `requirements.md` is titled
// `# Requirements` and an invariants subpage `# Invariants`, so a menu listing
// pages by title can tell them from the context's other pages; a
// `requirements.<name>.md` split takes its feature's name, so the entries
// under `requirements.md` differ. A file still
// titled by its context validates; `migrations/018-behaviour-titles/`
// retitles it, because reconcile never touches an authored file.
//
// Version 19 lets an invariants chapter pair with the `## Shared Value
// Objects` or `## Shared Enums` grouping, so a shared type's own rules sit
// beside it in `domain.invariants.md` instead of under an aggregate that
// happens to use it. It only widens what `related` may name: nothing written
// under 18 stops validating, and no migration is owed.
//
// Version 20 lets a repository declare its own `status` ladder per folder, file
// glob, and block level in `.devbook/statuses.json` (statuses.mjs). The resting
// value, the decision rungs, and a required rating stay devbook's; a folder or a
// block the file does not name takes the built-in ladder, so a repository with
// no file validates exactly as under 19 and no migration is owed.
//
// Version 21 removes the review triad — `review`, `reviewer`, `review-at`.
// A review in progress is the chapter's `status` rung plus its open
// annotation fences, and who owes the next move lives in the pull request
// or the tracker. A leftover field is reported by name, and
// `migrations/021-no-review-triad/` deletes it.
//
// Version 22 gives `.design` its one chapter type, `requirement`: a rule a
// component keeps or breaks, as a `### Requirement:` with `#### Scenario:`
// cases under the component's chapter, held to `e2e` by the coverage warning.
// Every other `.design` chapter stays untyped, so nothing written under 21
// stops validating, and no migration is owed.
//
// Version 23 adopts the change folder, `openspec/changes/`, as a folder kind:
// each change's `proposal.md` is a `type: change` file at `status: proposed`
// with a `category`, each file under its `devbook-delta/` is a delta checked by
// the merge in delta.mjs, `archive/` is never indexed, and `change` is legal on
// any chapter as the merge's provenance. A repository without the folder
// validates exactly as under 22, and no migration is owed.
//
// Version 24 gives a change's `proposal.md` the two decision rungs, `approved`
// and `accepted`, with the six record fields, for the whole change: its
// fingerprint covers the proposal and every delta, an open question anywhere
// in the change stands against a rung, and `delta.mjs --apply` merges only an
// accepted change over its current fingerprint. The merge writes no rung onto
// the chapters it lands in and lifts one it makes stale. `domain/`'s own rungs
// are unchanged, so nothing written under 23 stops validating, and no
// migration is owed.
//
// Version 25 gives `.domain`, `.arc42`, and `.design` an optional `sync` —
// `push`, `pull`, `sync`, `report`, or `off` — for which way a chapter and its
// code sync, set on a folder overview, a `context.md`, a context page, or a
// unit's root chapter, nearest wins, `report` when none does. It is refused on
// a chapter a unit owns and on `requirements*.md` and `*.invariants.md`, and a
// value no unit inherits is warned. A `domain-event` is warned when its
// `related` names no aggregate or domain service raising it. Absent means
// `report`, today's behaviour, so nothing written under 24 stops validating
// and no migration is owed.
//
// Version 26 learns the click demos: the optional `demo` field on any chapter
// and on a change's `proposal.md` and `solution.md`, each address resolved
// against the demo's `demo-model`, and a requirement's walkthrough held to one
// of its own scenarios. Every `*.demo.html` keeps the HTML contract — its
// screens and anchors listed in `demo-model` once, no script outside the
// template's managed region but `demo-model` and `demo-meta`, nothing fetched,
// one variant under domain/, and 500 KB as a warning — and a page-named demo
// sits beside its page. A demo is folded into the fingerprint of the blocks it
// belongs to, so editing one lifts their approval. A corpus with no demo and no
// `demo` field validates and fingerprints exactly as under 25, and no
// migration is owed.
//
// Version 27 changes no chapter: the procedures fold into devbook, so their
// stamp entry moves from `components.devbook-procedures` to
// `components.devbook` — `procedures.adopted` beside the folders' `adopted`,
// and every procedure file in devbook's `materialized`, hashes kept. It ships
// as `migrations/027-procedures-in-devbook/`, with the procedures' own
// `001`–`004` carried over under their shipped ids.
//
// Version 28 makes a `user` and a `technical` actor chapter a sync unit's
// root, for the `actor` converter kind: `sync` is legal on them, and a value
// on `actors.md` is inherited by its users and technical actors. An
// `organisation` roots no unit, so `sync` on one is still refused and an
// `actors.md` of organisations only is still warned. Additive: a corpus
// written under 27 validates unchanged, and no migration is owed.
//
// Version 29 leaves a demo's managed region out of every fingerprint, so
// `demo-template.mjs --refresh` lifts no approval, and reads `demo-model` only
// in the shape the template's authoring reference states. A corpus with no
// demo fingerprints as under 28. A page approved over a demo under 28 reads as
// changed once and is approved again; demos are days old and no migration is
// owed for a value the approval gate rewrites.
export const CONTRACT_VERSION = 29;

// The oldest contract a reconcile still carries forward. A migration lives
// for the major version it ships in: a major release raises this to the
// contract the previous major last reached and drops every `migrations/`
// folder at or below it. A stamp below the floor is refused in phase 1 of
// the reconcile — upgrade through the previous major's last release first —
// so a folder that is gone can never be a hole in a ledger. 9 is where 1.0.0
// shipped, and nothing published sits below it.
export const MINIMUM_CONTRACT_VERSION = 9;

// What the derived artifacts stamp themselves with. The same number under the
// name a consumer of `graph.json` / `index.json` reads it by: the schema those
// files follow *is* the contract, so keeping two counters would only let them
// drift. A contract bump with no migration folder is normal and harmless —
// presence of a migration decides whether one runs, never the version number.
export const SCHEMA_VERSION = CONTRACT_VERSION;
export const REPO_SCOPE = ".";

// Where a repository that adopts the convention keeps this folder: under
// `.devbook/`, beside the stack config and the stamp, because the generator is
// plain Node and `.github/` is one host's folder. The fallback for
// `generatedBy`, and still the right answer whenever the generator is not
// inside the repository it is indexing — a plugin install, or `--root`.
export const GENERATOR = ".devbook/_tools/devbook-meta/build.mjs";

const GENERATOR_FILE = fileURLToPath(new URL("./build.mjs", import.meta.url));

/**
 * What a derived artifact stamps as `generatedBy`: a repo-relative path to the
 * generator, so anyone finding a `_meta/` file knows how to regenerate it.
 *
 * A repository that vendors this folder somewhere other than the conventional
 * location — the one that authors the convention, for instance — gets a path
 * that actually resolves. When the generator sits outside the repository the
 * only honest answer is the conventional location: an absolute path would break
 * determinism, and a `../..` climb out of the repository is not repo-relative.
 */
export function generatorPath(repoRoot) {
    const relative = path.relative(path.resolve(repoRoot), GENERATOR_FILE);
    if (!relative || relative.startsWith("..") || path.isAbsolute(relative)) return GENERATOR;
    return relative.split(path.sep).join("/");
}

// Metadata fields that hold `<path>` / `<path>#<slug>` references, and the edge
// type each one produces. Non-reference list fields (`aliases`, `alternatives`,
// `role`, `roadmap`, `stage`) are deliberately absent — they stay node attributes.
//
// A `feature-flag` gates the feature — `gated-by`; a `setting` gates or shapes
// it, so the edge says `configured-by` and leaves which to the setting's own
// values. The target's kind is held to the field below, since a reference that
// resolves to the wrong kind of chapter parses like a right one.
const REFERENCE_FIELDS = {
    "depends-on": "depends-on",
    "feature-flag": "gated-by",
    setting: "configured-by",
    related: "related",
};

// The chapter `kind` each switch field must resolve to.
const SWITCH_TARGET_KIND = { "feature-flag": "feature-flag", setting: "setting" };

// The prose chapter each behaviour chapter pairs with, by the behaviour
// chapter's own kind. `requirements.md` and `invariants.md` hold what a feature
// promises and what an aggregate enforces; the prose half holds why it exists,
// who works with it, where the boundary runs. `related` is the only thing
// joining the two, so a behaviour chapter that names none is a half nobody can
// reach from the other side — reported as an error, like a switch reference
// landing on the wrong kind.
//
// A domain service is in the `invariants` row because it enforces rules of its
// own; what it *reacts* to is a requirement of whatever reacts, and lands in
// the `requirements` row through that feature. The `## Shared Value Objects`
// and `## Shared Enums` groupings are there because they hold what belongs to
// no single aggregate, and the rules a shared type enforces belong to it too:
// pinned under one aggregate that uses the type they read as that aggregate's,
// and copied under each they are the duplicate the prose side forbids. The
// groupings live on `domain.md`, which never splits, so their rules land in
// `domain.invariants.md` and the placement check below needs nothing more.
// The check asks for one entry of the right kind and no more: a chapter may
// link onward to anything else.
const RELATED_TARGET_KINDS = {
    requirements: ["feature", "sub-feature"],
    invariants: ["aggregate", "domain-service", "shared-value-objects", "shared-enums"],
};

// The chapter kinds a `domain-event`'s `related` names as the one that raises it.
const RAISER_KINDS = ["aggregate", "domain-service"];

// The authored `type` field is emitted under the node key `kind`, because
// `type` on a node is already the structural discriminator
// (`file`/`chapter`/`heading`/`external`). `.tech` nodes have always carried
// `kind`; the field is simply populated for every folder now.
const ATTRIBUTE_FIELDS = [
    "version",
    "issue",
    "aliases",
    "alternatives",
    "key",
    "default",
    "scope",
    "deployment",
    // Which way the chapter and its code sync. Carried as written; the
    // effective direction of a unit is resolved from `syncSources`.
    "sync",
    "date",
    "approved-by",
    "approved-at",
    // Carried so a consumer can tell "approved, and the content has not moved"
    // from a bare "approved" without re-reading the Markdown or asking git.
    "approved-hash",
    // The rung above it: who accepted the built work against this chapter, and
    // when. The approval record stays beside it — the two are a stack.
    "accepted-by",
    "accepted-at",
    "accepted-hash",
    // The change folder's own: a proposal's category, and on any chapter the
    // change whose merge last touched it.
    "category",
    "change",
];

// Non-reference fields whose authored form may be a scalar or a bracket list,
// and which are always emitted as a list so a consumer reading graph.json never
// has to branch on shape. `aliases`/`alternatives` above stay verbatim.
//
// `tests` is here rather than in REFERENCE_FIELDS because a test identifier
// names something in a test project, not a chapter, so it produces no edge — the
// same reason `role`, `roadmap`, and `.ai`'s `stage` stay attributes — a `role`
// names something in the authorization configuration.
//
// `demo` is here for the same reason: it names a place in a click demo, which
// is HTML and no node. demo.mjs resolves each address against the demo itself.
const LIST_ATTRIBUTE_FIELDS = ["role", "roadmap", "stage", "tests", "demo"];

// Fields authored as an integer scalar. The parser hands back the raw string,
// so they are coerced here and a viewer can sum or threshold them directly.
// A value that is not a non-negative integer is left off the node — the lint in
// metadata.mjs is what reports it, and the graph never carries a bad number.
const NUMERIC_ATTRIBUTE_FIELDS = ["effort"];

/** Recursively collect Markdown files under a folder, as repo-relative posix paths. */
async function collectMarkdown(repoRoot, relFolder) {
    const results = [];
    async function walk(current) {
        let entries;
        try {
            entries = await readdir(path.join(repoRoot, current), { withFileTypes: true });
        } catch {
            return; // folder not present yet
        }
        for (const entry of entries) {
            const child = `${current}/${entry.name}`;
            if (entry.isDirectory()) await walk(child);
            else if (entry.isFile() && entry.name.endsWith(".md")) results.push(child);
        }
    }
    await walk(relFolder);
    return results.sort();
}

function asList(value) {
    if (value === null || value === undefined) return [];
    return Array.isArray(value) ? value : [value];
}

function applyMeta(node, meta, folder) {
    if (!meta) return;
    // An omitted status in an editorial folder means that folder's resting
    // value, so the node carries the resolved word — a viewer that read
    // `meta.status` straight through would badge a few hundred resting
    // chapters as unknown and collapse `nodesByStatus`. `statusDeclared` is
    // written only when it is false, both to keep the distinction between "at
    // rest" and "nobody said" available and to leave every already-declared
    // node byte-identical to what earlier versions emitted.
    const { status, declared } = resolveStatus(folder, meta);
    node.status = status;
    if (!declared && status !== null) node.statusDeclared = false;
    const declaredType = resolveType(folder, meta);
    if (declaredType !== null) node.kind = declaredType;
    for (const field of ATTRIBUTE_FIELDS) {
        if (meta[field] !== undefined && meta[field] !== null) node[field] = meta[field];
    }
    for (const field of LIST_ATTRIBUTE_FIELDS) {
        const values = asList(meta[field]);
        if (values.length) node[field] = values;
    }
    for (const field of NUMERIC_ATTRIBUTE_FIELDS) {
        const raw = meta[field];
        if (typeof raw === "string" && /^\d+$/.test(raw)) node[field] = Number(raw);
    }
    for (const field of Object.keys(REFERENCE_FIELDS)) {
        const refs = asList(meta[field]);
        if (refs.length) node[field] = refs;
    }
    // Everything under `ext` belongs to a plugin layered on top of devbook and
    // is carried through verbatim: same keys, same values, gathered under one
    // node key so a consumer can hand the block to its owner without having to
    // know what is in it. No edge is produced, and no value is inspected.
    const ext = {};
    for (const [key, value] of Object.entries(meta)) {
        if (isExtensionField(key)) ext[key] = value;
    }
    if (Object.keys(ext).length) node.ext = ext;
}

/**
 * A chapter's lede: its first paragraph or blockquote, joined to one line with
 * its inline Markdown kept as written. Fences — the `meta` block, an
 * `annotation`, a diagram — are stepped over, and the next heading ends the
 * search, so a chapter that opens straight into a sub-chapter has none.
 *
 * Read from the lines `buildGraph` already holds, so the term register gets
 * its descriptions without a second pass over the corpus.
 */
function chapterLede(lines, headingLine) {
    let fence = null;
    const paragraph = [];
    for (let i = headingLine; i < lines.length; i++) {
        const line = lines[i];
        const marker = /^\s*(`{3,}|~{3,})/.exec(line);
        if (fence) {
            if (marker && marker[1][0] === fence[0] && marker[1].length >= fence.length) fence = null;
            continue;
        }
        if (marker) {
            if (paragraph.length) break;
            fence = marker[1];
            continue;
        }
        if (/^#{1,6}\s/.test(line)) break;
        if (line.trim() === "") {
            if (paragraph.length) break;
            continue;
        }
        paragraph.push(line.replace(/^\s*>\s?/, "").trim());
    }
    const lede = paragraph.join(" ").replace(/\s+/g, " ").trim();
    return lede || null;
}

/**
 * Compose a file node's display label.
 *
 * A `domain/` file is titled by what it holds, so a split file's title is its
 * chapter's name and an older file's may still be the context's. The file's
 * `type` says which kind of page it is, and is left off when the title already
 * says it ("Domain" + `domain`, "Context Map" + `context-map`).
 */
function composeFileLabel(title, type) {
    if (!type || slugify(title) === type) return title;
    return `${title} (${type})`;
}

/**
 * Build the graph from every devbook document.
 *
 * Nodes: one per file, one per heading that carries a `meta` block, plus any
 * structural heading that something actually references. Edges: `contains`
 * from the structural hierarchy, and one per `depends-on`/`related` entry.
 */
export async function buildGraph(repoRoot, folders = null) {
    const nodes = new Map();
    const edges = [];
    const problems = [];
    // Every heading anchor in the corpus, including structural headings with no
    // `meta` block. Those are still legal reference targets — e.g. a .domain
    // term pointing at a Value Object sub-chapter covered by its parent
    // aggregate's block — so they are materialized on demand.
    const headingIndex = new Map();
    // The sync level of every block that may state `sync` — every unit root
    // among them — keyed by node id. Kept off the nodes: the level follows
    // from the path and the type. `units.mjs` reads it to find the roots.
    const syncLevels = new Map();
    // Each chapter's lede, by node id. Kept off the nodes so graph.json does
    // not change shape; the term register is its one reader.
    const ledes = new Map();
    // Every anchor each indexed file renders, and every link its prose
    // carries — resolved once the whole corpus is read.
    const anchors = new Map();
    const links = [];
    // Every block carrying a `demo` field, resolved against the demos once the
    // corpus is read; and the reader both that and the fingerprints share.
    const demoHolders = [];
    const demoText = demoReader(repoRoot);

    const layout = folders ? null : await discoverLayout(repoRoot);
    const scanned = folders ?? layout.folders;
    for (const stray of layout?.stray ?? []) {
        problems.push({
            severity: "error",
            message:
                `${stray}/ sits at the repository root. Only the \`${DEVBOOK_ROOT}/\` layout is ` +
                `supported: move it to ${DEVBOOK_ROOT}/${stray.slice(1)}/ and repoint every reference. ` +
                `It is not indexed here.`,
        });
    }

    // The repository's own status ladder, if it declares one. A configuration
    // error is reported here, once, instead of on every block it would fail.
    const { ladder, issues: ladderIssues } = await loadStatusLadder(repoRoot);
    problems.push(...ladderIssues);

    // The change folder is indexed beside the folders whenever it exists: its
    // proposals and deltas are chapters, and a delta's references resolve like
    // any other. Its `archive/` is history and is never read.
    const files = [
        ...(await Promise.all(scanned.map((folder) => collectMarkdown(repoRoot, folder)))).flat(),
        ...(await changeFiles(repoRoot)),
    ];

    for (const relPath of files) {
        const folder = folderKindForPath(relPath);
        const raw = await readFile(path.join(repoRoot, relPath), "utf8");
        const { fileTitle, chapters } = parseDocument(raw);
        const lines = raw.split(/\r?\n/);
        anchors.set(relPath, fileAnchors(chapters));
        links.push(...proseLinks(raw).map((link) => ({ ...link, from: relPath })));

        // Open notes per chapter, counted from the same read. Carrying the
        // count on the node is what lets the canvas badge the chapters nobody
        // has answered — and because the canvas imports this module, the live
        // view and the committed graph.json can never disagree about it.
        const openNotes = new Map();
        for (const note of parseAnnotations(raw)) {
            if (resolveAnnotation(note.fields).status !== "open") continue;
            const key = note.chapter?.slug ?? null;
            openNotes.set(key, (openNotes.get(key) ?? 0) + 1);
        }

        const fileMeta = chapters.find((c) => c.level === 1)?.meta ?? null;
        const delta = changePathParts(relPath)?.part === "delta" ? parseDeltaHeader(raw)?.meta ?? {} : null;
        const fileNode = {
            id: relPath,
            label: composeFileLabel(
                fileTitle ?? path.basename(relPath, ".md"),
                resolveType(folder, fileMeta)
            ),
            type: "file",
            folder,
            path: relPath,
        };

        // An .arc42 file is exactly one top-level chapter, so its level-1 block
        // serves as the file-level block; other folders follow the same shape.
        applyMeta(fileNode, fileMeta, folder);
        const fileSync = syncLevel(relPath, "file", fileMeta, 1);
        if (fileSync.level) syncLevels.set(fileNode.id, fileSync.level);
        // Set outside applyMeta because it also comes from the filename, which
        // no metadata field can supply.
        const number = documentNumber(relPath, fileMeta);
        if (number !== null) fileNode.number = number;
        // Omitted rather than emitted as 0, so adding this did not churn every
        // node of every existing index.
        // A delta's own header says which change it belongs to and what it
        // does; where it lands is its path, re-rooted at the devbook.
        if (delta) {
            if (delta.change) fileNode.change = delta.change;
            if (delta.delta) fileNode.delta = delta.delta;
            fileNode.target = changePathParts(relPath).target;
        }
        const fileOpenNotes = [...openNotes.values()].reduce((a, b) => a + b, 0);
        if (fileOpenNotes) fileNode.openNotes = fileOpenNotes;
        nodes.set(fileNode.id, fileNode);

        // The schema lint, over every block in the file at once. It is the only
        // implementation of the per-block rules — the status ladders, the value
        // shapes, the unrecognized-field sweep, a heading carrying no `meta`
        // block at all — so the gate runs it here rather than leaving it to the
        // editor extension, which not every author has open. Each issue keeps
        // its own severity: a warning stays a warning, and only an error fails
        // the run.
        // A delta is checked by the merge that would apply it: its header, its
        // shape, every chapter it names resolved in its target, and the merged
        // target through this same lint.
        // A proposal's rungs decide the whole change, so its fingerprint and its
        // open questions are read across the proposal and every delta.
        const proposalOf = changePathParts(relPath)?.part === "proposal" ? await readChange(repoRoot, changePathParts(relPath).name) : null;
        const fileIssues = delta
            ? (await checkDelta(repoRoot, relPath, raw, { ladder, demoText })).issues
            : proposalOf
              ? [
                    ...validateDocument(relPath, raw, { ladder, changeHash: changeHash(proposalOf.proposal, proposalOf.deltas) }),
                    ...changeDecisionIssues(proposalOf),
                ]
              : validateDocument(relPath, raw, { ladder, demoText });
        for (const issue of fileIssues) {
            problems.push({
                severity: issue.severity,
                path: relPath,
                message: `${relPath} ${issue.message}`,
            });
        }

        const scenarios = requirementScenarios(relPath, raw);
        for (const chapter of chapters) {
            if (chapter.meta?.demo == null) continue;
            const id = chapter.level === 1 ? relPath : `${relPath}#${chapter.slug}`;
            const refs = Array.isArray(chapter.meta.demo) ? chapter.meta.demo : [chapter.meta.demo];
            demoHolders.push({ id, path: relPath, refs, scenarios: scenarios.get(id) ?? null });
        }

        // Track the nearest enclosing addressable heading per level so
        // sub-chapters attach to their parent rather than to the file.
        const ancestors = [{ level: 1, id: relPath }];

        for (const chapter of chapters) {
            if (chapter.level === 1) continue;
            while (ancestors.length && ancestors[ancestors.length - 1].level >= chapter.level) {
                ancestors.pop();
            }
            const parentId = ancestors.length ? ancestors[ancestors.length - 1].id : relPath;

            const id = `${relPath}#${chapter.slug}`;

            // An anchor is claimed by any heading, structural ones included, so
            // this runs before the fence guard below. The first heading keeps
            // it — that is the one GitHub leaves unsuffixed — and the later one
            // is dropped rather than overwriting it.
            //
            // Only a collision with a chapter on either side is reported. A
            // chapter that cannot be addressed is the error; two structural
            // headings sharing an anchor is the ordinary shape of a chapter
            // file (`#### Scenario: The order is already confirmed` under two
            // rules in one `requirements.md`, `### Payload` under every event),
            // and those are materialized on demand, never referenced by
            // accident.
            if (headingIndex.has(id)) {
                if (chapter.meta || nodes.has(id)) {
                    problems.push({
                        severity: "error",
                        path: relPath,
                        message: `Duplicate chapter anchor "${id}" — two headings slugify identically, so a reference to it is ambiguous. The first heading keeps the anchor; the one on line ${chapter.line} is dropped from the graph and cannot be addressed.`,
                    });
                }
                continue;
            }

            headingIndex.set(id, {
                id,
                label: chapter.text,
                type: "heading",
                folder,
                path: relPath,
                slug: chapter.slug,
                level: chapter.level,
                line: chapter.line,
                parent: parentId,
            });

            if (!chapter.meta) continue; // structural heading, not an addressable chapter

            const node = {
                id,
                label: chapter.text,
                type: "chapter",
                folder,
                path: relPath,
                slug: chapter.slug,
                level: chapter.level,
                line: chapter.line,
            };
            applyMeta(node, chapter.meta, folder);
            const chapterSync = syncLevel(relPath, "chapter", chapter.meta, chapter.level);
            if (chapterSync.level) syncLevels.set(id, chapterSync.level);
            if (openNotes.get(chapter.slug)) node.openNotes = openNotes.get(chapter.slug);
            nodes.set(id, node);
            const lede = chapterLede(lines, chapter.line);
            if (lede) ledes.set(id, lede);
            ancestors.push({ level: chapter.level, id });

            edges.push({
                id: `contains:${parentId}->${id}`,
                source: parentId,
                target: id,
                type: "contains",
            });
        }
    }

    // Reference edges are resolved only after every node exists, so forward
    // references across files are valid.
    for (const node of nodes.values()) {
        for (const [field, edgeType] of Object.entries(REFERENCE_FIELDS)) {
            for (const ref of asList(node[field])) {
                const targetPath = ref.split("#")[0];
                if (!nodes.has(ref)) {
                    // A structural heading is a legal target — materialize it
                    // (with its containment edge) the first time it is cited.
                    const heading = headingIndex.get(ref);
                    if (heading) {
                        const { parent, ...data } = heading;
                        nodes.set(ref, data);
                        edges.push({
                            id: `contains:${parent}->${ref}`,
                            source: parent,
                            target: ref,
                            type: "contains",
                        });
                    } else {
                        const insideDevbook = folderKindForPath(targetPath) !== null;
                        problems.push({
                            severity: insideDevbook ? "error" : "warning",
                            path: node.path,
                            message: insideDevbook
                                ? `${node.id} has \`${field}\` reference "${ref}" that does not resolve to any chapter, heading, or file.`
                                : `${node.id} has \`${field}\` reference "${ref}" pointing outside the devbook folders; recorded as an external node.`,
                        });
                        if (insideDevbook) continue; // don't invent a node for a typo
                        nodes.set(ref, {
                            id: ref,
                            label: ref,
                            type: "external",
                            folder: null,
                            path: targetPath,
                        });
                    }
                }
                const expectedKind = SWITCH_TARGET_KIND[field];
                const targetKind = nodes.get(ref)?.kind;
                if (expectedKind && targetKind !== expectedKind) {
                    problems.push({
                        severity: "error",
                        path: node.path,
                        message: `${node.id} has \`${field}\` reference "${ref}" that resolves to a ${targetKind ? `\`${targetKind}\` chapter` : "heading or file"}, not a \`${expectedKind}\` chapter — point it at the switch's own chapter in the context's \`context.md\`.`,
                    });
                }
                // tech/ points depends-on at tech/ alone; a relation to another folder is
                // `related`, per the tech rule.
                if (field === "depends-on" && node.folder === "tech" && folderKindForPath(targetPath) !== "tech") {
                    problems.push({
                        severity: "warning",
                        path: node.path,
                        message: `${node.id} has \`depends-on\` reference "${ref}" outside tech/; tech/ uses \`related\` for a chapter in another folder.`,
                    });
                }
                edges.push({
                    id: `${edgeType}:${node.id}->${ref}`,
                    source: node.id,
                    target: ref,
                    type: edgeType,
                });
            }
        }

        // The behaviour half of a chapter has to name the prose half it
        // belongs to. Only a `##` grouping chapter carries the pairing: the
        // file-level block shares the same `type` word but covers every
        // feature or aggregate in the context, so it has no one chapter to
        // point at, and a `requirement`/`invariant` is paired through the
        // grouping chapter that contains it.
        const pairKinds = node.type === "chapter" ? RELATED_TARGET_KINDS[node.kind] : null;
        if (pairKinds) {
            const paired = asList(node.related)
                .map((ref) => nodes.get(ref))
                .filter((target) => pairKinds.includes(target?.kind));
            if (!paired.length) {
                problems.push({
                    severity: "error",
                    path: node.path,
                    message: `${node.id} is a \`${node.kind}\` chapter whose \`related\` names no ${pairKinds
                        .map((kind) => `\`${kind}\``)
                        .join(" or ")} chapter — the behaviour half of a chapter points at the prose half it belongs to, and nothing else pairs the two.`,
                });
            } else if (node.kind === "invariants") {
                // An invariants subpage belongs to one domain page: the
                // aggregates it holds rules for are chapters of that page, so
                // splitting an aggregate out moves its rules with it.
                const { page } = domainFileName(node.path);
                const pagePath = page && path.posix.join(path.posix.dirname(node.path), page);
                if (pagePath && !paired.some((target) => target.path === pagePath)) {
                    problems.push({
                        severity: "warning",
                        path: node.path,
                        message: `${node.id} is in the invariants subpage of ${pagePath}, but pairs with ${paired
                            .map((target) => target.id)
                            .join(", ")} — an aggregate's invariants sit in the subpage of the page that holds the aggregate. Move the chapter there.`,
                    });
                }
            }
        }
    }

    // How a context ships is stated twice — on the map, by its
    // `bounded-context` chapter, and by the context itself, on its
    // `context.md` — so a reader of either sees it without opening the other.
    // The pair is the chapter and the `context` file its `related` names, and
    // the two have to agree: a context written as `module` on the map and
    // `service` in its own folder has no answer at all.
    for (const node of nodes.values()) {
        if (node.type !== "chapter" || node.kind !== "bounded-context") continue;
        for (const ref of asList(node.related)) {
            const context = nodes.get(ref);
            if (context?.type !== "file" || context.kind !== "context") continue;
            if ((node.deployment ?? null) === (context.deployment ?? null)) continue;
            const state = (value) => (value == null ? "no `deployment`" : `\`deployment: ${value}\``);
            problems.push({
                severity: "error",
                path: node.path,
                message: `${node.id} states ${state(node.deployment)} but ${context.id} states ${state(context.deployment)} — how a context ships is written the same on its \`bounded-context\` chapter and its \`context.md\`, or on neither.`,
            });
        }
    }

    // A domain event belongs to the aggregate root or domain service that
    // raises it, and `related` is the only place that says which: the Trigger
    // names the raiser in prose, which no tool groups by. Without it the event
    // is in no sync unit. A warning, so a corpus written before the field was
    // asked for keeps passing the check.
    for (const node of nodes.values()) {
        if (node.type !== "chapter" || node.folder !== "domain" || node.kind !== "domain-event") continue;
        const raised = asList(node.related).some((ref) => RAISER_KINDS.includes(nodes.get(ref)?.kind));
        if (raised) continue;
        problems.push({
            severity: "warning",
            path: node.path,
            message: `${node.id} is a \`domain-event\` chapter whose \`related\` names no ${RAISER_KINDS.map((kind) => `\`${kind}\``).join(" or ")} chapter — name the one that raises it, which is what places the event in that unit.`,
        });
    }

    // A stated direction no unit inherits does nothing: every unit below it
    // states its own, or the level holds no unit at all (an `actors.md` of
    // organisations only). Each unit resolves nearest-wins through
    // `syncSources`; whatever no unit resolved to is reported.
    const inherited = new Set();
    for (const [id, level] of syncLevels) {
        if (level !== "unit") continue;
        const source = syncSources(id).find((ref) => syncLevels.has(ref) && nodes.get(ref)?.sync != null);
        if (source) inherited.add(source);
    }
    for (const [id, level] of syncLevels) {
        const node = nodes.get(id);
        if (level === "unit" || inherited.has(id) || !SYNC_DIRECTIONS.includes(node?.sync)) continue;
        problems.push({
            severity: "warning",
            path: node.path,
            message: `${id} has \`sync: ${node.sync}\`, which no unit inherits — every unit under it states its own direction, or none sits under it. Remove it, or set it where a unit reads it.`,
        });
    }

    problems.push(...(await brokenLinkIssues(repoRoot, links, anchors)));
    problems.push(...(await demoProblems(repoRoot, scanned, demoHolders)));

    return { nodes: [...nodes.values()], edges, problems, ledes, syncLevels };
}

/**
 * The anchors GitHub renders for a file's headings: the first heading with a
 * slug keeps it bare, and each later one gets `-1`, `-2`, … in order. The
 * graph keeps only the first, since a reference has to be unambiguous; a
 * link written against GitHub's page may name either.
 */
function fileAnchors(chapters) {
    const seen = new Map();
    const result = new Set();
    for (const { slug } of chapters) {
        const count = seen.get(slug) ?? 0;
        seen.set(slug, count + 1);
        result.add(count ? `${slug}-${count}` : slug);
    }
    return result;
}

/**
 * A prose link that does not resolve: a relative target with no file behind
 * it, or an anchor no heading in an indexed file renders. `related` and
 * `depends-on` are checked above; nothing else reads prose, which is how a
 * link to a deleted chapter outlives it.
 *
 * Warnings only — a chapter may link ahead to one not written yet. An absolute
 * URL is never fetched, and a file outside the indexed corpus is checked for
 * existence alone: its anchors are not this tool's to know.
 */
async function brokenLinkIssues(repoRoot, links, anchors) {
    const issues = [];
    for (const { from, line, target } of links) {
        if (/^[a-z][a-z0-9+.-]*:/i.test(target) || target.startsWith("//")) continue;
        const hash = target.indexOf("#");
        const rawPath = hash === -1 ? target : target.slice(0, hash);
        const anchor = hash === -1 ? null : safeDecode(target.slice(hash + 1));
        const decoded = safeDecode(rawPath);
        const resolved = !decoded
            ? from
            : decoded.startsWith("/")
              ? path.posix.normalize(decoded.slice(1))
              : path.posix.join(path.posix.dirname(from), decoded);
        const at = `${from}:${line}`;
        if (decoded && !(await exists(path.join(repoRoot, resolved)))) {
            issues.push({
                severity: "warning",
                path: from,
                message: `${at} links to "${target}", but ${resolved} does not exist.`,
            });
            continue;
        }
        const rendered = anchors.get(resolved);
        if (!anchor || !rendered || rendered.has(anchor) || rendered.has(anchor.toLowerCase())) continue;
        issues.push({
            severity: "warning",
            path: from,
            message: `${at} links to "${target}", but no heading in ${resolved} renders the anchor "#${anchor}".`,
        });
    }
    return issues;
}

function safeDecode(text) {
    try {
        return decodeURIComponent(text);
    } catch {
        return text;
    }
}

async function exists(absolutePath) {
    try {
        await stat(absolutePath);
        return true;
    } catch {
        return false;
    }
}

function summarize(nodes, edges) {
    const count = (items, key) =>
        items.reduce((acc, item) => {
            const value = item[key] ?? "none";
            acc[value] = (acc[value] ?? 0) + 1;
            return acc;
        }, {});
    return {
        nodes: nodes.length,
        edges: edges.length,
        nodesByFolder: count(nodes, "folder"),
        nodesByType: count(nodes, "type"),
        nodesByKind: count(nodes, "kind"),
        nodesByStatus: count(nodes, "status"),
        edgesByType: count(edges, "type"),
    };
}

/**
 * Project the full graph down to one scope.
 *
 * A scoped graph keeps every node inside the scope, plus any node outside it
 * that an in-scope node references — those boundary nodes are flagged
 * `outOfScope: true` so a viewer can render them as stubs rather than pretend
 * they are part of the scope. Edges are kept when both ends survive.
 *
 * `scope` is a devbook folder path (".tech") or "." for the whole repository.
 */
export function projectScope(graph, scope) {
    if (scope === REPO_SCOPE) {
        return { nodes: graph.nodes, edges: graph.edges, problems: graph.problems };
    }

    const prefix = `${scope}/`;
    const inScope = (node) => node.path === scope || node.path?.startsWith(prefix);

    const kept = new Map();
    for (const node of graph.nodes) {
        if (inScope(node)) kept.set(node.id, node);
    }

    const byId = new Map(graph.nodes.map((node) => [node.id, node]));
    const boundary = new Map();
    for (const edge of graph.edges) {
        const sourceIn = kept.has(edge.source);
        const targetIn = kept.has(edge.target);
        if (!sourceIn && !targetIn) continue;
        // Only outbound references pull a boundary node in; an unrelated
        // document merely pointing *at* this scope must not drag its whole
        // neighbourhood into the scoped graph.
        if (sourceIn && !targetIn) {
            const target = byId.get(edge.target);
            if (target) boundary.set(target.id, { ...target, outOfScope: true });
        }
    }

    const nodes = [...kept.values(), ...boundary.values()];
    const available = new Set(nodes.map((node) => node.id));
    const edges = graph.edges.filter(
        (edge) => available.has(edge.source) && available.has(edge.target) && kept.has(edge.source)
    );

    return {
        nodes,
        edges,
        problems: graph.problems.filter((problem) => !problem.path || inScope({ path: problem.path })),
    };
}

/**
 * Build the serializable index document for one scope, following the
 * derived-artifacts convention.
 *
 * Pass a pre-built graph to project several scopes without re-reading disk.
 * `folders` is the set of devbook folders this repository actually adopts,
 * so the repo-wide `sources` never claims a folder that is not there.
 */
export async function buildGraphDocument(
    repoRoot,
    scope = REPO_SCOPE,
    prebuilt = null,
    folders = null
) {
    folders ??= (await discoverLayout(repoRoot)).folders;
    const graph = prebuilt ?? (await buildGraph(repoRoot, folders));
    const { nodes, edges, problems } = projectScope(graph, scope);
    return {
        // Bumped whenever the emitted shape changes, so consumers detect drift.
        schemaVersion: SCHEMA_VERSION,
        generatedBy: generatorPath(repoRoot),
        scope,
        sources: scope === REPO_SCOPE ? [...folders, ...(await hasChanges(repoRoot))] : [scope],
        // Deliberately no timestamp: the index is a deterministic function of
        // the Markdown, so re-running it produces a byte-identical file and CI
        // can diff it to detect a stale commit.
        stats: summarize(nodes, edges),
        problems,
        elements: {
            nodes: nodes.map((data) => ({ data })),
            edges: edges.map((data) => ({ data })),
        },
    };
}

/** Every scope this generator knows about: the repo-wide rollup plus one per folder. */
export const SCOPES = [REPO_SCOPE, ...DEVBOOK_FOLDERS];

/**
 * A scope as a caller may spell it — `tech`, `.tech`, `.devbook/tech`, or `.`
 * — resolved to the one spelling the generator uses, or null when it names no
 * scope. The short forms exist because a folder is called `.tech` in every
 * rule and every conversation, and `--scope .tech` should keep meaning it.
 */
export function resolveScope(value) {
    if (value == null || value === "") return null;
    const normalized = String(value).replace(/\\/g, "/").replace(/\/+$/, "");
    if (SCOPES.includes(normalized)) return normalized;
    const name = normalized.startsWith(".") ? normalized.slice(1) : normalized;
    const full = `${DEVBOOK_ROOT}/${name}`;
    return SCOPES.includes(full) ? full : null;
}

/**
 * The scopes a specific repository actually has, so a repo that adopts only
 * `.domain` and `.arc42` never gets `_meta/` folders for conventions it does
 * not use. Returns an empty array when no devbook folder is present.
 */
export async function discoverScopes(repoRoot) {
    const { folders } = await discoverLayout(repoRoot);
    return folders.length ? [REPO_SCOPE, ...folders] : [];
}

/**
 * Which devbook folders this repository actually has.
 *
 * `folders` holds the real repository paths under `.devbook/`. `stray` lists
 * any of the five spelled as a root-level dot-folder — the layout this
 * convention no longer supports (the chapter-schema decision). A stray folder is reported by the
 * graph build and never indexed, so a repository that has not moved yet learns
 * it from an error rather than from a quiet half-corpus.
 */
export async function discoverLayout(repoRoot) {
    const folders = [];
    const stray = [];
    for (const name of DEVBOOK_FOLDER_NAMES) {
        if (await isDirectory(path.join(repoRoot, DEVBOOK_ROOT, name))) {
            folders.push(`${DEVBOOK_ROOT}/${name}`);
        }
        if (await isDirectory(path.join(repoRoot, `.${name}`))) stray.push(`.${name}`);
    }
    // The change folder is adopted the same way, by existing.
    const changes = (await isDirectory(path.join(repoRoot, CHANGES_ROOT))) ? CHANGES_ROOT : null;
    return { folders, stray, changes };
}

/** The change folder as a rollup source, when the repository has one. */
async function hasChanges(repoRoot) {
    return (await isDirectory(path.join(repoRoot, CHANGES_ROOT))) ? [CHANGES_ROOT] : [];
}

async function isDirectory(absolutePath) {
    try {
        return (await stat(absolutePath)).isDirectory();
    } catch {
        return false; // Folder not adopted by this repository.
    }
}

/**
 * Repo-relative output path for a scope, per the derived-index convention: a
 * folder's beside its chapters, the rollup's under the parent itself.
 */
export function outputPathFor(scope) {
    return scope === REPO_SCOPE ? `${DEVBOOK_ROOT}/_meta/graph.json` : `${scope}/_meta/graph.json`;
}
