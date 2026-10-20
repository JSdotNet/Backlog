// c4-graph.mjs — puts the arc42 folder's C4 model into the reference graph and
// checks the chapters against it.
//
// The workspace, `.devbook/arc42/workspace.dsl`, is read with c4.mjs when it
// exists. Every element becomes an `element` node and every view a `view`
// node, both inside the arc42 scope; an element's parent `contains` it, and a
// DSL relationship is a `relationship` edge. A chapter reaches the model three
// ways: an image embed `![…](c4:<key>)` is an `embeds` edge from the section
// that holds it to the view, and the `c4` and `c4-touches` fields are edges of
// those names to the elements they name. A building block's `code` stays an
// attribute on its node; its folders are checked against the disk here.
//
// What `devbook-arc42.md` asks and this checks: the workspace reads clean (an
// unknown identifier, a duplicate one, an unresolved include, and a dynamic
// step no relationship backs are the reader's problems); every element has a
// one-line description; every embed and every field names a view or element
// the workspace defines; a view is embedded in exactly one section, in the
// chapter its kind belongs to; and a view no chapter embeds is a warning.

import { stat } from "node:fs/promises";
import path from "node:path";
import { readWorkspace } from "./c4.mjs";
import { C4_FIELDS, DEVBOOK_PREFIX, isRepoFolder } from "./metadata.mjs";

export const WORKSPACE = `${DEVBOOK_PREFIX}arc42/workspace.dsl`;

const ARC42 = `${DEVBOOK_PREFIX}arc42/`;
const BUILDING_BLOCK = /^building-blocks\/[^/]+\.md$/;

// Where each view kind is embedded, per the rule's table: the chapter, and for
// a building block's runtime the `## Runtime` section inside it.
const VIEW_PLACES = {
    systemLandscape: { chapters: ["03-context-and-scope.md"], says: "`03-context-and-scope.md`" },
    systemContext: { chapters: ["03-context-and-scope.md"], says: "`03-context-and-scope.md`" },
    container: { chapters: ["05-building-block-view.md"], block: true, says: "`05-building-block-view.md` or a `building-blocks/<slug>.md`" },
    component: { chapters: ["05-building-block-view.md"], block: true, says: "`05-building-block-view.md` or a `building-blocks/<slug>.md`" },
    dynamic: { chapters: ["06-runtime-view.md"], runtime: true, says: "`06-runtime-view.md` or a building block's `## Runtime`" },
    deployment: { chapters: ["07-deployment-view.md"], says: "`07-deployment-view.md`" },
};

// Kinds a description is asked of: every declared element. An instance is a
// placement of an element declared elsewhere and carries that one's.
const DESCRIBED = new Set(["person", "softwareSystem", "container", "component", "deploymentNode", "infrastructureNode"]);

const C4_EMBED = /!\[[^\]]*\]\(\s*<?c4:([^)\s>]*)>?(?:\s+"[^"]*")?\s*\)/gi;

export const elementNodeId = (id) => `c4:${id}`;
export const viewNodeId = (key) => `c4-view:${key}`;

/**
 * Every `c4:` image embed in one document outside a fence: its key, its line,
 * and the headings it sits under, innermost last.
 */
export function c4Embeds(raw, chapters) {
    const embeds = [];
    const lines = raw.split(/\r?\n/);
    const headings = [...chapters].sort((a, b) => a.line - b.line);
    let fence = null;
    let comment = false;
    for (let i = 0; i < lines.length; i++) {
        let line = lines[i];
        // An HTML comment hides what it holds, across lines too.
        if (comment) {
            const close = line.indexOf("-->");
            if (close < 0) continue;
            line = line.slice(close + 3);
            comment = false;
        }
        const marker = /^\s*(`{3,}|~{3,})/.exec(line);
        if (fence) {
            if (marker && marker[1][0] === fence[0] && marker[1].length >= fence.length) fence = null;
            continue;
        }
        if (marker) { fence = marker[1]; continue; }
        // An embed inside an inline code span is an example, not an embed.
        let prose = line.replace(/(`+)[\s\S]*?\1/g, "").replace(/<!--[\s\S]*?-->/g, "");
        const open = prose.indexOf("<!--");
        if (open >= 0) {
            prose = prose.slice(0, open);
            comment = true;
        }
        for (const match of prose.matchAll(C4_EMBED)) {
            const lineNumber = i + 1;
            const trail = [];
            for (const heading of headings) {
                if (heading.line > lineNumber) break;
                while (trail.length && trail[trail.length - 1].level >= heading.level) trail.pop();
                trail.push(heading);
            }
            embeds.push({ key: match[1], line: lineNumber, trail });
        }
    }
    return embeds;
}

async function kindOf(absolutePath) {
    try {
        const s = await stat(absolutePath);
        return s.isDirectory() ? "folder" : "file";
    } catch {
        return null;
    }
}

/**
 * The C4 part of the graph. `docs` is every indexed document as the graph
 * build read it, `nodes` the chapter and file nodes by id. Returns the nodes
 * and edges to add, and the problems.
 */
export async function c4Graph(repoRoot, docs, nodes) {
    const added = [];
    const edges = [];
    const problems = [];
    const report = (severity, at, message) => problems.push({ severity, path: at, message });

    let workspace = null;
    if (await kindOf(path.join(repoRoot, WORKSPACE)) === "file") {
        try {
            workspace = readWorkspace(WORKSPACE, { root: repoRoot });
        } catch (error) {
            report("error", WORKSPACE, `${WORKSPACE} could not be read (${error.message}).`);
        }
    }

    const elements = new Map(); // lower-cased id → element, named elements only
    const views = new Map(); // lower-cased key → view
    if (workspace) {
        for (const p of workspace.problems) {
            const file = p.file ?? WORKSPACE;
            report("error", file, `${file}:${p.line} ${p.message}`);
        }
        for (const element of workspace.elements) {
            if (!element.anonymous && !elements.has(element.id.toLowerCase())) elements.set(element.id.toLowerCase(), element);
            const node = {
                id: elementNodeId(element.id),
                label: element.name ?? element.id,
                type: "element",
                folder: "arc42",
                path: element.file,
                line: element.line,
                kind: element.kind,
                element: element.id,
            };
            for (const field of ["description", "technology", "environment", "instanceOf"]) {
                if (element[field] != null) node[field] = element[field];
            }
            if (element.tags.length) node.tags = element.tags;
            if (!nodes.has(node.id) && !added.some((n) => n.id === node.id)) added.push(node);

            if (DESCRIBED.has(element.kind)) {
                const description = element.description ?? "";
                if (!description.trim()) {
                    report("error", element.file, `${element.file}:${element.line} \`${element.id}\` has no description — give every element one line saying what it is.`);
                } else if (/[\r\n]/.test(description)) {
                    report("error", element.file, `${element.file}:${element.line} \`${element.id}\` has a description of more than one line — an element's description is one line; the chapter that embeds its view explains it.`);
                }
            }
        }
        for (const element of workspace.elements) {
            if (!element.parent) continue;
            edges.push({ id: `contains:${elementNodeId(element.parent)}->${elementNodeId(element.id)}`, source: elementNodeId(element.parent), target: elementNodeId(element.id), type: "contains" });
        }
        const seen = new Map();
        for (const r of workspace.relationships) {
            const base = `relationship:${elementNodeId(r.source)}->${elementNodeId(r.destination)}`;
            const n = (seen.get(base) ?? 0) + 1;
            seen.set(base, n);
            const edge = { id: n === 1 ? base : `${base}#${n}`, source: elementNodeId(r.source), target: elementNodeId(r.destination), type: "relationship" };
            if (r.description) edge.description = r.description;
            if (r.technology) edge.technology = r.technology;
            edges.push(edge);
        }
        for (const view of workspace.views) {
            if (!view.key || views.has(view.key.toLowerCase())) continue; // the reader reported it
            views.set(view.key.toLowerCase(), view);
            const node = {
                id: viewNodeId(view.key),
                label: view.title ?? view.key,
                type: "view",
                folder: "arc42",
                path: view.file,
                line: view.line,
                kind: view.kind,
                key: view.key,
            };
            if (view.scope) node.scope = view.scope;
            if (view.environment) node.environment = view.environment;
            if (view.description) node.description = view.description;
            added.push(node);
        }
    }

    // ── embeds ──
    const embedded = new Map(); // view key → [{ at, source }]
    for (const { relPath, raw, chapters, fileMeta } of docs) {
        for (const embed of c4Embeds(raw, chapters)) {
            const at = `${relPath}:${embed.line}`;
            if (!workspace) {
                report("error", relPath, `${at} embeds \`c4:${embed.key}\`, but there is no ${WORKSPACE} to define the view.`);
                continue;
            }
            const view = views.get(embed.key.toLowerCase());
            if (!view) {
                report("error", relPath, `${at} embeds \`c4:${embed.key}\`, which is no view key in ${WORKSPACE}.`);
                continue;
            }
            // The section is the innermost heading that is a node; a structural
            // heading under it, or none, falls back to the chapter or file.
            let source = relPath;
            for (let k = embed.trail.length - 1; k >= 0; k--) {
                const heading = embed.trail[k];
                const id = heading.level === 1 ? relPath : `${relPath}#${heading.slug}`;
                if (nodes.has(id)) { source = id; break; }
            }
            const list = embedded.get(view.key) ?? [];
            list.push({ at, source, relPath });
            embedded.set(view.key, list);
            edges.push({ id: `embeds:${source}->${viewNodeId(view.key)}${list.length > 1 ? `#${list.length}` : ""}`, source, target: viewNodeId(view.key), type: "embeds" });

            const place = VIEW_PLACES[view.kind];
            const subject = relPath.startsWith(ARC42) ? relPath.slice(ARC42.length) : null;
            // The building-blocks index lists the blocks; it is none of them.
            const inBlock = subject !== null && BUILDING_BLOCK.test(subject) && fileMeta?.index !== "root";
            const underRuntime = embed.trail.some((h) => h.level === 2 && h.slug === "runtime");
            const fits =
                subject !== null &&
                (place.chapters.includes(subject) || (place.block && inBlock) || (place.runtime && inBlock && underRuntime));
            if (!fits) {
                report("error", relPath, `${at} embeds the \`${view.kind}\` view \`${view.key}\`, which belongs in ${place.says} — any other chapter links to the section that embeds it.`);
            }
        }
    }
    for (const [key, list] of embedded) {
        if (list.length < 2) continue;
        report("error", list[1].relPath, `View \`${key}\` is embedded ${list.length} times (${list.map((e) => e.at).join(", ")}) — embed each view in exactly one section and link to it from the others.`);
    }
    for (const view of views.values()) {
        if (embedded.has(view.key)) continue;
        report("warning", view.file, `${view.file}:${view.line} view \`${view.key}\` is embedded in no chapter — embed it as \`![…](c4:${view.key})\` in the chapter its kind belongs to, or remove it.`);
    }

    // ── fields ──
    for (const node of nodes.values()) {
        if (node.folder !== "arc42" && node.folder !== "domain") continue;
        for (const field of C4_FIELDS) {
            for (const entry of new Set(node[field] ?? [])) {
                if (typeof entry !== "string") continue;
                if (!workspace) {
                    report("error", node.path, `${node.id} has \`${field}\` entry "${entry}", but there is no ${WORKSPACE} to define it.`);
                    continue;
                }
                const element = elements.get(entry.toLowerCase());
                if (!element) {
                    report("error", node.path, `${node.id} has \`${field}\` entry "${entry}", which is no element in ${WORKSPACE}.`);
                    continue;
                }
                edges.push({ id: `${field}:${node.id}->${elementNodeId(element.id)}`, source: node.id, target: elementNodeId(element.id), type: field });
            }
        }
        for (const entry of node.code ?? []) {
            if (!isRepoFolder(entry)) continue; // the lint reported it
            const found = await kindOf(path.join(repoRoot, String(entry).replace(/\\/g, "/")));
            if (found === "file") {
                report("error", node.path, `${node.id} has \`code\` entry "${entry}", which is a file — \`code\` lists folders, never files.`);
            } else if (found === null) {
                report("warning", node.path, `${node.id} has \`code\` entry "${entry}", and no such folder exists — the block and its code have drifted apart.`);
            }
        }
    }

    return { nodes: added, edges, problems };
}
