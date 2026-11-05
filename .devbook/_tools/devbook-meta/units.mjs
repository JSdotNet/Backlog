// units.mjs — list the sync units of a devbook and the groups they join into.
//
// A sync unit is what one verify pass covers and one pull request changes: an
// aggregate with everything it owns and every rule that names it, a domain
// service the same way, a feature, a switch, a user or technical actor, a
// building block, a design component, and one shared-types unit per context. A group is the units a
// requirement naming two aggregates and no feature ties together. The rules
// are "Sync units" in devbook-chapter-metadata.md. Which units a sweep levels,
// and which way, is not the devbook's: since contract 38 no chapter carries a
// direction, and the tool that runs the sweep keeps it.
//
// Deterministic: the same corpus prints the same output, sorted by id. It
// reads the graph `buildGraph` builds, so membership comes from headings,
// `type`, file names, and `related` — never from prose.
//
// Dependency-free ESM against node built-ins, like everything else here.

import path from "node:path";
import { pathToFileURL } from "node:url";

import { WORKSPACE } from "./c4-graph.mjs";
import { buildGraph } from "./graph.mjs";
import { slugify } from "./metadata.mjs";

/** A group past this many chapters is set aside rather than split. */
export const DEFAULT_MAX_GROUP_CHAPTERS = 40;

/** The version of the JSON this tool prints. */
export const UNITS_SCHEMA_VERSION = 2;

// The unit kinds, by the `type` of their root chapter in `domain/`. A `user`
// and a `technical` chapter run as the `actor` converter kind; an
// `organisation` roots no unit and stays context.
const DOMAIN_ROOT_KINDS = ["aggregate", "domain-service", "feature", "feature-flag", "setting", "user", "technical"];

// The roots a requirement or an event can name to belong to — or, two of them
// with no feature, to join.
const MODEL_KINDS = ["aggregate", "domain-service"];
const FEATURE_KINDS = ["feature", "sub-feature"];
const SHARED_KINDS = ["shared-value-objects", "shared-enums"];


// The grouping chapters of the behaviour pages, and the kinds each names in
// `related` as the unit its chapters belong to. It may link onward to others.
const PAIRED_KINDS = {
    invariants: [...MODEL_KINDS, ...SHARED_KINDS],
    requirements: FEATURE_KINDS,
};
const PAIRING_KINDS = Object.keys(PAIRED_KINDS);

// Owned chapters that belong to the unit they sit under.
const NESTED_KINDS = ["entity", "value-object", "enum", "invariant", "sub-feature"];

const asList = (value) => (value == null ? [] : Array.isArray(value) ? value : [value]);
const fold = (name) => String(name).trim().toLowerCase();
const byId = (a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
const sorted = (values) => [...values].sort();

/** The context folder a domain path sits in, or null outside one. */
function contextOf(relPath) {
    const parts = String(relPath).split("/");
    return parts[1] === "domain" && parts.length === 4 ? parts.slice(0, 3).join("/") : null;
}

/**
 * Every sync unit in a graph, the chapters that belong to none, and the
 * requirements that tie units together. Pure: takes what `buildGraph`
 * returns.
 */
export function collectUnits(graph) {
    const nodes = new Map(graph.nodes.map((node) => [node.id, node]));
    const roots = graph.unitRoots ?? new Set();
    const parent = new Map();
    for (const edge of graph.edges) {
        if (edge.type === "contains") parent.set(edge.target, edge.source);
    }

    const units = new Map();
    const owner = new Map(); // chapter id -> unit id
    const orphans = [];
    const ties = []; // { requirement, units }

    const addUnit = (unit) => units.set(unit.id, { ...unit, chapters: new Set([unit.root]), files: new Set() });
    const assign = (chapterId, unitId) => {
        owner.set(chapterId, unitId);
        units.get(unitId).chapters.add(chapterId);
    };

    // 1. The roots, one unit each.
    for (const node of nodes.values()) {
        if (!roots.has(node.id)) continue;
        if (node.folder === "domain" && DOMAIN_ROOT_KINDS.includes(node.kind)) {
            addUnit({ id: node.id, kind: node.kind, root: node.id, label: node.label, context: contextOf(node.path) });
        } else if (node.folder === "arc42" && node.type === "file") {
            addUnit({ id: node.id, kind: "building-block", root: node.id, label: node.label, context: null });
        } else if (node.folder === "design" && node.type === "chapter") {
            addUnit({ id: node.id, kind: "design-component", root: node.id, label: node.label, context: null });
        }
    }
    // One shared-types unit per context that has a shared grouping.
    const sharedUnit = new Map(); // context -> unit id
    for (const node of nodes.values()) {
        if (node.type !== "chapter" || node.folder !== "domain" || !SHARED_KINDS.includes(node.kind)) continue;
        const context = contextOf(node.path);
        if (!context) continue;
        let id = sharedUnit.get(context);
        if (!id) {
            id = `${context}#shared-types`;
            sharedUnit.set(context, id);
            addUnit({ id, kind: "shared-types", root: node.id, label: `Shared types (${path.posix.basename(context)})`, context });
        } else if (node.id < units.get(id).root) {
            units.get(id).root = node.id;
        }
        assign(node.id, id);
    }

    // The unit a chapter id stands for when something names it: a root, a
    // sub-feature's feature, a shared grouping's shared-types unit.
    const unitFor = (ref) => {
        if (units.has(ref) && units.get(ref).kind !== "shared-types") return ref;
        const node = nodes.get(ref);
        if (node?.kind === "sub-feature" || SHARED_KINDS.includes(node?.kind)) return owner.get(ref) ?? nestedUnit(ref);
        return null;
    };
    // The one unit a pairing chapter names, or null for none or two.
    const pairedUnit = (node) => {
        const kinds = PAIRED_KINDS[node.kind];
        const named = new Set(
            asList(node.related).filter((ref) => kinds.includes(nodes.get(ref)?.kind)).map(unitFor).filter(Boolean)
        );
        return named.size === 1 ? [...named][0] : null;
    };
    // The unit a chapter sits under, by heading nesting: the nearest ancestor
    // that is a root, or a pairing chapter whose `related` names one.
    const nestedUnit = (id) => {
        for (let at = parent.get(id); at; at = parent.get(at)) {
            const node = nodes.get(at);
            if (owner.has(at)) return owner.get(at);
            if (units.has(at) && units.get(at).kind !== "shared-types") return at;
            if (PAIRING_KINDS.includes(node?.kind)) return pairedUnit(node);
        }
        return null;
    };

    // 2. A building block holds every chapter of its file, its row in the
    // building block view, and — where arc42 has one — the workspace with the
    // files it includes, per "The workspace is part of the kind" in
    // spec-kinds/building-block.md; a design component every chapter under it.
    const workspaceFiles = new Set();
    for (const node of nodes.values()) {
        if (node.folder === "arc42" && (node.type === "element" || node.type === "view") && node.path) workspaceFiles.add(node.path);
    }
    if (workspaceFiles.size) workspaceFiles.add(WORKSPACE);
    for (const unit of units.values()) {
        if (unit.kind === "building-block") {
            unit.files.add(".devbook/arc42/05-building-block-view.md");
            for (const file of workspaceFiles) unit.files.add(file);
            for (const node of nodes.values()) {
                if (node.type === "chapter" && node.path === unit.root) assign(node.id, unit.id);
            }
        }
    }

    // 3. Owned chapters, in the order the rules give.
    const chapters = [...nodes.values()].filter((node) => node.type === "chapter" && !owner.has(node.id)).sort(byId);
    const orphan = (node, reason) => orphans.push({ id: node.id, kind: node.kind ?? null, reason });

    // Pairing chapters first, so the chapters under them can follow them.
    for (const node of chapters) {
        if (node.folder !== "domain" || !PAIRING_KINDS.includes(node.kind)) continue;
        const unit = pairedUnit(node);
        if (unit) assign(node.id, unit);
        else orphan(node, "names no one unit it pairs with in `related`");
    }
    for (const node of chapters) {
        if (owner.has(node.id) || units.has(node.id)) continue;
        if (node.folder === "design") {
            const unit = nestedUnit(node.id);
            if (unit) assign(node.id, unit);
            continue;
        }
        if (node.folder !== "domain") continue;
        if (node.kind === "domain-event") {
            const raisers = new Set(
                asList(node.related).filter((ref) => MODEL_KINDS.includes(nodes.get(ref)?.kind)).map(unitFor).filter(Boolean)
            );
            if (raisers.size === 1) assign(node.id, [...raisers][0]);
            else orphan(node, raisers.size ? "names two raisers in `related`" : "names no raiser in `related`");
            continue;
        }
        if (node.kind === "requirement") {
            const named = asList(node.related).map((ref) => nodes.get(ref)).filter(Boolean);
            const features = new Set(named.filter((t) => FEATURE_KINDS.includes(t.kind)).map((t) => unitFor(t.id)).filter(Boolean));
            const models = new Set(named.filter((t) => MODEL_KINDS.includes(t.kind)).map((t) => t.id).filter((id) => units.has(id)));
            if (features.size === 1) assign(node.id, [...features][0]);
            else if (features.size > 1) orphan(node, "names two features in `related`");
            else if (models.size) {
                const all = sorted(models);
                assign(node.id, all[0]);
                if (all.length > 1) ties.push({ requirement: node.id, units: all });
            } else {
                const unit = nestedUnit(node.id);
                if (unit) assign(node.id, unit);
                else orphan(node, "names nothing it belongs to");
            }
            continue;
        }
        if (NESTED_KINDS.includes(node.kind)) {
            const unit = nestedUnit(node.id);
            if (unit) assign(node.id, unit);
            else orphan(node, "sits under no unit");
        }
    }

    // 4. Terms, by the counterpart ladder's first rung: a term's name or alias
    // that is the name or alias of a chapter in exactly one unit.
    const spellings = new Map(); // folded spelling -> Set of unit ids
    for (const [chapterId, unitId] of owner) {
        const node = nodes.get(chapterId);
        for (const spelling of [node.label, ...asList(node.aliases)]) {
            const key = fold(spelling);
            if (!spellings.has(key)) spellings.set(key, new Set());
            spellings.get(key).add(unitId);
        }
    }
    for (const unit of units.values()) {
        const node = nodes.get(unit.root);
        for (const spelling of [node.label, ...asList(node.aliases)]) {
            const key = fold(spelling);
            if (!spellings.has(key)) spellings.set(key, new Set());
            spellings.get(key).add(unit.id);
        }
    }
    for (const node of chapters) {
        if (node.folder !== "domain" || node.kind !== "term" || owner.has(node.id)) continue;
        const homes = new Set();
        for (const spelling of [node.label, ...asList(node.aliases)]) {
            for (const unitId of spellings.get(fold(spelling)) ?? []) homes.add(unitId);
        }
        if (homes.size === 1) assign(node.id, [...homes][0]);
        else if (homes.size > 1) orphan(node, `its aliases resolve into ${homes.size} units: ${sorted(homes).join(", ")}`);
    }

    // 5. Files, and the final shape.
    const list = [...units.values()].map((unit) => {
        const chapterIds = sorted(unit.chapters);
        for (const id of chapterIds) unit.files.add(id.split("#")[0]);
        return {
            id: unit.id,
            kind: unit.kind,
            label: unit.label,
            context: unit.context,
            chapters: chapterIds,
            files: sorted(unit.files),
        };
    });
    list.sort(byId);
    orphans.sort(byId);
    ties.sort((a, b) => (a.requirement < b.requirement ? -1 : 1));

    return { units: list, orphans, ties };
}

/**
 * Join units into groups: the connected sets a requirement naming two
 * aggregates and no feature ties together. Links never join. A group that
 * holds more than `maxGroupChapters` chapters is set aside.
 */
export function groupUnits({ units, ties }, { maxGroupChapters = DEFAULT_MAX_GROUP_CHAPTERS } = {}) {
    const root = new Map(units.map((unit) => [unit.id, unit.id]));
    const find = (id) => {
        while (root.get(id) !== id) id = root.get(id);
        return id;
    };
    for (const tie of ties) {
        const [first, ...rest] = tie.units.map(find);
        for (const other of rest) {
            const [a, b] = first < other ? [first, other] : [other, first];
            root.set(b, a);
        }
    }

    const members = new Map();
    for (const unit of units) {
        const key = find(unit.id);
        if (!members.has(key)) members.set(key, []);
        members.get(key).push(unit);
    }

    const groups = [...members.values()].map((group) => {
        group.sort(byId);
        const ids = group.map((unit) => unit.id);
        const tying = ties.filter((tie) => ids.includes(tie.units[0])).map((tie) => tie.requirement);
        const chapters = group.reduce((sum, unit) => sum + unit.chapters.length, 0);
        const setAside = chapters > maxGroupChapters ? "oversized" : null;
        const lead = group[0];
        return {
            id: lead.id,
            slug: slugify([lead.context ? path.posix.basename(lead.context) : null, lead.label].filter(Boolean).join(" ")),
            setAside,
            chapters,
            ties: tying,
            units: group,
        };
    });
    return groups.sort(byId);
}

/**
 * The tool's whole answer for one repository: units or groups, with the
 * set-asides and the orphans.
 */
export async function listUnits(repoRoot, { groups = false, maxGroupChapters = DEFAULT_MAX_GROUP_CHAPTERS } = {}) {
    const graph = await buildGraph(repoRoot);
    const collected = collectUnits(graph);
    const result = { version: UNITS_SCHEMA_VERSION, maxGroupChapters };
    if (!groups) {
        result.units = collected.units;
    } else {
        const all = groupUnits(collected, { maxGroupChapters });
        result.groups = all.filter((group) => !group.setAside);
        result.setAside = all.filter((group) => group.setAside);
    }
    result.orphans = collected.orphans;
    return result;
}

/** The one-line summary a commit body or a brief can quote. */
export function summaryLine(result) {
    const orphans = `${result.orphans.length} orphan${result.orphans.length === 1 ? "" : "s"}`;
    if (!result.groups) return `${result.units.length} unit${result.units.length === 1 ? "" : "s"}; ${orphans}.`;
    const units = result.groups.reduce((sum, group) => sum + group.units.length, 0);
    const aside = result.setAside.length ? ` (${result.setAside.length} oversized)` : "";
    return `${result.groups.length} group${result.groups.length === 1 ? "" : "s"} of ${units} unit${units === 1 ? "" : "s"}; ${result.setAside.length} set aside${aside}; ${orphans}.`;
}

function unitLine(unit, indent = "") {
    return `${indent}${unit.kind.padEnd(16)} ${unit.id}  ${unit.chapters.length} chapter${unit.chapters.length === 1 ? "" : "s"}`;
}

/** Plain-text rendering of `listUnits`'s result. */
export function renderText(result) {
    const lines = [summaryLine(result), ""];
    if (result.units) {
        for (const unit of result.units) lines.push(unitLine(unit));
    } else {
        for (const group of result.groups) {
            lines.push(`${group.id}  ${group.chapters} chapters`);
            for (const unit of group.units) lines.push(unitLine(unit, "  "));
        }
        if (result.setAside.length) {
            lines.push("", "Set aside:");
            for (const group of result.setAside) {
                const why = `${group.chapters} chapters, past ${result.maxGroupChapters}`;
                lines.push(`${group.id}  ${why}${group.ties.length ? `; tied by ${group.ties.join(", ")}` : ""}`);
                for (const unit of group.units) lines.push(unitLine(unit, "  "));
            }
        }
    }
    if (result.orphans.length) {
        lines.push("", "Orphans:");
        for (const orphan of result.orphans) lines.push(`${orphan.id}  ${orphan.kind ?? "untyped"}: ${orphan.reason}`);
    }
    return lines.join("\n");
}

const USAGE = `units.mjs — the sync units of a devbook and the groups they join into

  node units.mjs                        every unit and its chapters
  node units.mjs --groups               the groups units join into, and the set-asides
  node units.mjs --json                 the same as JSON
  node units.mjs --max-group-chapters N set a group past N chapters aside (default 40)
  node units.mjs --root <path>          a repository other than the working directory`;

export async function main(argv) {
    const options = { groups: false, json: false, maxGroupChapters: DEFAULT_MAX_GROUP_CHAPTERS, root: process.cwd() };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === "--help" || arg === "-h") {
            console.log(USAGE);
            return 0;
        } else if (arg === "--groups") options.groups = true;
        else if (arg === "--json") options.json = true;
        else if (arg === "--max-group-chapters") {
            const value = argv[++i];
            if (!/^\d+$/.test(value ?? "")) {
                console.error(`--max-group-chapters takes a whole number; got "${value ?? ""}".`);
                return 1;
            }
            options.maxGroupChapters = Number(value);
        } else if (arg === "--root") options.root = path.resolve(argv[++i] ?? ".");
        else {
            console.error(`Unknown argument "${arg}".\n\n${USAGE}`);
            return 1;
        }
    }
    const result = await listUnits(options.root, options);
    console.log(options.json ? JSON.stringify(result, null, 2) : renderText(result));
    return 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    process.exit(await main(process.argv.slice(2)));
}
