// c4-graph.test.mjs — the C4 model in the graph and the check, contract 33.
//
// The per-block rules are validateDocument's: `c4` and `c4-touches` on
// arc42/ and domain/ blocks, `code` on a building block's top-level block,
// `severity` and the debt ladder on a debt record's. The corpus rules are the
// graph build's, through c4-graph.mjs: the workspace's elements, views,
// containment, and relationships as nodes and edges; `embeds`, `c4`, and
// `c4-touches` edges; and every error and the warning `devbook-arc42.md` asks
// of a workspace and the chapters that show it.
//
// Run: node plugins/devbook/tools/devbook-meta/c4-graph.test.mjs
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { documentDigest, validateDocument, resolveStatus } from "./metadata.mjs";
import { buildGraph, CONTRACT_VERSION } from "./graph.mjs";
import { compileStatusLadder } from "./statuses.mjs";
import { c4Embeds } from "./c4-graph.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};
const fence = (body = "") => "```meta\n" + body + "```\n";
const dump = (list) => JSON.stringify(list, null, 2);
const errors = (issues) => issues.filter((i) => i.severity === "error");
const warnings = (issues) => issues.filter((i) => i.severity === "warning");
const has = (issues, needle) => issues.some((i) => i.message.includes(needle));

check(CONTRACT_VERSION >= 33, "the contract is at least 33");

// ── metadata: the four fields ──────────────────────────────────────────────

const BLOCK = ".devbook/arc42/building-blocks/shop.md";
const TDR = ".devbook/arc42/tdr/3-no-retry-budget.md";
const doc = (meta, body = "") => `# Title\n\n${fence(meta)}\nProse.\n${body}`;

{
    const issues = validateDocument(BLOCK, doc("c4: [shop.web]\nc4-touches: [shop.db]\ncode: [src/shop]\n"));
    check(errors(issues).length === 0 && warnings(issues).length === 0, "c4, c4-touches, and code on a building block are clean", dump(issues));
}
{
    const issues = validateDocument(".devbook/domain/orders/domain.md", doc("type: domain\nc4: [shop.api]\n", `\n## Order\n\n${fence("type: aggregate\nc4-touches: [shop.db]\n")}\nProse.\n`));
    check(!has(issues, "unrecognized field"), "c4 and c4-touches are domain/ fields too", dump(issues));
}
{
    const issues = validateDocument(".devbook/tech/x.md", doc("status: trial\nc4: [shop]\n"));
    check(has(warnings(issues), "unrecognized field `c4`"), "c4 outside arc42/ and domain/ is an unrecognized field", dump(issues));
}
{
    const issues = validateDocument(BLOCK, doc("c4: [.devbook/arc42/x.md#y]\n"));
    check(has(errors(issues), "is not a workspace identifier"), "a c4 entry that is a path is an error", dump(issues));
}
{
    const issues = validateDocument(BLOCK, doc("c4: [shop.web]\nc4-touches: [shop.web]\n"));
    check(has(errors(issues), "in both `c4` and `c4-touches`"), "one element in both c4 fields is an error", dump(issues));
}
for (const [where, relPath, markdown] of [
    ["chapter 5", ".devbook/arc42/05-building-block-view.md", doc("code: [src]\n")],
    ["a building block's section", BLOCK, doc("", `\n## Structure\n\n${fence("code: [src]\n")}\nProse.\n`)],
    ["the building-blocks index", ".devbook/arc42/building-blocks/README.md", doc("index: root\ncode: [src]\n")],
]) {
    check(has(errors(validateDocument(relPath, markdown)), "has `code`, which sits on an"), `code on ${where} is an error`);
}
for (const entry of ["/src", "../src", "C:/src", "src#x"]) {
    check(has(errors(validateDocument(BLOCK, doc(`code: [${entry}]\n`))), "is not a repository-relative folder"), `code entry "${entry}" is an error`);
}
{
    const issues = validateDocument(TDR, doc("number: 3\ndate: 2026-10-20\nseverity: high\n"));
    check(errors(issues).length === 0 && warnings(issues).length === 0, "a debt record with severity is clean", dump(issues));
}
{
    const issues = validateDocument(TDR, doc("severity: huge\n"));
    check(has(errors(issues), "has `severity` \"huge\", expected one of: low, medium, high, critical"), "an unknown severity is an error", dump(issues));
}
{
    const issues = validateDocument(TDR, doc("date: 2026-10-20\n"));
    check(has(warnings(issues), "debt record without `severity`") && errors(issues).length === 0, "a debt record without severity is a warning, so an older set keeps validating", dump(issues));
}
for (const [where, relPath, markdown] of [
    ["the tdr/ index", ".devbook/arc42/tdr/README.md", doc("index: root\nseverity: low\n")],
    ["a debt record's section", TDR, doc("severity: low\n", `\n## Impact\n\n${fence("severity: low\n")}\nProse.\n`)],
    ["an ADR", ".devbook/arc42/adr/storage.md", doc("severity: low\n")],
]) {
    check(has(errors(validateDocument(relPath, markdown)), "has `severity`, which sits on an"), `severity on ${where} is an error`);
}
check(!has(validateDocument(".devbook/arc42/tdr/README.md", doc("index: root\n")), "without `severity`"), "the tdr/ index owes no severity");

// ── metadata: the debt ladder ──────────────────────────────────────────────

for (const status of ["planned", "in-progress", "resolved"]) {
    const issues = validateDocument(TDR, doc(`severity: low\nstatus: ${status}\n`));
    check(errors(issues).length === 0 && warnings(issues).length === 0, `status: ${status} is on the debt ladder`, dump(issues));
}
{
    const issues = validateDocument(TDR, doc("severity: low\nstatus: identified\n"));
    check(has(warnings(issues), "resting value of a debt record"), "status: identified is the resting value and is reported when written", dump(issues));
}
{
    const issues = validateDocument(TDR, doc("severity: low\nstatus: draft\n"));
    check(has(warnings(issues), "a rung of the arc42/ ladder") && errors(issues).length === 0, "an arc42 rung on a debt record is a warning, not an error", dump(issues));
}
{
    const issues = validateDocument(TDR, doc("severity: low\nstatus: paid\n"));
    check(has(errors(issues), "expected one of: identified, planned, in-progress, resolved"), "a word on neither ladder is an error", dump(issues));
}
{
    const issues = validateDocument(".devbook/arc42/05-building-block-view.md", doc("status: planned\n"));
    check(has(errors(issues), "expected one of: draft, proposed, active, deprecated"), "the debt ladder does not apply elsewhere in arc42/", dump(issues));
}
{
    const issues = validateDocument(TDR, doc("severity: low\n", `\n## Impact\n\n${fence("status: planned\n")}\nProse.\n`));
    check(has(errors(issues), "## Impact") && has(errors(issues), "expected one of: draft, proposed, active, deprecated"), "a debt record's sections keep the arc42/ ladder", dump(issues));
}
{
    const issues = validateDocument(".devbook/arc42/tdr/README.md", doc("index: root\nstatus: planned\n"));
    check(has(errors(issues), "expected one of: draft, proposed, active, deprecated"), "the tdr/ index keeps the arc42/ ladder", dump(issues));
}
check(resolveStatus("arc42", {}, TDR, "file").status === "identified", "an omitted status on a debt record resolves to identified");
check(resolveStatus("arc42", {}, ".devbook/arc42/05-building-block-view.md", "file").status === "active", "an omitted status elsewhere in arc42/ resolves to active");
{
    const { ladder, issues } = compileStatusLadder({
        folders: { arc42: { rules: [{ files: ["**"], statuses: ["draft"] }, { files: ["tdr/**"], scope: "file", statuses: ["planned"] }] } },
    });
    check(issues.length === 0, "a tdr/ rule may list debt rungs", dump(issues));
    const narrowed = validateDocument(TDR, doc("severity: low\nstatus: resolved\n"), { ladder });
    check(has(errors(narrowed), "expected one of: planned"), "a tdr/ rule replaces the debt ladder, and a folder-wide rule does not", dump(narrowed));
    check(errors(validateDocument(TDR, doc("severity: low\nstatus: planned\n"), { ladder })).length === 0, "the tdr/ rule's rung is accepted");
    const { issues: restingIssues } = compileStatusLadder({ folders: { arc42: { rules: [{ files: ["tdr/**"], statuses: ["identified", "planned"] }] } } });
    check(has(warnings(restingIssues), "lists `identified`, the resting value of a debt record") && errors(restingIssues).length === 0, "a tdr/ rule listing identified is a warning, as it was a rung under 31", dump(restingIssues));
}

// ── what validated under contract 32 still does ────────────────────────────

{
    const { ladder } = compileStatusLadder({ folders: { arc42: { rules: [{ files: ["**/*.md"], statuses: ["draft", "superseded"] }] } } });
    const issues = validateDocument(TDR, doc("severity: low\nstatus: superseded\n"), { ladder });
    check(errors(issues).length === 0 && has(warnings(issues), "a rung of the arc42/ ladder"), "a folder-wide rule's rung on a debt record is a warning", dump(issues));
}
{
    const { ladder } = compileStatusLadder({ folders: { arc42: { rules: [{ files: ["tdr/*.md"], statuses: ["planned"] }] } } });
    const issues = validateDocument(TDR, doc("severity: low\nstatus: active\n"), { ladder });
    check(errors(issues).length === 0 && warnings(issues).length === 1, "status: active on a debt record under a tdr/ rule is a warning", dump(issues));
    const index = validateDocument(".devbook/arc42/tdr/README.md", doc("index: root\nstatus: proposed\n"), { ladder });
    check(errors(index).length === 0, "a tdr/ rule does not narrow the tdr/ index", dump(index));
}
for (const [relPath, meta] of [[".devbook/design/x.md", "severity: low\n"], [".devbook/tech/x.md", "status: trial\ncode: [src]\n"], [".devbook/domain/orders/domain.md", "type: domain\nseverity: low\n"]]) {
    const issues = validateDocument(relPath, doc(meta));
    check(errors(issues).length === 0 && has(warnings(issues), "unrecognized field"), `${meta.trim().split("\n").pop()} in ${relPath} stays an unrecognized-field warning`, dump(issues));
}
check(has(errors(validateDocument(BLOCK, doc("code: [.]\n"))), "is not a repository-relative folder"), "code entry \".\" is an error");
{
    const embeds = c4Embeds("# T\n\n<!-- ![x](c4:a) -->\n<!--\n![y](c4:b)\n-->\n![z](c4:c)\n", [{ level: 1, line: 1, slug: "t", text: "T" }]);
    check(embeds.map((e) => e.key).join() === "c", "an embed in an HTML comment is not an embed", dump(embeds));
}

// ── metadata: a c4: embed is a diagram ────────────────────────────────────

check(documentDigest("# T\n\n![Containers](c4:containers)\n").diagrams === 1, "a c4: embed counts as a diagram");
check(documentDigest("# T\n\n![Board](shot:board)\n").diagrams === 0, "a shot: image still does not");
{
    const embeds = c4Embeds("# T\n\n`![x](c4:a)` and ![y](c4:b)\n\n```md\n![z](c4:c)\n```\n", [{ level: 1, line: 1, slug: "t", text: "T" }]);
    check(embeds.map((e) => e.key).join() === "b", "an embed in a code span or a fence is not an embed", dump(embeds));
}

// ── the graph: a repository fixture ────────────────────────────────────────

const WORKSPACE = `workspace "Shop" {
    !identifiers hierarchical
    model {
        customer = person "Customer" "Buys things."
        shop = softwareSystem "Shop" "Sells things." {
            web = container "Web" "Serves pages." "ASP.NET Core"
            db = container "Database" "Holds orders." "PostgreSQL"
            web -> db "Reads and writes" "SQL"
        }
        customer -> shop.web "Browses"
        live = deploymentEnvironment "Live" {
            server = deploymentNode "Server" "The one host." {
                containerInstance shop.web
            }
        }
    }
    views {
        systemContext shop context {
            include *
        }
        container shop containers {
            include *
        }
        dynamic shop checkout {
            customer -> shop.web "Places an order"
            shop.web -> shop.db "Stores it"
        }
        deployment * live live-deployment {
            include *
        }
    }
}
`;

const CLEAN = {
    ".devbook/arc42/workspace.dsl": WORKSPACE,
    ".devbook/arc42/03-context-and-scope.md": `# 03. Context and Scope\n\n${fence()}\n![Context](c4:context)\n`,
    ".devbook/arc42/05-building-block-view.md": `# 05. Building Block View\n\n${fence()}\n## Containers\n\n${fence()}\n![Containers](c4:containers)\n`,
    ".devbook/arc42/building-blocks/shop.md": `# Shop\n\n${fence("c4: [shop]\nc4-touches: [shop.db]\ncode: [src/shop]\n")}\nThe shop.\n\n## Runtime\n\n${fence()}\n### Checkout\n\n![Checkout](c4:checkout)\n`,
    ".devbook/arc42/07-deployment-view.md": `# 07. Deployment View\n\n${fence()}\n![Live](c4:live-deployment)\n`,
    ".devbook/domain/orders/domain.md": `# Domain\n\n${fence("type: domain\nc4: [shop.web]\n")}\nOrders.\n`,
    "src/shop/README.md": "code\n",
};

async function graphOf(overrides = {}) {
    const root = await mkdtemp(path.join(tmpdir(), "c4-graph-"));
    const files = { ...CLEAN, ...overrides };
    for (const [rel, text] of Object.entries(files)) {
        if (text == null) continue;
        await mkdir(path.dirname(path.join(root, rel)), { recursive: true });
        await writeFile(path.join(root, rel), text);
    }
    try {
        return await buildGraph(root);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}
const c4Problems = (graph) => graph.problems.filter((p) => !p.message.includes("has no `meta` block") && !p.message.includes("context.md"));
const node = (graph, id) => graph.nodes.find((n) => n.id === id);
const edge = (graph, type, source, target) => graph.edges.some((e) => e.type === type && e.source === source && e.target === target);

{
    const graph = await graphOf();
    const relevant = c4Problems(graph).filter((p) => p.path?.startsWith(".devbook/arc42") || p.message.includes("c4"));
    check(relevant.length === 0, "the clean fixture reports nothing about the model", dump(relevant));
    check(node(graph, "c4:shop.web")?.type === "element" && node(graph, "c4:shop.web").kind === "container" && node(graph, "c4:shop.web").path === ".devbook/arc42/workspace.dsl", "an element is a node in the arc42 scope", dump(node(graph, "c4:shop.web")));
    check(node(graph, "c4-view:containers")?.type === "view" && node(graph, "c4-view:containers").kind === "container", "a view is a node", dump(node(graph, "c4-view:containers")));
    check(edge(graph, "contains", "c4:shop", "c4:shop.web"), "a system contains its container");
    check(edge(graph, "relationship", "c4:shop.web", "c4:shop.db"), "a relationship is an edge");
    check(edge(graph, "embeds", ".devbook/arc42/05-building-block-view.md#containers", "c4-view:containers"), "an embed is an edge from its section to the view");
    check(edge(graph, "embeds", ".devbook/arc42/building-blocks/shop.md#runtime", "c4-view:checkout"), "an embed under a structural heading comes from the nearest chapter");
    check(edge(graph, "c4", ".devbook/arc42/building-blocks/shop.md", "c4:shop"), "c4 is an edge to the element");
    check(edge(graph, "c4-touches", ".devbook/arc42/building-blocks/shop.md", "c4:shop.db"), "c4-touches is an edge to the element");
    check(edge(graph, "c4", ".devbook/domain/orders/domain.md", "c4:shop.web"), "a domain/ chapter's c4 is an edge too");
    check(JSON.stringify(node(graph, ".devbook/arc42/building-blocks/shop.md")?.code) === '["src/shop"]', "code is carried on the block's node", dump(node(graph, ".devbook/arc42/building-blocks/shop.md")));
}

// ── the graph: every error and the warning ─────────────────────────────────

const ws = (from, to) => ({ ".devbook/arc42/workspace.dsl": WORKSPACE.replace(from, to) });
const bb5 = (body) => ({ ".devbook/arc42/05-building-block-view.md": `# 05. Building Block View\n\n${fence()}\n${body}\n` });

for (const [name, overrides, needle, severity = "error"] of [
    ["an unknown identifier in the workspace", ws('customer -> shop.web "Browses"', 'customer -> shop.api "Browses"'), "`shop.api` is not an element in this workspace"],
    ["a duplicate identifier", ws('db = container "Database"', 'web = container "Database"'), "`shop.web` is declared more than once"],
    ["an unresolved include", ws("    model {\n", "    model {\n        !include c4/missing.dsl\n"), "`!include c4/missing.dsl`: no such file"],
    ["a dynamic step between elements with no relationship", ws('shop.web -> shop.db "Stores it"', 'shop.db -> customer "Stores it"'), "No relationship from `shop.db` to `customer`"],
    ["an element without a description", ws('"Holds orders."', '""'), "`shop.db` has no description"],
    ["an element with a multi-line description", ws('"Holds orders."', '"Holds orders.\\nAnd more."'), "has a description of more than one line"],
    ["`!identifiers` not first", ws("    !identifiers hierarchical\n    model {", "    model {\n    }\n    !identifiers hierarchical\n    model {"), "`!identifiers` comes after the model began"],
    ["an unknown view key", bb5("![Containers](c4:containers)\n![Nope](c4:nope)"), "embeds `c4:nope`, which is no view key"],
    ["an unknown element in c4", { ".devbook/domain/orders/domain.md": `# Domain\n\n${fence("type: domain\nc4: [shop.api]\n")}\nOrders.\n` }, "has `c4` entry \"shop.api\", which is no element"],
    ["an unknown element in c4-touches", { ".devbook/arc42/building-blocks/shop.md": CLEAN[".devbook/arc42/building-blocks/shop.md"].replace("c4-touches: [shop.db]", "c4-touches: [shop.cache]") }, "has `c4-touches` entry \"shop.cache\", which is no element"],
    ["a view embedded twice", bb5("## Containers\n\n```meta\n```\n![Containers](c4:containers)\n\n## Again\n\n```meta\n```\n![Containers](c4:containers)"), "View `containers` is embedded 2 times"],
    ["a context view outside chapter 3", bb5("![Containers](c4:containers)\n![Context](c4:context)"), "embeds the `systemContext` view `context`, which belongs in `03-context-and-scope.md`"],
    ["a container view in a domain/ chapter", { ".devbook/domain/orders/domain.md": `# Domain\n\n${fence("type: domain\n")}\n![Containers](c4:containers)\n` }, "embeds the `container` view `containers`, which belongs in"],
    ["a dynamic view in a building block outside `## Runtime`", { ".devbook/arc42/building-blocks/shop.md": `# Shop\n\n${fence()}\nThe shop.\n\n## Structure\n\n${fence()}\n![Checkout](c4:checkout)\n` }, "embeds the `dynamic` view `checkout`, which belongs in `06-runtime-view.md` or a building block's `## Runtime`"],
    ["a deployment view in chapter 6", { ".devbook/arc42/07-deployment-view.md": null, ".devbook/arc42/06-runtime-view.md": `# 06. Runtime View\n\n${fence()}\n![Live](c4:live-deployment)\n` }, "embeds the `deployment` view `live-deployment`, which belongs in `07-deployment-view.md`"],
    ["a container view in the building-blocks index", { ".devbook/arc42/building-blocks/README.md": `# Building Blocks\n\n${fence("index: root\n")}\n![Containers](c4:containers)\n` }, "embeds the `container` view `containers`, which belongs in"],
    ["an embed with no workspace", { ".devbook/arc42/workspace.dsl": null }, "embeds `c4:context`, but there is no .devbook/arc42/workspace.dsl"],
    ["a code entry that is a file", { "src/shop.txt": "x", ".devbook/arc42/building-blocks/shop.md": CLEAN[".devbook/arc42/building-blocks/shop.md"].replace("code: [src/shop]", "code: [src/shop.txt]") }, "which is a file — `code` lists folders"],
    ["a view no chapter embeds", { ".devbook/arc42/07-deployment-view.md": null }, "view `live-deployment` is embedded in no chapter", "warning"],
    ["a code folder that does not exist", { ".devbook/arc42/building-blocks/shop.md": CLEAN[".devbook/arc42/building-blocks/shop.md"].replace("code: [src/shop]", "code: [src/gone]") }, "no such folder exists", "warning"],
]) {
    const graph = await graphOf(overrides);
    const hit = graph.problems.find((p) => p.message.includes(needle));
    check(hit?.severity === severity, `${name} is ${severity === "error" ? "an error" : "a warning"}`, dump(hit ?? c4Problems(graph)));
}
{
    const graph = await graphOf({ ".devbook/domain/orders/domain.md": `# Domain\n\n${fence("type: domain\nc4: [shop.web, shop.web]\n")}\nOrders.\n` });
    const ids = graph.edges.filter((e) => e.type === "c4" && e.source === ".devbook/domain/orders/domain.md").map((e) => e.id);
    check(ids.length === 1, "an entry written twice draws one edge", dump(ids));
}
{
    const graph = await graphOf({ ".devbook/arc42/workspace.dsl": WORKSPACE.replace("    model {\n", "    model {\n        !include c4/people.dsl\n").replace('customer = person "Customer" "Buys things."\n', ""), ".devbook/arc42/c4/people.dsl": 'customer = person "Customer" "Buys things."\n' });
    const relevant = c4Problems(graph).filter((p) => p.path?.startsWith(".devbook/arc42"));
    check(relevant.length === 0 && node(graph, "c4:customer")?.path === ".devbook/arc42/c4/people.dsl", "a resolved include reads clean, its elements placed in their own file", dump(relevant));
}

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
