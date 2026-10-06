// Asserts the `sync` field: its five values, the four levels it may be set at,
// the owned chapters and owned-only pages that refuse it, a value no unit
// inherits, and the `related` a domain event names its raiser by.
//
// Run: `node sync-field.test.mjs`
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { validateDocument, syncLevel, syncSources } from "./metadata.mjs";
import { buildGraph } from "./graph.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};

const fence = (body) => "```meta\n" + body + "```\n";
const find = (issues, severity, needle) =>
    issues.find((i) => i.severity === severity && i.message.includes(needle));
const syncProblems = (issues) => issues.filter((i) => i.message.includes("`sync`"));
const dump = (issues) => JSON.stringify(issues, null, 2);

const CONTEXT = ".devbook/domain/ordering";

// --- The value ------------------------------------------------------------

{
    for (const value of ["push", "pull", "sync", "report", "off"]) {
        const issues = validateDocument(
            `${CONTEXT}/domain.md`,
            `# Domain\n\n${fence("type: domain\n")}\n## Order\n\n${fence(`type: aggregate\nsync: ${value}\n`)}\nProse.\n`
        );
        check(!syncProblems(issues).length, `\`sync: ${value}\` on an aggregate is accepted`, dump(issues));
    }

    const issues = validateDocument(
        `${CONTEXT}/domain.md`,
        `# Domain\n\n${fence("type: domain\n")}\n## Order\n\n${fence("type: aggregate\nsync: both\n")}\nProse.\n`
    );
    check(Boolean(find(issues, "error", '`sync` "both", expected one of')), "a misspelt direction is an error", dump(issues));

    const empty = validateDocument(
        `${CONTEXT}/domain.md`,
        `# Domain\n\n${fence("type: domain\n")}\n## Order\n\n${fence("type: aggregate\nsync:\n")}\nProse.\n`
    );
    check(Boolean(find(empty, "warning", "`sync` to an empty/null value")), "an empty `sync` is a warning", dump(empty));
}

// --- The four levels ------------------------------------------------------

{
    const cases = [
        [".devbook/domain/context-map.md", "file", { type: "context-map" }, "folder"],
        [`${CONTEXT}/context.md`, "file", { type: "context" }, "context"],
        [`${CONTEXT}/domain.md`, "file", { type: "domain" }, "page"],
        [`${CONTEXT}/domain.payments.md`, "file", { type: "domain" }, "page"],
        [`${CONTEXT}/features.md`, "file", { type: "features" }, "page"],
        [`${CONTEXT}/skills.md`, "file", { type: "skills" }, "page"],
        [`${CONTEXT}/actors.md`, "file", { type: "actors" }, "page"],
        [`${CONTEXT}/domain.md`, "chapter", { type: "aggregate" }, "unit"],
        [`${CONTEXT}/domain.md`, "chapter", { type: "domain-service" }, "unit"],
        [`${CONTEXT}/features.md`, "chapter", { type: "feature" }, "unit"],
        [`${CONTEXT}/context.md`, "chapter", { type: "feature-flag" }, "unit"],
        [`${CONTEXT}/context.md`, "chapter", { type: "setting" }, "unit"],
        [`${CONTEXT}/actors.md`, "chapter", { type: "user" }, "unit"],
        [`${CONTEXT}/actors.md`, "chapter", { type: "technical" }, "unit"],
        [`${CONTEXT}/context.md`, "chapter", { type: "user" }, "unit"],
        [".devbook/arc42/05-building-block-view.md", "file", {}, "folder"],
        [".devbook/arc42/building-blocks/ordering.md", "file", {}, "unit"],
        [".devbook/design/component-libraries.md", "file", {}, "folder"],
        [".devbook/design/component-libraries.md", "chapter", {}, "unit"],
    ];
    for (const [relPath, blockLevel, meta, level] of cases) {
        const got = syncLevel(relPath, blockLevel, meta);
        check(got.level === level, `${relPath} (${blockLevel}, ${meta.type ?? "untyped"}) is the ${level} level`, JSON.stringify(got));
    }
}

// --- Where it is refused --------------------------------------------------

{
    for (const type of ["entity", "value-object", "enum", "domain-event", "term"]) {
        const issues = validateDocument(
            `${CONTEXT}/domain.md`,
            `# Domain\n\n${fence("type: domain\n")}\n## Thing\n\n${fence(`type: ${type}\nsync: push\n`)}\nProse.\n`
        );
        check(Boolean(find(issues, "error", `\`sync\` on a \`${type}\` chapter`)), `\`sync\` on a ${type} is refused`, dump(issues));
    }

    const sub = validateDocument(
        `${CONTEXT}/features.md`,
        `# Features\n\n${fence("type: features\n")}\n## Checkout\n\n${fence("type: feature\n")}\nProse.\n\n### Guest\n\n${fence("type: sub-feature\nsync: pull\n")}\nProse.\n`
    );
    check(Boolean(find(sub, "error", "`sync` on a `sub-feature` chapter")), "`sync` on a sub-feature is refused", dump(sub));

    for (const page of ["requirements.md", "requirements.checkout.md", "domain.invariants.md", "domain.payments.invariants.md"]) {
        const type = page.includes("invariants") ? "invariants" : "requirements";
        const issues = validateDocument(
            `${CONTEXT}/${page}`,
            `# ${type}\n\n${fence(`type: ${type}\nsync: pull\n`)}\nProse.\n`
        );
        check(Boolean(find(issues, "error", "holds only chapters a unit on another page owns")), `\`sync\` on ${page} is refused`, dump(issues));
    }

    const rule = validateDocument(
        `${CONTEXT}/requirements.md`,
        `# Requirements\n\n${fence("type: requirements\n")}\n## Checkout\n\n${fence("type: requirements\n")}\n### Requirement: Pay once\n\n${fence("type: requirement\nsync: push\n")}\nThe system SHALL charge once.\n`
    );
    check(Boolean(find(rule, "error", "holds only chapters a unit on another page owns")), "`sync` on a requirement is refused", dump(rule));

    const elsewhere = [
        [`${CONTEXT}/model.md`, `# Model\n\n${fence("type: model\nsync: pull\n")}`],
        [`${CONTEXT}/actors.md`, `# Actors\n\n${fence("type: actors\n")}\n## Card Issuer\n\n${fence("type: organisation\nsync: pull\n")}\nProse.\n`],
        [".devbook/arc42/01-introduction-and-goals.md", `# Introduction\n\n${fence("sync: pull\n")}`],
        [".devbook/arc42/building-blocks/README.md", `# Building Blocks\n\n${fence("index: root\nsync: pull\n")}`],
        [".devbook/design/color-scheme.md", `# Colors\n\n${fence("sync: pull\n")}`],
        [".devbook/tech/frameworks.md", `# Frameworks\n\n${fence("status: adopt\nsync: pull\n")}`],
    ];
    for (const [relPath, markdown] of elsewhere) {
        const issues = validateDocument(relPath, markdown);
        check(Boolean(find(issues, "error", "no sync level")), `\`sync\` on ${relPath} is refused`, dump(issues));
        check(!find(issues, "warning", "unrecognized field `sync`"), `\`sync\` on ${relPath} is reported once`, dump(issues));
    }

    const section = validateDocument(
        ".devbook/arc42/building-blocks/ordering.md",
        `# Ordering\n\n${fence("")}\nProse.\n\n## Interfaces\n\n${fence("sync: push\n")}\nProse.\n`
    );
    check(Boolean(find(section, "error", "no sync level")), "`sync` on a building block's section is refused", dump(section));
}

// --- Nearest wins ---------------------------------------------------------

{
    const sources = syncSources(`${CONTEXT}/domain.md#order`);
    check(
        JSON.stringify(sources) ===
            JSON.stringify([`${CONTEXT}/domain.md#order`, `${CONTEXT}/domain.md`, `${CONTEXT}/context.md`, ".devbook/domain/context-map.md"]),
        "an aggregate reads unit, page, context, folder in that order",
        JSON.stringify(sources)
    );
    const flag = syncSources(`${CONTEXT}/context.md#dark-mode`);
    check(flag.length === 3, "a switch chapter's page is its context, listed once", JSON.stringify(flag));
    const block = syncSources(".devbook/arc42/building-blocks/ordering.md");
    check(
        JSON.stringify(block) === JSON.stringify([".devbook/arc42/building-blocks/ordering.md", ".devbook/arc42/05-building-block-view.md"]),
        "a building block reads itself, then the building block view",
        JSON.stringify(block)
    );
}

// --- Across files: inheritance and the raising aggregate ------------------

async function graphOf(files, folders) {
    const root = await mkdtemp(path.join(tmpdir(), "devbook-sync-"));
    try {
        for (const [rel, body] of Object.entries(files)) {
            await mkdir(path.join(root, path.dirname(rel)), { recursive: true });
            await writeFile(path.join(root, rel), body, "utf8");
        }
        return await buildGraph(root, folders);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

const domainMd = (orderSync = "", eventRelated = `related: [${CONTEXT}/domain.md#order]\n`) =>
    `# Domain\n\n${fence("type: domain\n")}\n## Order\n\n${fence(`type: aggregate\n${orderSync}`)}\nProse.\n\n` +
    `## Order Placed\n\n${fence(`type: domain-event\n${eventRelated}`)}\nPublished when an order is placed.\n`;

{
    const { problems } = await graphOf(
        {
            [`${CONTEXT}/context.md`]: `# Ordering\n\n${fence("type: context\nsync: pull\n")}\nProse.\n`,
            [`${CONTEXT}/domain.md`]: domainMd(),
        },
        [".devbook/domain"]
    );
    check(!problems.some((p) => p.message.includes("no unit inherits")), "a context value an aggregate inherits is silent", dump(problems));
    check(!problems.some((p) => p.message.includes("`domain-event`")), "an event naming its aggregate is silent", dump(problems));
}

{
    const { problems } = await graphOf(
        {
            [`${CONTEXT}/context.md`]: `# Ordering\n\n${fence("type: context\nsync: pull\n")}\nProse.\n`,
            [`${CONTEXT}/domain.md`]: domainMd("sync: push\n"),
        },
        [".devbook/domain"]
    );
    check(
        problems.some((p) => p.severity === "warning" && p.message.includes(`${CONTEXT}/context.md has \`sync: pull\`, which no unit inherits`)),
        "a context value every unit overrides is reported",
        dump(problems)
    );
}

{
    const { problems } = await graphOf(
        {
            [`${CONTEXT}/actors.md`]: `# Actors\n\n${fence("type: actors\nsync: pull\n")}\n## Clerk\n\n${fence("type: user\n")}\nProse.\n`,
        },
        [".devbook/domain"]
    );
    check(
        !problems.some((p) => p.message.includes("which no unit inherits")),
        "`actors.md`'s direction is inherited by the user actor on it",
        dump(problems)
    );
    check(!problems.some((p) => p.severity === "error"), "`actors.md` carrying `sync` is not an error", dump(problems));
}

{
    const { problems } = await graphOf(
        {
            [`${CONTEXT}/actors.md`]: `# Actors\n\n${fence("type: actors\nsync: pull\n")}\n## Card Issuer\n\n${fence("type: organisation\n")}\nProse.\n`,
        },
        [".devbook/domain"]
    );
    check(
        problems.some((p) => p.severity === "warning" && p.message.includes(`${CONTEXT}/actors.md has \`sync: pull\`, which no unit inherits`)),
        "an `actors.md` of organisations only is allowed, and inherited by nothing",
        dump(problems)
    );
}

{
    const { problems } = await graphOf({ [`${CONTEXT}/domain.md`]: domainMd("", "") }, [".devbook/domain"]);
    check(
        problems.some((p) => p.severity === "warning" && p.message.includes("is a `domain-event` chapter whose `related` names no")),
        "an event naming no raiser is reported",
        dump(problems)
    );
}

{
    const { problems } = await graphOf(
        {
            ".devbook/arc42/05-building-block-view.md": `# Building Block View\n\n${fence("sync: pull\n")}\nProse.\n`,
            ".devbook/arc42/building-blocks/README.md": `# Building Blocks\n\n${fence("index: root\n")}\nProse.\n`,
            ".devbook/arc42/building-blocks/ordering.md": `# Ordering\n\n${fence("")}\nProse.\n`,
        },
        [".devbook/arc42"]
    );
    check(!problems.some((p) => p.message.includes("sync")), "a building block inherits the building block view's direction", dump(problems));
}

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
