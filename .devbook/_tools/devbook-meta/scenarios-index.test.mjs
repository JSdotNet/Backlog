// The scenario register, `_meta/scenarios.json`, is built from the graph
// `buildGraph` already holds: every `type: scenario` page, sorted by stem, with
// its path, title, declared status, the six setup fields as written, its
// screenshot labels in page order with the part each sits in, and its parts in
// page order — title, anchor, the labels under it, and every requirement case
// whose `Proved by:` names it. A page in another context and a duplicated stem
// are both listed; the duplicate's problem rides along. The register carries
// its own `schemaVersion` of 1, writes an empty list outside `domain/`, and
// leaves graph.json's shape alone.
//
// Run: `node scenarios-index.test.mjs`
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { buildGraph, buildGraphDocument } from "./graph.mjs";
import { buildScenariosDocument, scenariosPathFor, SCENARIOS_SCHEMA_VERSION } from "./scenarios-index.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};
const fence = (body) => "```meta\n" + body + "```\n";
const dump = (value) => JSON.stringify(value, null, 2);

const FOLDERS = [".devbook/arc42", ".devbook/domain"];
const PAGE = ".devbook/domain/work/journeys/set-up-and-fill-the-backlog.md";
const OTHER = ".devbook/domain/shop/move-an-item.md";
const REQUIREMENTS = ".devbook/domain/work/requirements.md";

const CONTEXT =
    `# Work\n\n${fence("type: context\n")}\nThe work context.\n\n## New Board\n\n${fence("type: feature-flag\nkey: new-board\n")}\nA flag.\n\n` +
    `## Backlog Max Columns\n\n${fence("type: setting\nkey: backlog-max-columns\n")}\nA setting.\n`;
const ACTORS = `# Actors\n\n${fence("type: actors\n")}\nWho works here.\n\n## Product Owner\n\n${fence("type: user\n")}\nOwns the backlog.\n`;
const FEATURES = `# Features\n\n${fence("type: features\n")}\nWhat it does.\n\n## Backlog Board\n\n${fence("type: feature\n")}\nThe board.\n`;
const page = (title, meta) =>
    `# ${title}\n\n${fence(meta)}\nA product owner sets up the backlog and moves the first item.\n\n` +
    `## Statuses are set up\n\n- **Given** a product with no statuses\n- **When** I create "New", "Doing" and "Done"\n- **Then** they appear as columns in that order\n\n` +
    `![The board](shot:statuses-set-up)\n\n![The legend](shot:legend)\n\n## An item is moved\n\n- **Given** an item in "New"\n- **When** I drag it to "Doing"\n- **Then** it stays in "Doing"\n\n` +
    `![After the drag](shot:after-drag)\n`;
const requirement = (title, cases) =>
    `### Requirement: ${title}\n\n${fence("type: requirement\n")}\nThe board SHALL do it.\n\n` +
    cases.map(([heading, target]) => `#### Scenario: ${heading}\nProved by: ${target}\n`).join("\n");
const REQUIREMENTS_MD =
    `# Requirements\n\n${fence("type: requirements\n")}\n## Backlog board\n\n${fence("type: requirements\nrelated: [.devbook/domain/work/features.md#backlog-board]\n")}\n` +
    requirement("Columns follow the status order", [["Statuses are set up", "set-up-and-fill-the-backlog.md#statuses-are-set-up"]]) +
    "\n" +
    requirement("Column limit", [
        ["Statuses are set up", `${PAGE}#statuses-are-set-up`],
        ["An item is moved", "move-an-item.md#an-item-is-moved"],
    ]);

const SETUP =
    "type: scenario\nstatus: draft\nstart: /products/webshop/backlog\nactor: actors.md#product-owner\ndata: [webshop-without-statuses]\n" +
    "profile: tenant-acme\nflags: [context.md#new-board]\nsettings: [context.md#backlog-max-columns=5]\n";

async function withCorpus(files, run) {
    const root = await mkdtemp(path.join(tmpdir(), "devbook-scenarios-"));
    try {
        for (const [relPath, content] of Object.entries(files)) {
            await mkdir(path.join(root, path.dirname(relPath)), { recursive: true });
            await writeFile(path.join(root, relPath), content, "utf8");
        }
        return await run(root, await buildGraph(root, FOLDERS));
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

const corpus = {
    ".devbook/arc42/01-introduction.md": `# Introduction\n\n${fence("")}\nWhat this is.\n`,
    ".devbook/domain/work/context.md": CONTEXT,
    ".devbook/domain/work/actors.md": ACTORS,
    ".devbook/domain/work/features.md": FEATURES,
    [REQUIREMENTS]: REQUIREMENTS_MD,
    [PAGE]: page("Set up and fill the backlog", SETUP),
    ".devbook/domain/shop/context.md": `# Shop\n\n${fence("type: context\n")}\nThe shop context.\n`,
    [OTHER]: page("Move an item", "type: scenario\n"),
};

await withCorpus(corpus, async (root, graph) => {
    const document = buildScenariosDocument(root, ".", graph, FOLDERS);
    const [moved, setUp] = document.scenarios;

    check(document.schemaVersion === SCENARIOS_SCHEMA_VERSION && SCENARIOS_SCHEMA_VERSION === 1, "the register carries its own schemaVersion, 1");
    check(document.scope === "." && dump(document.sources) === dump(FOLDERS), "the rollup's envelope names its scope and sources", dump(document));
    check(
        dump(document.scenarios.map((entry) => entry.stem)) === dump(["move-an-item", "set-up-and-fill-the-backlog"]),
        "every scenario page is listed, sorted by stem, across bounded contexts",
        dump(document.scenarios)
    );

    check(setUp.path === PAGE && setUp.title === "Set up and fill the backlog" && setUp.status === "draft", "a page carries its path, title, and declared status", dump(setUp));
    check(moved.status === null, "a page with no status reads null", dump(moved));
    check(
        dump(setUp.setup) ===
            dump({
                start: "/products/webshop/backlog",
                actor: ["actors.md#product-owner"],
                data: ["webshop-without-statuses"],
                profile: "tenant-acme",
                flags: ["context.md#new-board"],
                settings: ["context.md#backlog-max-columns=5"],
            }),
        "the six setup fields are listed as written, start and profile single, the rest lists",
        dump(setUp.setup)
    );
    check(
        dump(moved.setup) === dump({ start: null, actor: [], data: [], profile: null, flags: [], settings: [] }),
        "an absent setup field reads null or an empty list",
        dump(moved.setup)
    );

    check(
        dump(setUp.labels) ===
            dump([
                { label: "statuses-set-up", part: "statuses-are-set-up" },
                { label: "legend", part: "statuses-are-set-up" },
                { label: "after-drag", part: "an-item-is-moved" },
            ]),
        "every screenshot label is listed in page order with the part it sits in",
        dump(setUp.labels)
    );

    check(
        dump(setUp.parts.map(({ title, anchor, id, labels }) => ({ title, anchor, id, labels }))) ===
            dump([
                { title: "Statuses are set up", anchor: "statuses-are-set-up", id: `${PAGE}#statuses-are-set-up`, labels: ["statuses-set-up", "legend"] },
                { title: "An item is moved", anchor: "an-item-is-moved", id: `${PAGE}#an-item-is-moved`, labels: ["after-drag"] },
            ]),
        "each part carries its title, anchor, id, and labels, in page order",
        dump(setUp.parts)
    );
    check(
        dump(setUp.parts[0].provedBy) ===
            dump([
                { requirement: `${REQUIREMENTS}#requirement-column-limit`, case: "Statuses are set up" },
                { requirement: `${REQUIREMENTS}#requirement-columns-follow-the-status-order`, case: "Statuses are set up" },
            ]),
        "a part lists every requirement case pointing at it, by stem or by path",
        dump(setUp.parts[0].provedBy)
    );
    check(setUp.parts[1].provedBy.length === 0, "a part nothing points at has no provedBy", dump(setUp.parts[1]));
    check(
        dump(moved.parts[1].provedBy) === dump([{ requirement: `${REQUIREMENTS}#requirement-column-limit`, case: "An item is moved" }]),
        "a pointer into another context's page lands on that page's part",
        dump(moved.parts)
    );

    check(
        dump(document.stats) === dump({ pages: 2, parts: 4, labels: 6, unclaimed: 2 }),
        "stats count pages, parts, labels, and unclaimed parts",
        dump(document.stats)
    );
    check(
        document.problems.length === 2 && document.problems.every((problem) => problem.message.includes("unclaimed journey")),
        "problems carry the graph's problems on the listed pages only",
        dump(document.problems)
    );

    const domain = buildScenariosDocument(root, ".devbook/domain", graph, FOLDERS);
    const arc42 = buildScenariosDocument(root, ".devbook/arc42", graph, FOLDERS);
    check(domain.scenarios.length === 2 && dump(domain.sources) === dump([".devbook/domain"]), "the domain scope lists its pages");
    check(arc42.scenarios.length === 0 && arc42.problems.length === 0 && arc42.stats.pages === 0, "a folder with no scenario page writes an empty register", dump(arc42));

    const graphDocument = await buildGraphDocument(root, ".", graph, FOLDERS);
    check(!("scenarios" in graphDocument), "graph.json does not change shape", Object.keys(graphDocument).join(", "));
    check(graph.nodes.every((node) => node.id !== `${PAGE}#an-item-is-moved`), "an unreferenced part is still no graph node");

    check(dump(buildScenariosDocument(root, ".", graph, FOLDERS)) === dump(document), "the register is deterministic");
});

// A stem two contexts share: both pages are listed, the duplicate's problem rides along.
await withCorpus(
    {
        ".devbook/domain/work/context.md": CONTEXT,
        ".devbook/domain/work/actors.md": ACTORS,
        ".devbook/domain/work/set-up.md": page("Set up", "type: scenario\n"),
        ".devbook/domain/shop/context.md": `# Shop\n\n${fence("type: context\n")}\nThe shop context.\n`,
        ".devbook/domain/shop/set-up.md": page("Set up", "type: scenario\n"),
    },
    async (root, graph) => {
        const document = buildScenariosDocument(root, ".", graph, FOLDERS);
        check(
            dump(document.scenarios.map((entry) => entry.path)) === dump([".devbook/domain/shop/set-up.md", ".devbook/domain/work/set-up.md"]),
            "a duplicated stem lists both pages, ordered by path",
            dump(document.scenarios)
        );
        check(document.problems.some((problem) => problem.message.includes('has the stem "set-up"')), "the duplicate-stem problem rides along", dump(document.problems));
    }
);

// A corpus with no scenario page.
await withCorpus({ ".devbook/arc42/01-introduction.md": `# Introduction\n\n${fence("")}\nWhat this is.\n` }, async (root, graph) => {
    const document = buildScenariosDocument(root, ".", graph, [".devbook/arc42"]);
    check(dump(document.scenarios) === "[]" && document.stats.pages === 0, "a corpus with no scenario page writes an empty register", dump(document));
});

check(scenariosPathFor(".") === ".devbook/_meta/scenarios.json", "the rollup lands in .devbook/_meta/");
check(scenariosPathFor(".devbook/domain") === ".devbook/domain/_meta/scenarios.json", "a folder's lands in its own _meta/");

console.log(`\n${failed ? `${failed} failed` : "all passed"}`);
process.exit(failed ? 1 : 0);
