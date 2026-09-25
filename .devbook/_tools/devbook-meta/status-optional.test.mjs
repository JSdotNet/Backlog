// Exercises the per-folder `status` rule: optional in the editorial folders,
// where absence means the resting value `active`, and still mandatory in the
// three folders whose `status` is a rating or a work state.
//
// The load-bearing case is the empty `meta` fence. `.arc42` and `.design`
// define no `type`, so a settled chapter with no relations has nothing left to
// write — and the fence is the only thing marking that heading as an
// addressable chapter. If an empty block ever stops producing a graph node,
// hundreds of chapters silently leave the graph.
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { parseDocument, validateDocument, resolveStatus, restingStatusFor } from "./metadata.mjs";
import { buildGraph, buildGraphDocument } from "./graph.mjs";
import { buildOutlineDocument } from "./outline.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};

const fence = (body) => "```meta\n" + body + "```\n";
const counts = (issues) => ({
    errors: issues.filter((i) => i.severity === "error").length,
    warnings: issues.filter((i) => i.severity === "warning").length,
});

// ── The resolution helper ───────────────────────────────────────────────────

for (const folder of ["domain", "arc42", "design"]) {
    check(restingStatusFor(folder) === "active", `.${folder} rests at active`);
    const absent = resolveStatus(folder, {});
    check(
        absent.status === "active" && absent.declared === false,
        `.${folder}: an absent status resolves to active, flagged undeclared`,
        JSON.stringify(absent)
    );
    const stated = resolveStatus(folder, { status: "draft" });
    check(
        stated.status === "draft" && stated.declared === true,
        `.${folder}: a stated status is passed through as declared`,
        JSON.stringify(stated)
    );
}

for (const folder of ["tech", "ai"]) {
    check(restingStatusFor(folder) === null, `.${folder} has no resting value`);
    const absent = resolveStatus(folder, {});
    check(
        absent.status === null && absent.declared === false,
        `.${folder}: an absent status stays null — "nobody said", not a default`,
        JSON.stringify(absent)
    );
}

// ── validateDocument, folder by folder ─────────────────────────────────────

const lints = [
    // The editorial three: omitting `status` is correct and silent.
    {
        name: ".domain: a chapter omitting status is clean",
        path: ".devbook/domain/ordering/domain.md",
        markdown: `# Ordering\n\n${fence("type: domain\n")}\n## Order\n\n${fence("type: aggregate\n")}\nProse.\n`,
        errors: 0,
        warnings: 0,
    },
    {
        name: ".arc42: an empty meta fence is clean",
        path: ".devbook/arc42/03-context.md",
        markdown: `# 03. Context\n\n${fence("")}\nProse.\n\n## Interfaces\n\n${fence("")}\nProse.\n`,
        errors: 0,
        warnings: 0,
    },
    {
        name: ".design: an empty meta fence is clean",
        path: ".devbook/design/color-scheme.md",
        markdown: `# Color Scheme\n\n${fence("")}\nProse.\n\n## Tokens\n\n${fence("")}\nProse.\n`,
        errors: 0,
        warnings: 0,
    },
    {
        name: ".domain: a status in transition is clean",
        path: ".devbook/domain/ordering/domain.md",
        markdown: `# Ordering\n\n${fence("type: domain\n")}\n## Order\n\n${fence("status: draft\ntype: aggregate\n")}\nProse.\n`,
        errors: 0,
        warnings: 0,
    },
    {
        name: ".domain: deprecated is a standing warning and stays written",
        path: ".devbook/domain/ordering/domain.md",
        markdown: `# Ordering\n\n${fence("type: domain\n")}\n## Order\n\n${fence("status: deprecated\ntype: aggregate\n")}\nProse.\n`,
        errors: 0,
        warnings: 0,
    },
    // The editorial three: stating the resting value is reported.
    {
        name: ".domain: an explicit `status: active` warns",
        path: ".devbook/domain/ordering/domain.md",
        markdown: `# Ordering\n\n${fence("type: domain\n")}\n## Order\n\n${fence("status: active\ntype: aggregate\n")}\nProse.\n`,
        errors: 0,
        warnings: 1,
    },
    {
        name: ".design: an explicit `status: active` warns at file level too",
        path: ".devbook/design/color-scheme.md",
        markdown: `# Color Scheme\n\n${fence("status: active\n")}\nProse.\n`,
        errors: 0,
        warnings: 1,
    },
    {
        name: ".arc42: `status: null` warns — an absence is spelled by omission",
        path: ".devbook/arc42/03-context.md",
        markdown: `# 03. Context\n\n${fence("status: null\n")}\nProse.\n`,
        errors: 0,
        warnings: 1,
    },
    // The other three: a missing status is still an error.
    {
        name: ".tech: a chapter omitting status errors",
        path: ".devbook/tech/backend.md",
        markdown: `# Backend\n\n${fence("status: adopted\n")}\n## PostgreSQL\n\n${fence("type: service\n")}\nProse.\n`,
        errors: 1,
        warnings: 0,
    },
    {
        name: ".ai: a chapter omitting status errors",
        path: ".devbook/ai/02-build.md",
        markdown: `# 02. Build\n\n${fence("status: trial\ntype: stage\n")}\n## Agent-Driven TDD\n\n${fence("type: practice\nstage: build\n")}\nProse.\n`,
        errors: 1,
        warnings: 0,
    },
    {
        name: ".tech: `active` is not this folder's resting value, it is off the ladder",
        path: ".devbook/tech/backend.md",
        markdown: `# Backend\n\n${fence("status: adopted\n")}\n## PostgreSQL\n\n${fence("status: active\ntype: service\n")}\nProse.\n`,
        errors: 1,
        warnings: 0,
    },
    {
        name: ".tech: `status: adopted` on every block stays silent — a rating is meant to be stated",
        path: ".devbook/tech/backend.md",
        markdown: `# Backend\n\n${fence("status: adopted\n")}\n## PostgreSQL\n\n${fence("status: adopted\ntype: service\n")}\nProse.\n`,
        errors: 0,
        warnings: 0,
    },
];

for (const c of lints) {
    const issues = validateDocument(c.path, c.markdown);
    const { errors, warnings } = counts(issues);
    check(
        errors === c.errors && warnings === c.warnings,
        c.name,
        `expected ${c.errors}e/${c.warnings}w, got ${errors}e/${warnings}w: ${issues.map((i) => i.message).join(" | ")}`
    );
}

// ── An empty fence still parses as a block ─────────────────────────────────

const parsed = parseDocument(`# Title\n\n${fence("")}\n## Empty\n\n${fence("")}\nprose\n`);
check(
    parsed.chapters.length === 2 && parsed.chapters.every((c) => c.meta && Object.keys(c.meta).length === 0),
    "an empty meta fence parses to {}, not to null",
    JSON.stringify(parsed.chapters)
);

// ── The derived artifacts, over a real corpus on disk ──────────────────────

const repoRoot = await mkdtemp(path.join(tmpdir(), "devbook-status-"));
try {
    await mkdir(path.join(repoRoot, ".devbook", "design"), { recursive: true });
    await mkdir(path.join(repoRoot, ".devbook", "domain", "ordering"), { recursive: true });
    await mkdir(path.join(repoRoot, ".devbook", "tech"), { recursive: true });

    // A settled .design file: nothing but empty fences, top to bottom.
    await writeFile(
        path.join(repoRoot, ".devbook", "design", "README.md"),
        `# Design\n\n${fence("")}\nProse.\n\n## Principles\n\n${fence("")}\nProse.\n`,
        "utf8"
    );
    // A .domain file mixing a resting chapter with one in transition.
    await writeFile(
        path.join(repoRoot, ".devbook", "domain", "ordering", "domain.md"),
        `# Ordering\n\n${fence("type: domain\n")}\n## Order\n\n${fence("type: aggregate\n")}\nProse.\n\n## Basket\n\n${fence("status: draft\ntype: aggregate\n")}\nProse.\n`,
        "utf8"
    );
    // .tech, where the rating is always written.
    await writeFile(
        path.join(repoRoot, ".devbook", "tech", "technology-graph.md"),
        `# Technology Graph\n\n${fence("status: adopted\n")}\nProse.\n`,
        "utf8"
    );

    const graph = await buildGraph(repoRoot);
    const byId = new Map(graph.nodes.map((n) => [n.id, n]));

    const restingChapter = byId.get(".devbook/domain/ordering/domain.md#order");
    check(
        restingChapter?.status === "active" && restingChapter?.statusDeclared === false,
        "graph.json: a resting .domain chapter carries active plus statusDeclared: false",
        JSON.stringify(restingChapter)
    );

    const transitionChapter = byId.get(".devbook/domain/ordering/domain.md#basket");
    check(
        transitionChapter?.status === "draft" && !("statusDeclared" in transitionChapter),
        "graph.json: a declared status carries no statusDeclared key, so existing nodes do not churn",
        JSON.stringify(transitionChapter)
    );

    const emptyFenceChapter = byId.get(".devbook/design/README.md#principles");
    check(
        emptyFenceChapter !== undefined && emptyFenceChapter.status === "active",
        "graph.json: a chapter whose meta block is empty is still a node",
        JSON.stringify([...byId.keys()])
    );

    const techFile = byId.get(".devbook/tech/technology-graph.md");
    check(
        techFile?.status === "adopted" && !("statusDeclared" in techFile),
        "graph.json: .tech keeps its stated rating untouched",
        JSON.stringify(techFile)
    );

    const doc = await buildGraphDocument(repoRoot, ".", graph);
    check(
        (doc.stats.nodesByStatus.none ?? 0) === 0,
        "graph.json stats: nothing lands in the `none` status bucket",
        JSON.stringify(doc.stats.nodesByStatus)
    );

    const outline = await buildOutlineDocument(repoRoot, ".devbook/design");
    const designRoot = outline.entries.find((e) => e.path === ".devbook/design/README.md");
    check(
        designRoot?.status === "active" && designRoot?.statusDeclared === false,
        "index.json: a .design file that states no status is listed as active, flagged undeclared",
        JSON.stringify(designRoot)
    );

    const techOutline = await buildOutlineDocument(repoRoot, ".devbook/tech");
    const techEntry = techOutline.entries.find((e) => e.path === ".devbook/tech/technology-graph.md");
    check(
        techEntry?.status === "adopted" && !("statusDeclared" in techEntry),
        "index.json: a stated status is listed unchanged",
        JSON.stringify(techEntry)
    );
} finally {
    await rm(repoRoot, { recursive: true, force: true });
}

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
