// The term register, `_meta/naming.json`, is built from the graph `buildGraph`
// already holds: every `term` chapter in `domain/`, and every other `domain/`
// chapter that carries `aliases`, under its own title, its lede as the
// description. A `requirement` or `invariant` chapter is not a term: its
// `aliases` are the codes it is cited by. `arc42/`'s glossary is not read — its "Also called" line is
// prose, not a field. The register carries its own `schemaVersion` of 1, sorts
// by name, and reports a term with no definition and a spelling two terms
// claim, both as warnings.
//
// Run: `node naming.test.mjs`
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { buildGraph, buildGraphDocument } from "./graph.mjs";
import { buildNamingDocument, namingPathFor } from "./naming.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};

const fence = (body) => "```meta\n" + body + "```\n";
const DOMAIN = ".devbook/domain/ordering/domain.md";
const GLOSSARY = ".devbook/arc42/12-glossary.md";
const FOLDERS = [".devbook/arc42", ".devbook/domain"];

const domain = [
    `# Domain\n\n${fence("index: root\ntype: domain\n")}`,
    `## Order\n\n${fence("type: aggregate\naliases: [OrderRoot, order_id]\n")}`,
    "> The **order** a customer places,\n> confirmed once paid.\n",
    `### Order Line\n\n${fence("type: entity\n")}\nOne line of an order; no aliases, so no entry.\n`,
    `## Ubiquitous Language\n\n${fence("type: ubiquitous-language\n")}\n> The terms not modelled above.\n`,
    `### Checkout\n\n${fence(`type: term\naliases: [Afrekenen]\nrelated: [${DOMAIN}#order]\n`)}`,
    "```annotation\nkind: question\nstatus: open\nbody: Is this the right word?\n```\n",
    "The step where a customer turns a basket\ninto an `Order`.\n\nA second paragraph that is not the lede.\n",
    `### Basket\n\n${fence("type: term\naliases: [order_id]\n")}\n#### Detail\n\nBelow a heading, so not Basket's lede.\n`,
].join("\n");

// Two rules split from one source row share its code, and an invariant cites
// it too; one rule also lists a spelling a term owns. None of it is language.
const REQUIREMENTS = ".devbook/domain/ordering/requirements.md";
const INVARIANTS = ".devbook/domain/ordering/domain.invariants.md";
const rule = (kind, name, aliases) =>
    `### ${kind[0].toUpperCase()}${kind.slice(1)}: ${name}\n\n${fence(`type: ${kind}\naliases: [${aliases}]\n`)}\nThe system SHALL ${name.toLowerCase()}.\n`;
const requirements = [
    `# Requirements\n\n${fence("type: requirements\n")}`,
    `## Checkout\n\n${fence(`type: requirements\nrelated: [${DOMAIN}#order]\n`)}`,
    rule("requirement", "Acknowledge a confirmed order", "BACK-53"),
    rule("requirement", "Mail a confirmed order", "BACK-53, order_id"),
].join("\n");
const invariants = [
    `# Invariants\n\n${fence("type: invariants\n")}`,
    `## Order\n\n${fence(`type: invariants\nrelated: [${DOMAIN}#order]\n`)}`,
    rule("invariant", "Confirm an order once", "BACK-53"),
].join("\n");

const glossary = `# Glossary\n\n${fence("number: 12\n")}\nTerms.\n\n## Adoption\n\n${fence("date: 2026-09-08\n")}\nAlso called: scope.\n\nWhich folders a repository has taken on.\n`;

const root = await mkdtemp(path.join(tmpdir(), "devbook-naming-"));
try {
    for (const [rel, body] of Object.entries({ [DOMAIN]: domain, [REQUIREMENTS]: requirements, [INVARIANTS]: invariants, [GLOSSARY]: glossary })) {
        await mkdir(path.join(root, path.dirname(rel)), { recursive: true });
        await writeFile(path.join(root, rel), body, "utf8");
    }
    const graph = await buildGraph(root, FOLDERS);
    const register = buildNamingDocument(root, ".", graph, FOLDERS);
    const byName = Object.fromEntries(register.terms.map((term) => [term.name, term]));

    check(register.schemaVersion === 1, "the register carries its own schemaVersion, 1", register.schemaVersion);
    check(
        JSON.stringify(register.terms.map((t) => t.name)) === JSON.stringify(["Basket", "Checkout", "Order"]),
        "terms are the `term` chapters and the aliased model chapters, sorted by name",
        JSON.stringify(register.terms.map((t) => t.name))
    );
    check(!byName["Order Line"], "a model chapter with no `aliases` is not a term");
    check(!byName.Adoption, "an arc42 glossary chapter is not read");
    check(
        !register.terms.some((term) => term.path === REQUIREMENTS || term.path === INVARIANTS),
        "a requirement or invariant is not a term, aliases or not",
        JSON.stringify(register.terms.map((t) => t.id))
    );
    check(
        !register.problems.some((p) => /back-53/.test(p.message) || p.path === REQUIREMENTS || p.path === INVARIANTS),
        "a code shared by rules, or a rule citing a term's spelling, is no collision",
        JSON.stringify(register.problems, null, 2)
    );

    const order = byName.Order;
    check(
        order?.id === `${DOMAIN}#order` && order.path === DOMAIN && order.anchor === "order",
        "id is <path>#<anchor>, the graph node id",
        JSON.stringify(order)
    );
    check(
        JSON.stringify(Object.keys(order ?? {})) ===
            JSON.stringify(["name", "description", "id", "path", "anchor", "aliases", "related"]),
        "a term carries exactly the fields the reader expects",
        JSON.stringify(Object.keys(order ?? {}))
    );
    check(
        order?.description === "The **order** a customer places, confirmed once paid.",
        "a blockquote lede is the description, one line, inline Markdown kept",
        order?.description
    );
    check(JSON.stringify(order?.aliases) === '["OrderRoot","order_id"]', "aliases are carried verbatim", JSON.stringify(order?.aliases));

    const checkout = byName.Checkout;
    check(
        checkout?.description === "The step where a customer turns a basket into an `Order`.",
        "the lede steps over the meta and annotation fences and stops at the first blank line",
        checkout?.description
    );
    check(JSON.stringify(checkout?.related) === JSON.stringify([`${DOMAIN}#order`]), "related is carried", JSON.stringify(checkout?.related));

    check(byName.Basket?.description === "", "a chapter opening into a sub-heading has no description", byName.Basket?.description);
    const warnings = register.problems.filter((p) => p.severity === "warning");
    check(
        warnings.some((p) => p.message.includes(`${DOMAIN}#basket is a term with no definition`)),
        "a term with no definition is a warning",
        JSON.stringify(register.problems, null, 2)
    );
    check(
        warnings.some((p) => p.message.includes('both claim the term "order_id"')),
        "an alias two terms claim is a warning",
        JSON.stringify(register.problems, null, 2)
    );
    check(register.problems.every((p) => p.severity === "warning"), "the register reports nothing at error severity");

    const arc42 = buildNamingDocument(root, ".devbook/arc42", graph, FOLDERS);
    check(arc42.terms.length === 0 && arc42.problems.length === 0, "a folder with no terms writes an empty register", JSON.stringify(arc42));
    check(namingPathFor(".") === ".devbook/_meta/naming.json", "the rollup lands in .devbook/_meta/");
    check(namingPathFor(".devbook/domain") === ".devbook/domain/_meta/naming.json", "a folder's lands in its own _meta/");

    const graphDocument = await buildGraphDocument(root, ".", graph, FOLDERS);
    check(
        !JSON.stringify(graphDocument).includes("customer places"),
        "graph.json does not change shape: the ledes stay off its nodes"
    );
} finally {
    await rm(root, { recursive: true, force: true });
}

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
