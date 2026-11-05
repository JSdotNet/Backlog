// Asserts units.mjs: what each unit holds, which chapter belongs where, the
// one tie that joins units into a group and the links that never do, the
// set-asides, the orphans, and the flags. No unit carries a direction: since
// contract 38 the tool that runs a sweep keeps it.
//
// Run: `node units.test.mjs`
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { buildGraph } from "./graph.mjs";
import { collectUnits, groupUnits, listUnits, main, summaryLine } from "./units.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};

const fence = (body) => "```meta\n" + body + "```\n";
const dump = (value) => JSON.stringify(value, null, 2);

const CTX = ".devbook/domain/ordering";
const D = `${CTX}/domain.md`;
const F = `${CTX}/features.md`;
const R = `${CTX}/requirements.md`;
const I = `${CTX}/domain.invariants.md`;

// One context: two aggregates and a service, their events and invariants, a
// shared grouping, a feature with a sub-feature, requirements, terms, and a
// switch; one building block; one design component.
const files = {
    ".devbook/domain/context-map.md": `# Context Map\n\n${fence("type: context-map\n")}`,
    [`${CTX}/context.md`]: [
        `# Ordering\n\n${fence("type: context\n")}`,
        `## Express Checkout\n\n${fence("type: feature-flag\n")}\nA switch.\n`,
        `## Payment Callback\n\n${fence("type: technical\n")}\nConfirms a payment.\n`,
    ].join("\n"),
    [`${CTX}/actors.md`]: [
        `# Actors\n\n${fence("type: actors\n")}`,
        `## Buyer\n\n${fence("type: user\nrole: Buyer\n")}\nBuys.\n`,
        `## Card Issuer\n\n${fence("type: organisation\n")}\nIssues the card.\n`,
    ].join("\n"),
    [D]: [
        `# Domain\n\n${fence("type: domain\n")}`,
        `## Order\n\n${fence(`type: aggregate\naliases: [PurchaseOrder]\nrelated: [${I}#order]\n`)}\nAn order.\n`,
        `### OrderLine\n\n${fence("type: entity\n")}\nA line.\n`,
        `### OrderStatus\n\n${fence("type: enum\n")}\nA status.\n`,
        `## Customer\n\n${fence("type: aggregate\n")}\nA customer.\n`,
        `### Address\n\n${fence("type: value-object\n")}\nAn address.\n`,
        `## Pricing\n\n${fence("type: domain-service\n")}\nPrices.\n`,
        `## Shared Value Objects\n\n${fence(`type: shared-value-objects\nrelated: [${I}#shared-value-objects]\n`)}\nShared.\n`,
        `### Money\n\n${fence("type: value-object\n")}\nMoney.\n`,
        `## Domain Events\n\nThe events.\n`,
        `### OrderPlaced\n\n${fence(`type: domain-event\nrelated: [${D}#order]\n`)}\nPlaced.\n`,
        `### PriceComputed\n\n${fence(`type: domain-event\nrelated: [${D}#pricing, ${F}#checkout]\n`)}\nComputed.\n`,
        `### SomethingHappened\n\n${fence("type: domain-event\n")}\nUnraised.\n`,
        `## Ubiquitous Language\n\n${fence("type: ubiquitous-language\n")}\nTerms.\n`,
        `### Purchase Order\n\n${fence("type: term\naliases: [PurchaseOrder]\n")}\nAn order, as sales says it.\n`,
        `### Account\n\n${fence("type: term\naliases: [Customer, Order]\n")}\nTwo homes.\n`,
        `### Basket\n\n${fence("type: term\n")}\nNo home: context.\n`,
    ].join("\n"),
    [I]: [
        `# Invariants\n\n${fence("type: invariants\n")}`,
        `## Order\n\n${fence(`type: invariants\nrelated: [${D}#order, ${F}#checkout]\n`)}`,
        `### Invariant: An order has a line\n\n${fence("type: invariant\n")}\nAn order has at least one line.\n\nEnforced at: constructor\n`,
        `## Shared Value Objects\n\n${fence(`type: invariants\nrelated: [${D}#shared-value-objects]\n`)}`,
        `### Invariant: Money is never negative\n\n${fence("type: invariant\n")}\nMoney is never negative.\n\nEnforced at: constructor\n`,
    ].join("\n"),
    [F]: [
        `# Features\n\n${fence("type: features\n")}`,
        `## Checkout\n\n${fence(`type: feature\ndepends-on: [${D}#order, ${D}#customer]\nrelated: [${R}#checkout]\n`)}\nCheck out.\n`,
        `### Guest Checkout\n\n${fence("type: sub-feature\n")}\nAs a guest.\n`,
    ].join("\n"),
    [R]: [
        `# Requirements\n\n${fence("type: requirements\n")}`,
        `## Checkout\n\n${fence(`type: requirements\nrelated: [${F}#checkout]\n`)}`,
        `### Requirement: Checkout SHALL confirm\n\n${fence("type: requirement\n")}\nThe system SHALL confirm.\n`,
        `### Requirement: An order SHALL name its customer\n\n${fence(`type: requirement\nrelated: [${D}#order, ${D}#customer]\n`)}\nAn order SHALL name its customer.\n`,
        `### Requirement: A guest order SHALL price\n\n${fence(`type: requirement\nrelated: [${F}#guest-checkout, ${D}#order, ${D}#pricing]\n`)}\nIt SHALL price.\n`,
    ].join("\n"),
    ".devbook/arc42/05-building-block-view.md": `# Building Block View\n\n${fence("")}\nThe blocks.\n`,
    ".devbook/arc42/building-blocks/README.md": `# Building Blocks\n\n${fence("index: root\n")}\nThe index.\n`,
    ".devbook/arc42/building-blocks/billing.md": `# billing\n\n${fence("")}\nBilling.\n\n## Interfaces\n\n${fence("")}\nIts interfaces.\n`,
    ".devbook/design/component-libraries.md": [
        `# Component Libraries\n\n${fence("")}`,
        `## Button\n\n${fence("")}\nA button.\n`,
        `### Requirement: A button SHALL focus\n\n${fence("type: requirement\n")}\nIt SHALL focus.\n`,
    ].join("\n"),
};

async function fixture(extra = {}) {
    const root = await mkdtemp(path.join(tmpdir(), "devbook-units-"));
    for (const [rel, body] of Object.entries({ ...files, ...extra })) {
        await mkdir(path.join(root, path.dirname(rel)), { recursive: true });
        await writeFile(path.join(root, rel), body);
    }
    return root;
}

const root = await fixture();
try {
    const collected = collectUnits(await buildGraph(root));
    const unit = (id) => collected.units.find((u) => u.id === id);
    const has = (id, chapter) => unit(id)?.chapters.includes(chapter);

    // --- The units ---------------------------------------------------------

    check(
        JSON.stringify(collected.units.map((u) => [u.kind, u.id])) ===
            JSON.stringify([
                ["building-block", ".devbook/arc42/building-blocks/billing.md"],
                ["design-component", ".devbook/design/component-libraries.md#button"],
                ["shared-types", `${CTX}#shared-types`],
                ["user", `${CTX}/actors.md#buyer`],
                ["feature-flag", `${CTX}/context.md#express-checkout`],
                ["technical", `${CTX}/context.md#payment-callback`],
                ["aggregate", `${D}#customer`],
                ["aggregate", `${D}#order`],
                ["domain-service", `${D}#pricing`],
                ["feature", `${F}#checkout`],
            ]),
        "one unit per root, one shared-types unit per context, sorted by id; the README index is no block",
        dump(collected.units.map((u) => [u.kind, u.id]))
    );

    const order = `${D}#order`;
    check(has(order, `${D}#orderline`) && has(order, `${D}#orderstatus`), "an aggregate holds the entities and enums under it");
    check(has(order, `${D}#orderplaced`), "an event belongs to the raiser its `related` names");
    check(has(order, `${I}#order`) && has(order, `${I}#invariant-an-order-has-a-line`), "an aggregate holds its invariants chapter and the invariants under it");
    check(has(order, `${D}#purchase-order`), "a term whose alias resolves into one unit belongs to it");
    check(has(`${D}#customer`, `${D}#address`), "a value object under an aggregate is that aggregate's");
    check(has(`${D}#pricing`, `${D}#pricecomputed`), "a domain service holds the events it raises; a link onward to a feature does not move it");

    const shared = unit(`${CTX}#shared-types`);
    check(
        ["#shared-value-objects", "#money"].every((s) => shared.chapters.includes(`${D}${s}`)) &&
            shared.chapters.includes(`${I}#invariant-money-is-never-negative`),
        "the shared-types unit holds the shared groupings, their types, and their invariants",
        dump(shared)
    );

    const feature = `${F}#checkout`;
    check(has(feature, `${F}#guest-checkout`), "a feature holds its sub-features");
    check(has(feature, `${R}#checkout`) && has(feature, `${R}#requirement-checkout-shall-confirm`), "a requirement under a feature's grouping belongs to the feature");
    check(has(feature, `${R}#requirement-a-guest-order-shall-price`), "decision 7: a requirement naming a feature belongs to the feature, whatever else it names");
    check(
        has(`${D}#customer`, `${R}#requirement-an-order-shall-name-its-customer`) && !has(feature, `${R}#requirement-an-order-shall-name-its-customer`),
        "a requirement naming aggregates and no feature belongs to the first it names by id"
    );

    check(has(".devbook/design/component-libraries.md#button", ".devbook/design/component-libraries.md#requirement-a-button-shall-focus"), "a design component holds the requirements under it");
    check(
        unit(".devbook/arc42/building-blocks/billing.md").files.includes(".devbook/arc42/05-building-block-view.md") &&
            has(".devbook/arc42/building-blocks/billing.md", ".devbook/arc42/building-blocks/billing.md#interfaces"),
        "a building block holds its sections and its row in the building block view"
    );

    const owners = new Map();
    for (const u of collected.units) for (const c of u.chapters) owners.set(c, (owners.get(c) ?? 0) + 1);
    check([...owners.values()].every((n) => n === 1), "every chapter belongs to exactly one unit", dump([...owners].filter(([, n]) => n > 1)));
    check(!owners.has(`${D}#basket`) && !owners.has(`${CTX}/actors.md#card-issuer`), "a term with no home and an organisation stay context, in no unit");
    check(
        JSON.stringify(unit(`${CTX}/actors.md#buyer`)?.chapters) === JSON.stringify([`${CTX}/actors.md#buyer`]),
        "a user actor is a unit of its one chapter",
        dump(unit(`${CTX}/actors.md#buyer`))
    );

    // --- Orphans ------------------------------------------------------------------

    const orphan = (id) => collected.orphans.find((o) => o.id === id);
    check(orphan(`${D}#somethinghappened`)?.reason.includes("no raiser"), "an event naming no raiser is an orphan", dump(collected.orphans));
    check(orphan(`${D}#account`)?.reason.includes("2 units"), "a term whose aliases resolve into two units is an orphan", dump(collected.orphans));
    check(collected.orphans.length === 2, "nothing else is an orphan", dump(collected.orphans));
    check(!("unused" in collected), "there is no direction left to inherit, so nothing lists one", dump(Object.keys(collected)));
    check(
        collected.units.every((u) => !("sync" in u) && !("syncFrom" in u) && !("syncLevel" in u)),
        "a unit carries no direction",
        dump(collected.units[0])
    );

    // --- Groups --------------------------------------------------------------------

    const groups = groupUnits(collected);
    const groupOf = (id) => groups.find((g) => g.units.some((u) => u.id === id));
    check(groupOf(order) === groupOf(`${D}#customer`), "a requirement naming two aggregates and no feature joins them");
    check(groupOf(order) !== groupOf(`${D}#pricing`), "a requirement that also names a feature joins nothing");
    check(groupOf(feature) !== groupOf(order), "a feature's depends-on never joins it to the aggregates it names");
    check(groupOf(order) !== groupOf(`${CTX}#shared-types`), "a shared type never joins the aggregates that use it");
    check(groups.length === collected.units.length - 1, "every other unit is a group of one", dump(groups.map((g) => g.id)));
    check(
        JSON.stringify(groupOf(order).ties) === JSON.stringify([`${R}#requirement-an-order-shall-name-its-customer`]),
        "a group names the requirements that tie it",
        dump(groupOf(order))
    );
    check(!("direction" in groupOf(order)) && groupOf(order).setAside === null, "a group carries no direction, and is not set aside");
    check(groupOf(order).slug === "ordering-customer", "a group's slug is its context and its first unit", groupOf(order).slug);

    const small = groupUnits(collected, { maxGroupChapters: 5 });
    check(small.find((g) => g.id === `${D}#customer`)?.setAside === "oversized", "a group past maxGroupChapters is set aside, not split", dump(small.find((g) => g.id === `${D}#customer`)));
    check(groups.every((g) => g.setAside === null), "nothing is oversized at the default 40");

    // --- listUnits and the flags -----------------------------------------------------

    const all = await listUnits(root);
    check(
        JSON.stringify(all.units.map((u) => u.id)) === JSON.stringify(collected.units.map((u) => u.id)) && !("direction" in all),
        "listUnits lists every unit, with no direction filter",
        dump(all.units.map((u) => u.id))
    );
    const grouped = await listUnits(root, { groups: true });
    check(grouped.groups.length === collected.units.length - 1 && grouped.setAside.length === 0, "--groups lists every group", dump(grouped.groups.map((g) => g.id)));
    check(
        summaryLine(grouped) === "9 groups of 10 units; 0 set aside; 2 orphans.",
        "the summary line counts groups, units, set-asides, and orphans",
        summaryLine(grouped)
    );

    const first = JSON.stringify(await listUnits(root, { groups: true }));
    const second = JSON.stringify(await listUnits(root, { groups: true }));
    check(first === second, "two runs over one corpus print the same");

    const logs = [];
    const log = console.log;
    console.log = (line) => logs.push(line);
    const code = await main(["--root", root, "--groups", "--json"]);
    const bad = await main(["--root", root, "--direction", "pull"]);
    console.log = log;
    const printed = JSON.parse(logs[0]);
    check(code === 0 && printed.version === 2 && !("direction" in printed) && Array.isArray(printed.groups), "--json prints the result, version 2, with no direction", logs[0]?.slice(0, 200));
    check(bad === 1, "--direction is gone: it is an unknown argument");
} finally {
    await rm(root, { recursive: true, force: true });
}

// --- The workspace ------------------------------------------------------------------

{
    const BLOCK = ".devbook/arc42/building-blocks/billing.md";
    const bare = await fixture();
    const withWorkspace = await fixture({
        ".devbook/arc42/workspace.dsl": `workspace {\nmodel {\nshop = softwareSystem "Shop" "Sells." {\n!include c4/billing.dsl\n}\n}\nviews {\ncontainer shop "containers" {\ninclude *\n}\n}\n}\n`,
        ".devbook/arc42/c4/billing.dsl": `billing = container "Billing" "Bills."\n`,
    });
    try {
        const filesOf = async (at) => collectUnits(await buildGraph(at)).units.find((u) => u.id === BLOCK)?.files ?? [];
        const without = await filesOf(bare);
        check(!without.some((f) => f.includes("workspace.dsl")), "without a workspace, a building block lists no workspace.dsl", dump(without));
        const withIt = await filesOf(withWorkspace);
        check(
            withIt.includes(".devbook/arc42/workspace.dsl") && withIt.includes(".devbook/arc42/c4/billing.dsl"),
            "where arc42 has a workspace, a building block lists workspace.dsl and the files it includes",
            dump(withIt)
        );
    } finally {
        await rm(bare, { recursive: true, force: true });
        await rm(withWorkspace, { recursive: true, force: true });
    }
}

if (failed) {
    console.log(`\n${failed} check(s) failed.`);
    process.exit(1);
}
console.log("\nAll units checks passed.");
