// graph.mjs — derives the cross-folder knowledge graph from the `meta` blocks
// embedded in .arc42/, .domain/, .backlog/, .tech/, and .design/.
//
// Markdown stays canonical; this produces the *derived* index. Output shape is
// Cytoscape.js `elements` JSON, which most graph libraries consume natively or
// map from trivially.
//
// Consumed by the `build.mjs` CLI and by the knowledge-graph canvas, so
// the CLI output and the live view can never disagree.

import { readFile, readdir, stat } from "node:fs/promises";
import path from "node:path";
import { parseDocument, folderKindForPath } from "./metadata.mjs";

/** Every knowledge folder this convention recognizes. A repository adopts any subset. */
export const KNOWLEDGE_FOLDERS = [".arc42", ".domain", ".backlog", ".tech", ".design"];
export const SCHEMA_VERSION = 1;
export const REPO_SCOPE = ".";
export const GENERATOR = ".github/tools/knowledge-meta/build.mjs";

// Metadata fields that hold `<path>` / `<path>#<slug>` references, and the edge
// type each one produces. Non-reference list fields (`aliases`, `alternatives`,
// `feature-flag`) are deliberately absent — they stay node attributes.
const REFERENCE_FIELDS = {
    "depends-on": "depends-on",
    related: "related",
    implements: "implements",
};

const ATTRIBUTE_FIELDS = ["kind", "version", "issue", "aliases", "alternatives"];

// Non-reference fields whose authored form may be a scalar or a bracket list,
// and which are always emitted as a list so a consumer reading graph.json never
// has to branch on shape. `aliases`/`alternatives` above stay verbatim.
const LIST_ATTRIBUTE_FIELDS = ["feature-flag"];

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

function applyMeta(node, meta) {
    if (!meta) return;
    node.status = meta.status ?? null;
    for (const field of ATTRIBUTE_FIELDS) {
        if (meta[field] !== undefined && meta[field] !== null) node[field] = meta[field];
    }
    for (const field of LIST_ATTRIBUTE_FIELDS) {
        const values = asList(meta[field]);
        if (values.length) node[field] = values;
    }
    for (const field of Object.keys(REFERENCE_FIELDS)) {
        const refs = asList(meta[field]);
        if (refs.length) node[field] = refs;
    }
}

/**
 * Build the graph from every knowledge document.
 *
 * Nodes: one per file, one per heading that carries a `meta` block, plus any
 * structural heading that something actually references. Edges: `contains`
 * from the structural hierarchy, and one per `depends-on`/`related`/
 * `implements` entry.
 */
export async function buildGraph(repoRoot) {
    const nodes = new Map();
    const edges = [];
    const problems = [];
    // Every heading anchor in the corpus, including structural headings with no
    // `meta` block. Those are still legal reference targets — e.g. a .domain
    // naming term pointing at a Value Object sub-chapter covered by its parent
    // aggregate's block — so they are materialized on demand.
    const headingIndex = new Map();

    const files = (
        await Promise.all(KNOWLEDGE_FOLDERS.map((folder) => collectMarkdown(repoRoot, folder)))
    ).flat();

    for (const relPath of files) {
        const folder = folderKindForPath(relPath);
        const raw = await readFile(path.join(repoRoot, relPath), "utf8");
        const { fileTitle, chapters } = parseDocument(raw);

        const fileNode = {
            id: relPath,
            label: fileTitle ?? path.basename(relPath, ".md"),
            type: "file",
            folder,
            path: relPath,
        };

        // An .arc42 file is exactly one top-level chapter, so its level-1 block
        // serves as the file-level block; other folders follow the same shape.
        applyMeta(fileNode, chapters.find((c) => c.level === 1)?.meta);
        nodes.set(fileNode.id, fileNode);

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

            if (nodes.has(id)) {
                problems.push({
                    severity: "error",
                    path: relPath,
                    message: `Duplicate chapter anchor "${id}" — two headings slugify identically, so references to it are ambiguous.`,
                });
            }

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
            applyMeta(node, chapter.meta);
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
                        const insideKnowledge = folderKindForPath(targetPath) !== null;
                        problems.push({
                            severity: insideKnowledge ? "error" : "warning",
                            path: node.path,
                            message: insideKnowledge
                                ? `${node.id} has \`${field}\` reference "${ref}" that does not resolve to any chapter, heading, or file.`
                                : `${node.id} has \`${field}\` reference "${ref}" pointing outside the knowledge folders; recorded as an external node.`,
                        });
                        if (insideKnowledge) continue; // don't invent a node for a typo
                        nodes.set(ref, {
                            id: ref,
                            label: ref,
                            type: "external",
                            folder: null,
                            path: targetPath,
                        });
                    }
                }
                edges.push({
                    id: `${edgeType}:${node.id}->${ref}`,
                    source: node.id,
                    target: ref,
                    type: edgeType,
                });
            }
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
 * `scope` is a knowledge folder path (".tech") or "." for the whole repository.
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
 * `folders` is the set of knowledge folders this repository actually adopts,
 * so the repo-wide `sources` never claims a folder that is not there.
 */
export async function buildGraphDocument(
    repoRoot,
    scope = REPO_SCOPE,
    prebuilt = null,
    folders = KNOWLEDGE_FOLDERS
) {
    const graph = prebuilt ?? (await buildGraph(repoRoot));
    const { nodes, edges, problems } = projectScope(graph, scope);
    return {
        // Bumped whenever the emitted shape changes, so consumers detect drift.
        schemaVersion: SCHEMA_VERSION,
        generatedBy: GENERATOR,
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
export const SCOPES = [REPO_SCOPE, ...KNOWLEDGE_FOLDERS];

/**
 * The scopes a specific repository actually has, so a repo that adopts only
 * `.domain` and `.arc42` never gets `_meta/` folders for conventions it does
 * not use. Returns an empty array when no knowledge folder is present.
 */
export async function discoverScopes(repoRoot) {
    const present = [];
    for (const folder of KNOWLEDGE_FOLDERS) {
        try {
            if ((await stat(path.join(repoRoot, folder))).isDirectory()) present.push(folder);
        } catch {
            // Folder not adopted by this repository.
        }
    }
    return present.length ? [REPO_SCOPE, ...present] : [];
}

/** Repo-relative output path for a scope, per the derived-index convention. */
export function outputPathFor(scope) {
    return scope === REPO_SCOPE ? "_meta/graph.json" : `${scope}/_meta/graph.json`;
}
