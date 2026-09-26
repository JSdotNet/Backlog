// outline.mjs — derives the ordered reading outline of a devbook area from
// the folder convention, not from anything authored per repository.
//
// Markdown stays canonical; this produces the *derived* index that a viewer
// reads to present files in their intended order instead of alphabetically.
//
// Ordering rules, per directory:
//   1. The directory's *root document* sorts first — the file declaring
//      `index: root`, or failing that the entry point its folder convention
//      names (`.devbook/domain/context-map.md`, a bounded context's `domain.md`,
//      `.devbook/tech/technology-graph.md`, `.devbook/design/README.md`, `.devbook/ai/adoption-map.md`).
//   2. If anything left carries a **number** — from a `number` field or from a
//      numbered filename — the directory is a numbered set (arc42 chapters,
//      ADRs, TDRs, `.ai` usage files) and sorts by that number ascending, unnumbered entries
//      filename-sorted after them.
//   3. Otherwise the remaining prescribed files follow in the sequence that
//      folder's own instructions file documents in its structure block, with
//      anything else — an extra file, a bounded-context subdirectory, a
//      repository-chosen `.tech` layer — filename-sorted in between. A split
//      file (`domain.order.md`, one chapter out of `domain.md`) reads directly
//      after the file it is named after, in that file's place when the file
//      itself is gone.
//   4. A directory that is neither numbered nor covered by a convention sorts
//      by filename.
//
// A document declaring `index: exclude` is left out of the outline entirely.
//
// So reading order is a property of the *convention* by default and of the
// document itself where the document has something to say — its number, or that
// it is its directory's entry point. What it is never a property of is some
// other document's metadata block.

import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import {
    parseDocument,
    documentDigest,
    folderKindForPath,
    resolveType,
    resolveStatus,
    documentNumber,
    fileNumberFromPath,
    indexRole,
} from "./metadata.mjs";
import { discoverLayout, SCHEMA_VERSION, REPO_SCOPE, generatorPath, DEVBOOK_ROOT } from "./graph.mjs";

/**
 * A document's `tests` entries as a list, whatever shape they were authored in.
 *
 * Carried on a `file` entry for the same reason `summary` and `diagrams` are: a
 * viewer drawing a folder's list view wants to badge what covers each document
 * without opening it. Chapter-level entries stay in `graph.json`, which is where
 * a consumer goes for per-chapter detail.
 */
function testList(meta) {
    const value = meta?.tests;
    if (value === null || value === undefined) return [];
    return Array.isArray(value) ? value : [value];
}

/**
 * Reading order per directory shape, keyed by devbook folder with each
 * subdirectory level written as `*`. `root` is the entry point; `first` and
 * `last` pin the prescribed siblings around whatever else the directory holds.
 * `split` names the files a `<file>.<name>.md` may be split out of; such a
 * file sorts directly after its base, or in the base's slot when the base is
 * absent, never among the unprescribed rest. `subpage` names a suffix and the
 * files that take it: `domain.invariants.md` and `domain.order.invariants.md`
 * read directly after the page they belong to.
 *
 * This mirrors the structure block in each folder's own instructions file —
 * change one and change the other in the same edit.
 */
const DIRECTORY_CONVENTION = {
    "domain": { root: "context-map.md", first: [], last: [] },
    "domain/*": {
        root: "context.md",
        first: [
            "domain.md",
            "actors.md",
            "skills.md",
            "features.md",
            "model.md",
            "flow.md",
            "dependencies.md",
        ],
        last: [],
        split: ["domain.md", "features.md", "skills.md", "model.md", "flow.md"],
        subpage: { suffix: "invariants", of: ["domain.md"] },
    },
    "tech": { root: "technology-graph.md", first: ["shared.md"], last: ["tooling.md"] },
    // `.ai` needs no `first`/`last`: its usage files are numbered, so the
    // numbered branch orders the flow and leaves `concepts.md` filename-sorted
    // after it — map, then the flow in order, then the ideas underneath it.
    "ai": { root: "adoption-map.md", first: [], last: [] },
    "design": {
        root: "README.md",
        first: [
            "design-principles.md",
            "color-scheme.md",
            "typography-and-layout.md",
            "interaction-guidelines.md",
            "accessibility.md",
            "component-libraries.md",
        ],
        last: [],
    },
};

/**
 * The convention for a directory, or `null` when it sorts by filename. Keyed
 * by the folder kind and the depth below it, so `.devbook/domain/billing`
 * reads as `domain/*`.
 */
function conventionFor(relDir) {
    const normalized = relDir.replace(/\\/g, "/");
    const kind = folderKindForPath(`${normalized}/x.md`);
    if (!kind) return null;
    const below = normalized.split("/").slice(2);
    const key = below.length ? `${kind}/${below.map(() => "*").join("/")}` : kind;
    return DIRECTORY_CONVENTION[key] ?? null;
}

/**
 * Which entry is this directory's root document.
 *
 * An authored `index: root` wins over the convention, so a folder the
 * convention says nothing about — `.devbook/arc42/adr/`, a repository's own
 * subdirectory — can still name its entry point. Two of them is an error:
 * a directory has one way in.
 */
function resolveRoot(relDir, parsed, excluded, convention, problems) {
    const declared = [...parsed.entries()].filter(([, doc]) => indexRole(doc.meta) === "root");
    if (declared.length > 1) {
        problems.push({
            severity: "error",
            path: relDir,
            message: `${relDir} has more than one document declaring \`index: root\` (${declared
                .map(([name]) => name)
                .join(", ")}); a directory has exactly one entry point.`,
        });
    }
    if (declared.length) return declared[0][0];

    if (!convention) return null;
    if (parsed.has(convention.root)) return convention.root;

    problems.push(
        excluded.has(convention.root)
            ? {
                  severity: "warning",
                  path: `${relDir}/${convention.root}`,
                  message: `${relDir}/${convention.root} is the directory's root document by convention but declares \`index: exclude\`, so the directory now has no entry point. Drop the field, or mark another file \`index: root\`.`,
              }
            : {
                  severity: "warning",
                  path: relDir,
                  message: `${relDir} has no ${convention.root}; the convention makes it this directory's root document and the first thing read. Declare \`index: root\` on another file to name a different entry point.`,
              }
    );
    return null;
}

/**
 * Order one directory's entries: root document first, then by number if the
 * directory is a numbered set, otherwise by the folder convention.
 *
 * `numbers` maps an entry name to its number, or to `null` when it has none.
 */
function orderedSequence(relDir, names, numbers, rootName, problems) {
    const rest = [...names].filter((name) => name !== rootName).sort();
    const lead = rootName ? [rootName] : [];

    const numbered = rest.filter((name) => numbers.get(name) !== null);
    if (numbered.length) {
        const byNumber = new Map();
        for (const name of numbered) {
            const number = numbers.get(name);
            if (byNumber.has(number)) {
                problems.push({
                    severity: "error",
                    path: `${relDir}/${name}`,
                    message: `${relDir}/${name} and ${relDir}/${byNumber.get(number)} are both numbered ${number}; a number identifies one document in its directory.`,
                });
            } else {
                byNumber.set(number, name);
            }
        }
        // Sorted by number, then by name so a duplicated number is still
        // deterministic rather than dependent on directory read order.
        const inOrder = [...numbered].sort(
            (a, b) => numbers.get(a) - numbers.get(b) || a.localeCompare(b)
        );
        return [...lead, ...inOrder, ...rest.filter((name) => numbers.get(name) === null)];
    }

    const convention = conventionFor(relDir);
    if (!convention) return [...lead, ...rest];

    // A split file sorts with the file it is named after — after it, or in
    // its slot when the base was dropped once every chapter moved out. `rest`
    // is filename-sorted, so a base's split files keep that order among
    // themselves.
    const subpage = convention.subpage;
    const isSubpage = (name) => Boolean(subpage) && name.endsWith(`.${subpage.suffix}.md`);
    const subpageOf = (name) => {
        if (!subpage?.of.includes(splitBase(name) ?? name)) return [];
        const sub = name.replace(/\.md$/, `.${subpage.suffix}.md`);
        return rest.includes(sub) ? [sub] : [];
    };
    const splitsOf = (base) =>
        convention.split?.includes(base)
            ? rest.filter((name) => !isSubpage(name) && splitBase(name) === base)
            : [];
    const slot = (name) =>
        [...(rest.includes(name) ? [name] : []), ...splitsOf(name)].flatMap((page) => [page, ...subpageOf(page)]);

    const rootSplits = splitsOf(convention.root);
    const pinnedFirst = convention.first.flatMap(slot);
    const pinnedLast = convention.last.flatMap(slot);
    const placed = new Set([...rootSplits, ...pinnedFirst, ...pinnedLast]);

    return [
        ...lead,
        ...rootSplits,
        ...pinnedFirst,
        ...rest.filter((name) => !placed.has(name)),
        ...pinnedLast,
    ];
}

/**
 * The file a split file is named after: `domain.order.md` → `domain.md`, and
 * `null` for a name without exactly one inner segment.
 */
function splitBase(name) {
    const match = /^([^.]+)\.[^.]+\.md$/.exec(name);
    return match ? `${match[1]}.md` : null;
}

/** Read one directory into ordered `file` and `directory` outline entries. */
async function readDirectory(repoRoot, relDir, problems) {
    let entries;
    try {
        entries = await readdir(path.join(repoRoot, relDir), { withFileTypes: true });
    } catch {
        return []; // folder not present yet
    }

    const files = [];
    const dirs = [];
    for (const entry of entries) {
        // `_`-prefixed folders hold tooling artifacts, not readable content.
        if (entry.name.startsWith("_") || entry.name.startsWith(".")) continue;
        if (entry.isDirectory()) dirs.push(entry.name);
        else if (entry.isFile() && entry.name.endsWith(".md")) files.push(entry.name);
    }

    // Parse every file once: we need its title/status for the outline anyway,
    // and the digest (lede, diagram count) comes out of the same pass, which is
    // the whole point — a viewer listing this folder must not have to open the
    // files to learn what the generator already read.
    const parsed = new Map();
    const excluded = new Set();
    for (const name of files.sort()) {
        const relPath = `${relDir}/${name}`;
        const markdown = await readFile(path.join(repoRoot, relPath), "utf8");
        const { fileTitle, fileMeta } = parseDocument(markdown);
        const meta = fileMeta ?? {};
        // `index: exclude` keeps a document out of the outline. It is dropped
        // here rather than filtered later so it can never be picked as the root
        // document or occupy a number.
        if (indexRole(meta) === "exclude") {
            excluded.add(name);
            continue;
        }
        parsed.set(name, {
            relPath,
            title: fileTitle,
            meta,
            number: documentNumber(relPath, meta),
            digest: documentDigest(markdown),
        });
    }

    const convention = conventionFor(relDir);
    const rootName = resolveRoot(relDir, parsed, excluded, convention, problems);

    // A subdirectory has no metadata block, so its number can only come from
    // its name.
    const numbers = new Map([
        ...[...parsed.entries()].map(([name, doc]) => [name, doc.number]),
        ...dirs.map((name) => [name, fileNumberFromPath(name)]),
    ]);
    const sequence = orderedSequence(
        relDir,
        [...parsed.keys(), ...dirs],
        numbers,
        rootName,
        problems
    );

    const outline = [];
    for (const name of sequence) {
        if (parsed.has(name)) {
            const doc = parsed.get(name);
            // Titles are name-only, so every file in a `.domain` bounded context
            // shares one title; `kind` is what tells them apart in a viewer.
            const folder = folderKindForPath(doc.relPath);
            const fileKind = resolveType(folder, doc.meta);
            // Resolved, not passed through: a file that omits its status in an
            // editorial folder is at that folder's resting value, and an index
            // entry saying null would send a list view looking for a state the
            // convention already answers. `statusDeclared` records that the
            // file itself did not say it — written only when false, so no
            // existing entry churns.
            const { status, declared } = resolveStatus(folder, doc.meta);
            const tests = testList(doc.meta);
            outline.push({
                type: "file",
                name,
                path: doc.relPath,
                title: doc.title ?? path.basename(name, ".md"),
                ...(fileKind ? { kind: fileKind } : {}),
                status,
                ...(declared || status === null ? {} : { statusDeclared: false }),
                // Omitted rather than emitted empty, so adding these fields did
                // not churn every entry of every existing index.
                ...(doc.number !== null ? { number: doc.number } : {}),
                ...(doc.meta.date ? { date: doc.meta.date } : {}),
                // Always a list, so a list view can badge "2 tests" without
                // branching on whether the author wrote a scalar.
                ...(tests.length ? { tests } : {}),
                ...(doc.digest.summary ? { summary: doc.digest.summary } : {}),
                ...(doc.digest.diagrams ? { diagrams: doc.digest.diagrams } : {}),
                ...(name === rootName ? { root: true } : {}),
            });
        } else {
            const child = `${relDir}/${name}`;
            const children = await readDirectory(repoRoot, child, problems);
            outline.push({
                type: "directory",
                name,
                path: child,
                // A directory shows the title of its own root document, so a
                // viewer can label it without opening anything.
                title: children.find((c) => c.root)?.title ?? name,
                ...(numbers.get(name) !== null ? { number: numbers.get(name) } : {}),
                children,
            });
        }
    }
    return outline;
}

/**
 * Build the serializable outline document for one scope, following the
 * derived-artifacts convention.
 *
 * `folders` is the set of devbook folders this repository actually adopts.
 */
export async function buildOutlineDocument(repoRoot, scope = REPO_SCOPE, folders = null) {
    folders ??= (await discoverLayout(repoRoot)).folders;
    const problems = [];
    const roots = scope === REPO_SCOPE ? folders : [scope];

    let entries;
    if (scope === REPO_SCOPE) {
        // The repo-wide outline lists the devbook areas themselves, in the
        // canonical area order, each with its own outline nested underneath.
        entries = [];
        for (const folder of roots) {
            const children = await readDirectory(repoRoot, folder, problems);
            entries.push({
                type: "area",
                name: folder,
                path: folder,
                kind: folderKindForPath(`${folder}/x.md`),
                title: children.find((c) => c.root)?.title ?? folder,
                children,
            });
        }
    } else {
        entries = await readDirectory(repoRoot, scope, problems);
    }

    return {
        schemaVersion: SCHEMA_VERSION,
        generatedBy: generatorPath(repoRoot),
        scope,
        sources: roots,
        // Deliberately no timestamp: the index is a deterministic function of
        // the Markdown, so re-running it produces a byte-identical file and CI
        // can diff it to detect a stale commit.
        problems,
        entries,
    };
}

/** Repo-relative output path for a scope, per the derived-artifacts convention. */
export function outlinePathFor(scope) {
    return scope === REPO_SCOPE ? `${DEVBOOK_ROOT}/_meta/index.json` : `${scope}/_meta/index.json`;
}
