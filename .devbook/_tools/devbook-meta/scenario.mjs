// scenario.mjs — the corpus-wide rules of scenario pages, which no single
// document can check on its own: the stem register, the setup fields resolved
// against the chapters and the profile file, `scenario:` image references, the
// `Proved by:` pointers from requirement cases, the parts nothing points at,
// and the `e2e` test entry each pointer derives.
//
// The per-document rules — where the type may sit, the status, the step
// keywords, the field shapes — are validateDocument's, in metadata.mjs. This
// module runs once per graph build, over documents that build already read.
//
// The same pass yields the register `scenarios.json` is written from: every
// page with its parts, its screenshot labels, its setup fields, and the
// requirement cases that point at each part — so a reader never parses the
// corpus to find them.

import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import {
    DEVBOOK_PREFIX,
    folderKindForPath,
    isScenarioPage,
    proseLinks,
    provedByCases,
    scenarioStem,
    splitPortal,
    SCENARIO_SETUP_FIELDS,
} from "./metadata.mjs";

/** Where a repository keeps the profiles its scenario pages name. */
export const SCENARIO_PROFILES = ".devbook/scenarios/profiles.json";

// The chapter kinds each referencing setup field must resolve to.
const ACTOR_KINDS = ["user", "organisation", "technical"];

// A spec that implements a scenario page says so in its first lines, and that
// header is what derives a requirement's `e2e` entry. The runner is the one the
// derived specs are written for.
const SPEC_FILE = /\.(?:spec|test)\.[cm]?[jt]sx?$/;
const SPEC_HEADER = /^\s*\/\/\s*scenario:\s*(\S+)\s*$/m;
const SPEC_HEADER_LINES = 10;
const SPEC_SKIP_DIRS = new Set(["node_modules", "bin", "obj", "dist", "build", "out", "coverage"]);
const DERIVED_RUNNER = "playwright";

/**
 * Every corpus-wide scenario problem, and the `tests` entries derived for the
 * requirements a `Proved by:` line connects to a part with a spec.
 *
 * `docs` is every indexed document as `{ relPath, raw, chapters, fileMeta }`;
 * `nodes` the graph's node map, which already holds every chapter with its
 * `kind`. Returns `{ problems, derivedTests, register }`, `derivedTests` a
 * map from a requirement's id to the entries derived for it, `register` every
 * scenario page as `scenarios.json` lists it — see `registerEntry`.
 */
export async function scenarioProblems(repoRoot, docs, nodes) {
    const problems = [];
    const report = (severity, at, message) => problems.push({ severity, path: at, message: `${at} ${message}` });

    // ── The register: every scenario page by its stem ──────────────────────
    const pages = new Map();
    const byStem = new Map();
    for (const doc of docs) {
        if (!isScenarioPage(doc.relPath, doc.fileMeta)) continue;
        const parts = new Map();
        const partLines = [];
        for (const chapter of doc.chapters) {
            if (chapter.level !== 2 || parts.has(chapter.slug)) continue;
            parts.set(chapter.slug, chapter.text);
            partLines.push({ line: chapter.line, slug: chapter.slug });
        }
        // Each screenshot point, in page order, with the part it sits in: the
        // nearest `##` above it, or none before the first.
        const shots = new Set();
        const labels = [];
        for (const link of proseLinks(doc.raw)) {
            const shot = /^shot:(.+)$/i.exec(link.target);
            if (!shot) continue;
            shots.add(shot[1]);
            const part = partLines.filter((entry) => entry.line < link.line).pop()?.slug ?? null;
            labels.push({ label: shot[1], part });
        }
        const stem = scenarioStem(doc.relPath);
        const title = doc.chapters.find((chapter) => chapter.level === 1)?.text ?? stem;
        pages.set(doc.relPath, { stem, title, parts, shots, labels, meta: doc.fileMeta ?? {}, provedBy: new Map() });
        byStem.set(stem, [...(byStem.get(stem) ?? []), doc.relPath]);
    }
    for (const [stem, paths] of byStem) {
        if (paths.length < 2) continue;
        for (const relPath of paths.slice(1)) {
            report(
                "error",
                relPath,
                `has the stem "${stem}" of ${paths[0]} — a scenario page's file name is its identity for every tool, so it is unique across every bounded context. Rename one.`
            );
        }
    }

    // ── The setup fields, resolved ─────────────────────────────────────────
    const profiles = await readProfiles(repoRoot);
    if (profiles.error) problems.push({ severity: "error", path: SCENARIO_PROFILES, message: `${SCENARIO_PROFILES} ${profiles.error}` });
    for (const [relPath, page] of pages) {
        const meta = page.meta;
        const chapterOf = (entry, field, kinds) => {
            const ref = resolveSetupRef(relPath, entry, nodes);
            const node = nodes.get(ref);
            if (!node) {
                report("error", relPath, `has \`${field}\` entry "${entry}" that does not resolve to a chapter — written as a path from the page, from its bounded context, or from the repository root.`);
            } else if (!kinds.includes(node.kind)) {
                report("error", relPath, `has \`${field}\` entry "${entry}" that resolves to ${node.kind ? `a \`${node.kind}\` chapter` : "a heading or file"}, not ${kinds.map((kind) => `a \`${kind}\``).join(" or ")} chapter.`);
            }
        };
        const portals = [];
        for (const entry of asList(meta.actor)) {
            const { portal, rest } = splitPortal(entry);
            if (portal) portals.push({ field: "actor", portal });
            if (rest.includes("#")) chapterOf(rest, "actor", ACTOR_KINDS);
        }
        for (const entry of asList(meta.flags)) {
            const ref = String(entry).replace(/^-/, "");
            if (ref.includes("#")) chapterOf(ref, "flags", ["feature-flag"]);
        }
        for (const entry of asList(meta.settings)) {
            const at = String(entry).indexOf("=");
            const ref = at > 0 ? String(entry).slice(0, at) : String(entry);
            if (ref.includes("#")) chapterOf(ref, "settings", ["setting"]);
        }
        for (const entry of asList(meta.start)) {
            const { portal } = splitPortal(entry);
            if (portal) portals.push({ field: "start", portal });
        }

        if (!profiles.map) continue;
        const named = typeof meta.profile === "string" ? meta.profile : null;
        if (named && !(named in profiles.map)) {
            report("error", relPath, `has \`profile\` "${named}", which ${SCENARIO_PROFILES} does not define — profiles: ${Object.keys(profiles.map).join(", ") || "none"}.`);
            continue;
        }
        const profileName = named ?? "default";
        const profile = profiles.map[profileName];
        if (!profile) continue;
        const defined = Object.keys(profile?.portals ?? {});
        for (const { field, portal } of portals) {
            if (defined.includes(portal)) continue;
            report("error", relPath, `names portal "${portal}" in \`${field}\`, which profile "${profileName}" does not define — portals: ${defined.join(", ") || "none"}.`);
        }
    }

    // ── `scenario:` references, from any page ──────────────────────────────
    for (const doc of docs) {
        for (const link of proseLinks(doc.raw)) {
            const match = /^scenario:(.*)$/i.exec(link.target);
            if (!match) continue;
            const [stem, label] = match[1].split("#");
            const at = `${doc.relPath}:${link.line}`;
            const found = byStem.get(stem) ?? [];
            if (!stem || !label) {
                problems.push({ severity: "error", path: doc.relPath, message: `${at} shows "${link.target}" — a screenshot elsewhere is \`scenario:<stem>#<label>\`.` });
            } else if (!found.length) {
                problems.push({ severity: "error", path: doc.relPath, message: `${at} shows "${link.target}", but no scenario page has the stem "${stem}".` });
            } else if (found.length === 1 && !pages.get(found[0]).shots.has(label)) {
                problems.push({ severity: "warning", path: doc.relPath, message: `${at} shows "${link.target}", but ${found[0]} has no screenshot point \`shot:${label}\`.` });
            }
        }
    }

    // ── `Proved by:` pointers, and the parts nobody claims ─────────────────
    const claimed = new Set();
    const derivedTests = new Map();
    let specs = null;
    for (const doc of docs) {
        for (const scenarioCase of provedByCases(doc.relPath, doc.raw)) {
            for (const pointer of scenarioCase.pointers) {
                const hash = pointer.target.indexOf("#");
                if (hash <= 0) continue; // malformed — validateDocument reports it
                const pagePart = pointer.target.slice(0, hash);
                const part = pointer.target.slice(hash + 1);
                const at = `${doc.relPath}:${pointer.line}`;
                const resolved = resolvePage(doc.relPath, pagePart, pages, byStem);
                if (resolved.error) {
                    problems.push({ severity: "error", path: doc.relPath, message: `${at} has \`Proved by: ${pointer.target}\`, ${resolved.error}` });
                    continue;
                }
                const page = pages.get(resolved.path);
                if (!page.parts.has(part)) {
                    problems.push({
                        severity: "error",
                        path: doc.relPath,
                        message: `${at} has \`Proved by: ${pointer.target}\`, but ${resolved.path} has no part "#${part}" — parts: ${[...page.parts.keys()].join(", ") || "none"}.`,
                    });
                    continue;
                }
                claimed.add(`${resolved.path}#${part}`);
                // The case by its own name, without the `Scenario:` keyword.
                const caseName = scenarioCase.heading.replace(/^\s*Scenario:\s*/i, "");
                const cases = page.provedBy.get(part) ?? [];
                if (!cases.some((entry) => entry.requirement === scenarioCase.requirement && entry.case === caseName)) {
                    cases.push({ requirement: scenarioCase.requirement, case: caseName });
                }
                page.provedBy.set(part, cases);
                specs ??= await specIndex(repoRoot);
                const spec = specs.get(resolved.path);
                if (spec) {
                    const entry = `e2e:${DERIVED_RUNNER}:${spec}#${page.parts.get(part)}`;
                    const list = derivedTests.get(scenarioCase.requirement) ?? [];
                    if (!list.includes(entry)) list.push(entry);
                    derivedTests.set(scenarioCase.requirement, list);
                }
            }
        }
    }
    for (const [relPath, page] of pages) {
        for (const slug of page.parts.keys()) {
            if (claimed.has(`${relPath}#${slug}`)) continue;
            report(
                "warning",
                relPath,
                `part "#${slug}" is an unclaimed journey — no requirement's \`#### Scenario:\` points at it with \`Proved by: ${scenarioStem(relPath)}.md#${slug}\`.`
            );
        }
    }

    const register = [...pages].map(([relPath, page]) => registerEntry(relPath, page));
    register.sort((a, b) => a.stem.localeCompare(b.stem, "en") || a.path.localeCompare(b.path, "en"));
    return { problems, derivedTests, register };
}

/**
 * One page as the register lists it: its stem and path, its title and the
 * status it declares, the six setup fields as written — `start` and `profile`
 * a string or null, the other four lists — its screenshot labels in page
 * order with the part each sits in, and its parts in page order, each with
 * the labels under it and every requirement case whose `Proved by:` names it.
 */
function registerEntry(relPath, page) {
    const meta = page.meta;
    const setup = {};
    for (const field of SCENARIO_SETUP_FIELDS) {
        const single = field === "start" || field === "profile";
        const value = meta[field];
        setup[field] = single ? (value == null || Array.isArray(value) ? null : String(value)) : asList(value).map(String);
    }
    return {
        stem: page.stem,
        path: relPath,
        title: page.title,
        status: typeof meta.status === "string" ? meta.status : null,
        setup,
        labels: page.labels.map((entry) => ({ ...entry })),
        parts: [...page.parts].map(([anchor, title]) => ({
            title,
            anchor,
            id: `${relPath}#${anchor}`,
            labels: page.labels.filter((entry) => entry.part === anchor).map((entry) => entry.label),
            provedBy: [...(page.provedBy.get(anchor) ?? [])].sort(
                (a, b) => a.requirement.localeCompare(b.requirement, "en") || a.case.localeCompare(b.case, "en")
            ),
        })),
    };
}

function asList(value) {
    if (value === null || value === undefined) return [];
    return Array.isArray(value) ? value : [value];
}

/** The bounded context folder a domain path sits in, or null at the folder root. */
function contextRoot(relPath) {
    const segments = String(relPath).split("/");
    const domain = `${DEVBOOK_PREFIX}domain`.split("/").length;
    return segments.length > domain + 1 ? segments.slice(0, domain + 1).join("/") : null;
}

/**
 * A setup field's chapter reference as a node id: a repository path as
 * written, else the path from the page's own folder, else from its bounded
 * context — the first whose file the graph holds.
 */
function resolveSetupRef(pagePath, entry, nodes) {
    const ref = String(entry).trim();
    if (ref.startsWith(DEVBOOK_PREFIX)) return ref;
    const candidates = [path.posix.join(path.posix.dirname(pagePath), ref)];
    const context = contextRoot(pagePath);
    if (context) candidates.push(path.posix.join(context, ref));
    return candidates.find((candidate) => nodes.has(candidate.split("#")[0])) ?? candidates[0];
}

/**
 * The scenario page a `Proved by:` names: a repository path, a path from the
 * requirement's folder, or a bare `<stem>.md` looked up in the register.
 */
function resolvePage(fromPath, pagePart, pages, byStem) {
    if (!pagePart.endsWith(".md")) return { error: "which names no `.md` page." };
    let target;
    if (pagePart.startsWith(DEVBOOK_PREFIX)) target = pagePart;
    else if (pagePart.includes("/")) target = path.posix.join(path.posix.dirname(fromPath), pagePart);
    else {
        const found = byStem.get(pagePart.slice(0, -3)) ?? [];
        if (found.length > 1) return { error: `but the stem is ambiguous — ${found.join(", ")}.` };
        if (!found.length) return { error: `but no scenario page has the stem "${pagePart.slice(0, -3)}".` };
        target = found[0];
    }
    if (!pages.has(target)) {
        return { error: folderKindForPath(target) ? `but ${target} is not a scenario page.` : `but ${target} is not in the devbook.` };
    }
    return { path: target };
}

/** The repository's profiles, `{ map }` when the file exists and parses, `{}` when it does not exist. */
async function readProfiles(repoRoot) {
    let text;
    try {
        text = await readFile(path.join(repoRoot, SCENARIO_PROFILES), "utf8");
    } catch {
        return {};
    }
    try {
        const map = JSON.parse(text);
        if (!map || typeof map !== "object" || Array.isArray(map)) return { error: "is not a JSON object of profiles by name." };
        return { map };
    } catch (error) {
        return { error: `does not parse: ${error.message}` };
    }
}

/**
 * Every spec in the repository that names the scenario page it implements, as
 * a map from the page's path to the spec's. Read only when a pointer resolves,
 * so a corpus without one never walks the tree.
 */
async function specIndex(repoRoot) {
    const index = new Map();
    const found = [];
    async function walk(current) {
        let entries;
        try {
            entries = await readdir(path.join(repoRoot, current), { withFileTypes: true });
        } catch {
            return;
        }
        for (const entry of entries) {
            const child = current ? `${current}/${entry.name}` : entry.name;
            if (entry.isDirectory()) {
                if (entry.name.startsWith(".") || SPEC_SKIP_DIRS.has(entry.name)) continue;
                await walk(child);
            } else if (entry.isFile() && SPEC_FILE.test(entry.name)) {
                found.push(child);
            }
        }
    }
    await walk("");
    for (const spec of found.sort()) {
        let head;
        try {
            head = (await readFile(path.join(repoRoot, spec), "utf8")).split(/\r?\n/).slice(0, SPEC_HEADER_LINES).join("\n");
        } catch {
            continue;
        }
        const match = SPEC_HEADER.exec(head);
        if (match && !index.has(match[1])) index.set(match[1], spec);
    }
    return index;
}
