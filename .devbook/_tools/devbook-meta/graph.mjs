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
    resolveAnnotation,
    DEVBOOK_FOLDER_NAMES,
    DEVBOOK_ROOT,
} from "./metadata.mjs";

/**
 * Every devbook folder this convention recognizes, as the repository path it
 * lives at: `.devbook/arc42`, `.devbook/domain`, and so on. A repository adopts
 * any subset. A scope is one of these, or the repository rollup.
 */
export const DEVBOOK_FOLDERS = DEVBOOK_FOLDER_NAMES.map((name) => `${DEVBOOK_ROOT}/${name}`);

export { DEVBOOK_FOLDER_NAMES, DEVBOOK_ROOT };
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
// pages by title can tell them from the context's other pages. A file still
// titled by its context validates; `migrations/018-behaviour-titles/`
// retitles it, because reconcile never touches an authored file.
export const CONTRACT_VERSION = 18;

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
// the `requirements` row through that feature. The check asks for one entry of
// the right kind and no more: a chapter may link onward to anything else.
const RELATED_TARGET_KINDS = {
    requirements: ["feature", "sub-feature"],
    invariants: ["aggregate", "domain-service"],
};

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
];

// Non-reference fields whose authored form may be a scalar or a bracket list,
// and which are always emitted as a list so a consumer reading graph.json never
// has to branch on shape. `aliases`/`alternatives` above stay verbatim.
//
// `tests` is here rather than in REFERENCE_FIELDS because a test identifier
// names something in a test project, not a chapter, so it produces no edge — the
// same reason `role`, `roadmap`, and `.ai`'s `stage` stay attributes — a `role`
// names something in the authorization configuration.
const LIST_ATTRIBUTE_FIELDS = ["role", "roadmap", "stage", "tests"];

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
 * Compose a file node's display label.
 *
 * Heading text carries the name only, so every file in a `.domain` bounded
 * context is titled with the bare context name — six nodes sharing one label.
 * The file's `type` disambiguates them, and is left off when the title already
 * says it ("Context Map" + `context-map`).
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

    const files = (
        await Promise.all(scanned.map((folder) => collectMarkdown(repoRoot, folder)))
    ).flat();

    for (const relPath of files) {
        const folder = folderKindForPath(relPath);
        const raw = await readFile(path.join(repoRoot, relPath), "utf8");
        const { fileTitle, chapters } = parseDocument(raw);

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
        // Set outside applyMeta because it also comes from the filename, which
        // no metadata field can supply.
        const number = documentNumber(relPath, fileMeta);
        if (number !== null) fileNode.number = number;
        // Omitted rather than emitted as 0, so adding this did not churn every
        // node of every existing index.
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
        for (const issue of validateDocument(relPath, raw)) {
            problems.push({
                severity: issue.severity,
                path: relPath,
                message: `${relPath} ${issue.message}`,
            });
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
            if (openNotes.get(chapter.slug)) node.openNotes = openNotes.get(chapter.slug);
            nodes.set(id, node);
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

    return { nodes: [...nodes.values()], edges, problems };
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
        sources: scope === REPO_SCOPE ? folders : [scope],
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
 * convention no longer supports (record 80). A stray folder is reported by the
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
    return { folders, stray };
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
