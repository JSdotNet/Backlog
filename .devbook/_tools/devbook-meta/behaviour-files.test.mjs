// Exercises `requirements.md` and the invariants subpages — `domain.invariants.md`
// and `domain.<name>.invariants.md`: the four chapter types and two file types,
// the naming rules for a subpage, the three coverage warnings over a rule
// chapter, and the typed `related` pairing that joins a behaviour chapter to its
// prose half.
//
// The coverage checks are warnings by design, so every case here asserts the
// severity as well as the count — a regression that promoted one to an error
// would otherwise pass as "still reported".
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { behaviourIssues, domainFileName, scenarioCount, parseDocument, typeIssues, validateDocument } from "./metadata.mjs";
import { buildGraph } from "./graph.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};
const counts = (issues) => ({
    errors: issues.filter((i) => i.severity === "error").length,
    warnings: issues.filter((i) => i.severity === "warning").length,
});

// ── The four chapter types and the two file types are accepted ─────────────

for (const type of ["requirements", "requirement", "invariants", "invariant"]) {
    const { errors } = counts(typeIssues("domain", "chapter", { type }));
    check(errors === 0, `\`type: ${type}\` is a legal .domain chapter type`, `got ${errors} error(s)`);
}

for (const type of ["requirements", "invariants"]) {
    const { errors } = counts(typeIssues("domain", "file", { type }, type));
    check(errors === 0, `\`type: ${type}\` is a legal .domain file type`, `got ${errors} error(s)`);
}

// ── What a filename says ───────────────────────────────────────────────────

const naming = [
    ["domain.md", { base: "domain", page: null, legacy: false }],
    ["domain.order.md", { base: "domain", page: null, legacy: false }],
    ["domain.invariants.md", { base: "invariants", page: "domain.md", legacy: false }],
    ["domain.order.invariants.md", { base: "invariants", page: "domain.order.md", legacy: false }],
    ["invariants.md", { base: "invariants", page: null, legacy: true }],
    ["invariants.order.md", { base: "invariants", page: null, legacy: true }],
    ["requirements.checkout.md", { base: "requirements", page: null, legacy: false }],
];
for (const [name, expected] of naming) {
    const actual = domainFileName(`.devbook/domain/ordering/${name}`);
    check(
        JSON.stringify(actual) === JSON.stringify(expected),
        `\`${name}\` reads as ${expected.page ? `the invariants subpage of ${expected.page}` : `\`${expected.base}\``}${expected.legacy ? ", legacy" : ""}`,
        JSON.stringify(actual)
    );
}

const fileOnly = (type) => `# Ordering

\`\`\`meta
type: ${type}
\`\`\`

A file.
`;
const fileCases = [
    { name: "`domain.invariants.md` typed `invariants` is clean", file: "domain.invariants.md", type: "invariants", errors: 0, warnings: 0 },
    { name: "a split page's subpage is clean", file: "domain.order.invariants.md", type: "invariants", errors: 0, warnings: 0 },
    { name: "a subpage typed as its page is an error", file: "domain.invariants.md", type: "domain", errors: 1, warnings: 0 },
    { name: "a subpage of a page that is not a domain page is an error", file: "model.invariants.md", type: "invariants", errors: 1, warnings: 0 },
    { name: "a legacy `invariants.md` warns and does not fail", file: "invariants.md", type: "invariants", errors: 0, warnings: 1 },
    { name: "a legacy split `invariants.<name>.md` warns too", file: "invariants.order.md", type: "invariants", errors: 0, warnings: 1 },
];
for (const c of fileCases) {
    const issues = validateDocument(`.devbook/domain/ordering/${c.file}`, fileOnly(c.type));
    const { errors, warnings } = counts(issues);
    check(
        errors === c.errors && warnings === c.warnings,
        `naming: ${c.name}`,
        issues.map((i) => `${i.severity}: ${i.message}`).join(" | ")
    );
}

check(
    counts(typeIssues("domain", "chapter", { type: "requirment" })).errors === 1,
    "a misspelled type is still an error"
);

// ── Counting scenarios ─────────────────────────────────────────────────────

const ruleWith = (scenarios) =>
    `### Requirement: Something\n\n\`\`\`meta\ntype: requirement\n\`\`\`\n\nThe system SHALL do it.\n\n` +
    scenarios.map((s) => `#### Scenario: ${s}\n\n- **Given** a\n- **When** b\n- **Then** c\n\n`).join("");

const scenarioCases = [
    { name: "no scenarios", body: ruleWith([]), expected: 0 },
    { name: "one scenario", body: ruleWith(["the happy path"]), expected: 1 },
    { name: "three scenarios", body: ruleWith(["a", "b", "c"]), expected: 3 },
    {
        name: "a deeper heading under a scenario is not a second scenario",
        body: ruleWith(["a"]) + "##### Scenario: nested\n\n",
        expected: 1,
    },
    {
        name: "a sibling heading that is not a scenario does not count",
        body: ruleWith(["a"]) + "#### Notes\n\n",
        expected: 1,
    },
    {
        name: "the next rule's scenarios belong to the next rule",
        body: ruleWith(["a"]) + ruleWith(["b", "c"]),
        expected: 1,
    },
];

for (const c of scenarioCases) {
    const { chapters } = parseDocument(`# Ordering\n\n\`\`\`meta\ntype: requirements\n\`\`\`\n\n${c.body}`);
    const index = chapters.findIndex((ch) => ch.meta?.type === "requirement");
    const actual = scenarioCount(chapters, index);
    check(actual === c.expected, `scenarios: ${c.name}`, `got ${actual}, expected ${c.expected}`);
}

// ── The coverage warnings ──────────────────────────────────────────────────

const coverage = [
    {
        name: "a requirement proved e2e, with a scenario, is clean",
        type: "requirement",
        meta: { tests: "e2e:playwright:a.spec.ts" },
        scenarios: 1,
        warnings: 0,
    },
    {
        name: "a requirement proved integration is clean — a policy no user triggers",
        type: "requirement",
        meta: { tests: "integration:dotnet:A.B" },
        scenarios: 1,
        warnings: 0,
    },
    {
        name: "a requirement backed only by unit tests warns",
        type: "requirement",
        meta: { tests: "unit:dotnet:A.B" },
        scenarios: 1,
        warnings: 1,
    },
    {
        name: "an invariant proved unit, with a scenario, is clean",
        type: "invariant",
        meta: { tests: "unit:dotnet:A.B" },
        scenarios: 1,
        warnings: 0,
    },
    {
        name: "an invariant backed only by e2e warns",
        type: "invariant",
        meta: { tests: "e2e:playwright:a.spec.ts" },
        scenarios: 1,
        warnings: 1,
    },
    {
        name: "one entry at the right level is enough",
        type: "invariant",
        meta: { tests: ["e2e:playwright:a.spec.ts", "unit:dotnet:A.B"] },
        scenarios: 1,
        warnings: 0,
    },
    {
        name: "no tests at all is not a coverage claim, so nothing is reported",
        type: "invariant",
        meta: {},
        scenarios: 1,
        warnings: 0,
    },
    {
        name: "a rule with no scenario warns",
        type: "requirement",
        meta: { tests: "e2e:playwright:a.spec.ts" },
        scenarios: 0,
        warnings: 1,
    },
    {
        name: "both gaps at once are two warnings",
        type: "invariant",
        meta: { tests: "e2e:playwright:a.spec.ts" },
        scenarios: 0,
        warnings: 2,
    },
    {
        name: "a grouping chapter is not a rule and is not held to either",
        type: "requirements",
        meta: {},
        scenarios: 0,
        warnings: 0,
    },
    {
        name: "an aggregate is not a rule chapter",
        type: "aggregate",
        meta: { tests: "e2e:playwright:a.spec.ts" },
        scenarios: 0,
        warnings: 0,
    },
];

for (const c of coverage) {
    const issues = behaviourIssues(c.type, c.meta, c.scenarios);
    const { errors, warnings } = counts(issues);
    check(
        warnings === c.warnings && errors === 0,
        `coverage: ${c.name}`,
        `got ${errors} error(s), ${warnings} warning(s): ${issues.map((i) => i.message).join(" | ")}`
    );
}

// A malformed `tests` entry is `testIssues`' error to report, not a coverage
// claim this check should read a level out of.
check(
    counts(behaviourIssues("invariant", { tests: "OrderTests" }, 1)).warnings === 0,
    "a malformed `tests` entry raises no coverage warning of its own"
);

// ── The warnings reach a real document without becoming errors ─────────────

const document = `# Ordering

\`\`\`meta
type: invariants
\`\`\`

## Order

\`\`\`meta
type: invariants
related: [.devbook/domain/ordering/domain.md#order]
\`\`\`

### Invariant: An order cannot be confirmed twice

\`\`\`meta
type: invariant
tests: e2e:playwright:a.spec.ts
\`\`\`

An order that has been confirmed cannot be confirmed again.

Enforced at: Confirm()
`;

const documentIssues = validateDocument(".devbook/domain/ordering/domain.invariants.md", document);
const behaviour = documentIssues.filter((i) => /`invariant` chapter/.test(i.message));
check(
    behaviour.length === 2 && behaviour.every((i) => i.severity === "warning"),
    "both coverage gaps surface through validateDocument, as warnings",
    JSON.stringify(documentIssues.map((i) => `${i.severity}: ${i.message}`))
);
check(
    counts(documentIssues).errors === 0,
    "and an incomplete rule chapter never fails the document",
    JSON.stringify(documentIssues.filter((i) => i.severity === "error").map((i) => i.message))
);

// ── The typed `related` pairing ────────────────────────────────────────────

const CONTEXT = ".devbook/domain/ordering";

async function graphOf(files) {
    const root = await mkdtemp(path.join(tmpdir(), "devbook-behaviour-"));
    try {
        for (const [rel, body] of Object.entries(files)) {
            await mkdir(path.join(root, path.dirname(rel)), { recursive: true });
            await writeFile(path.join(root, rel), body, "utf8");
        }
        return await buildGraph(root, [".devbook/domain"]);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

const domainMd = `# Ordering

\`\`\`meta
type: domain
\`\`\`

## Order

\`\`\`meta
type: aggregate
\`\`\`

The order.

## Pricing

\`\`\`meta
type: domain-service
\`\`\`

Prices things.
`;

const featuresMd = `# Ordering

\`\`\`meta
type: features
\`\`\`

## Checkout

\`\`\`meta
type: feature
\`\`\`

Checking out.
`;

const invariantsMd = (related) => `# Ordering

\`\`\`meta
type: invariants
\`\`\`

## Order

\`\`\`meta
type: invariants${related ? `\nrelated: [${related}]` : ""}
\`\`\`

The rules.
`;

const pairing = [
    {
        name: "an `invariants` chapter pointing at its aggregate resolves",
        related: `${CONTEXT}/domain.md#order`,
        errors: 0,
    },
    {
        name: "an `invariants` chapter may point at a domain service instead",
        related: `${CONTEXT}/domain.md#pricing`,
        errors: 0,
    },
    {
        name: "one right target among several is enough",
        related: `${CONTEXT}/features.md#checkout, ${CONTEXT}/domain.md#order`,
        errors: 0,
    },
    {
        name: "an `invariants` chapter pointing at a feature is an error",
        related: `${CONTEXT}/features.md#checkout`,
        errors: 1,
    },
    {
        name: "an `invariants` chapter with no `related` at all is an error",
        related: null,
        errors: 1,
    },
];

for (const c of pairing) {
    const graph = await graphOf({
        [`${CONTEXT}/domain.md`]: domainMd,
        [`${CONTEXT}/features.md`]: featuresMd,
        [`${CONTEXT}/domain.invariants.md`]: invariantsMd(c.related),
    });
    const found = graph.problems.filter((p) => /names no .* chapter/.test(p.message));
    check(
        found.length === c.errors && found.every((p) => p.severity === "error"),
        `pairing: ${c.name}`,
        `got ${found.length}: ${found.map((p) => p.message).join(" | ")}`
    );
}

// The file-level block carries the same `type` word and covers the whole
// context, so it is deliberately exempt — without this the pairing check would
// fail every `requirements.md` and `domain.invariants.md` ever written.
const fileLevel = await graphOf({
    [`${CONTEXT}/domain.md`]: domainMd,
    [`${CONTEXT}/domain.invariants.md`]: invariantsMd(`${CONTEXT}/domain.md#order`),
});
check(
    fileLevel.problems.filter((p) => /names no .* chapter/.test(p.message)).length === 0,
    "the file-level block is not held to the pairing",
    JSON.stringify(fileLevel.problems.map((p) => p.message))
);

// A `requirements` chapter is the mirror case, and pairs with a feature.
const requirementsGraph = await graphOf({
    [`${CONTEXT}/domain.md`]: domainMd,
    [`${CONTEXT}/features.md`]: featuresMd,
    [`${CONTEXT}/requirements.md`]: `# Ordering

\`\`\`meta
type: requirements
\`\`\`

## Checkout

\`\`\`meta
type: requirements
related: [${CONTEXT}/features.md#checkout]
\`\`\`

The promises.
`,
});
check(
    requirementsGraph.problems.filter((p) => /names no .* chapter/.test(p.message)).length === 0,
    "a `requirements` chapter pointing at its feature resolves",
    JSON.stringify(requirementsGraph.problems.map((p) => p.message))
);

// ── A subpage holds the rules of its own page's aggregates ─────────────────

const splitOrder = `# Ordering

\`\`\`meta
type: domain
\`\`\`

## Order

\`\`\`meta
type: aggregate
\`\`\`

The order.
`;
const placement = [
    {
        name: "a split page's subpage pairing with that page is clean",
        file: "domain.order.invariants.md",
        related: `${CONTEXT}/domain.order.md#order`,
        warnings: 0,
    },
    {
        name: "a split page's subpage pairing with another page warns",
        file: "domain.order.invariants.md",
        related: `${CONTEXT}/domain.md#pricing`,
        warnings: 1,
    },
    {
        name: "`domain.invariants.md` holding a split-out aggregate's rules warns",
        file: "domain.invariants.md",
        related: `${CONTEXT}/domain.order.md#order`,
        warnings: 1,
    },
];
for (const c of placement) {
    const graph = await graphOf({
        [`${CONTEXT}/domain.md`]: domainMd.replace(/## Order[\s\S]*?(?=## Pricing)/, ""),
        [`${CONTEXT}/domain.order.md`]: splitOrder,
        [`${CONTEXT}/${c.file}`]: invariantsMd(c.related),
    });
    const found = graph.problems.filter((p) => /invariants subpage of/.test(p.message));
    check(
        found.length === c.warnings && found.every((p) => p.severity === "warning") &&
            graph.problems.every((p) => p.severity !== "error"),
        `placement: ${c.name}`,
        JSON.stringify(graph.problems.map((p) => `${p.severity}: ${p.message}`))
    );
}

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
