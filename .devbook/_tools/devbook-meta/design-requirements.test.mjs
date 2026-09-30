// Exercises `.design`'s one chapter type: a rule a component either keeps or
// breaks is a `### Requirement:` chapter, `type: requirement`, with
// `#### Scenario:` cases, under the component's own chapter. Every other
// `.design` chapter stays untyped, and the requirement is held to `e2e`.
//
// The coverage checks are warnings by design, so every case asserts the
// severity as well as the count.
import { behaviourIssues, typeIssues, validateDocument } from "./metadata.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};
const counts = (issues) => ({
    errors: issues.filter((i) => i.severity === "error").length,
    warnings: issues.filter((i) => i.severity === "warning").length,
});

// ── The value set ──────────────────────────────────────────────────────────

const typeCases = [
    { name: "`type: requirement` is a legal .design chapter type", level: "chapter", meta: { type: "requirement" }, errors: 0, warnings: 0 },
    { name: "an untyped .design chapter is still clean", level: "chapter", meta: {}, errors: 0, warnings: 0 },
    { name: "a .domain type on a .design chapter is an error", level: "chapter", meta: { type: "aggregate" }, errors: 1, warnings: 0 },
    { name: "`requirements` is not a .design type — the component chapter groups them", level: "chapter", meta: { type: "requirements" }, errors: 1, warnings: 0 },
    { name: "a .design file still takes no `type`", level: "file", meta: { type: "requirement" }, errors: 0, warnings: 1 },
];
for (const c of typeCases) {
    const { errors, warnings } = counts(typeIssues("design", c.level, c.meta));
    check(errors === c.errors && warnings === c.warnings, c.name, `got ${errors} error(s), ${warnings} warning(s)`);
}

// ── The coverage warnings ──────────────────────────────────────────────────

const coverage = [
    { name: "a design requirement proved `e2e` with a scenario is clean", meta: { tests: "e2e:playwright:tests/drag.spec.ts#reorders by keyboard" }, scenarios: 1, warnings: 0 },
    { name: "a design requirement with no scenario warns", meta: {}, scenarios: 0, warnings: 1 },
    { name: "a design requirement proved only `unit` warns", meta: { tests: "unit:vitest:src/contrast.test.ts" }, scenarios: 1, warnings: 1 },
    { name: "a design requirement proved only `integration` warns — `.domain`'s policy exception is not `.design`'s", meta: { tests: "integration:vitest:src/button.test.ts" }, scenarios: 1, warnings: 1 },
    { name: "one `e2e` entry among others is enough", meta: { tests: ["unit:vitest:src/contrast.test.ts", "e2e:playwright:tests/visual.spec.ts"] }, scenarios: 2, warnings: 0 },
];
for (const c of coverage) {
    const issues = behaviourIssues("requirement", c.meta, c.scenarios, "design");
    const { errors, warnings } = counts(issues);
    check(errors === 0 && warnings === c.warnings, `coverage: ${c.name}`, issues.map((i) => i.message).join(" | "));
}

check(
    counts(behaviourIssues("requirement", { tests: "integration:dotnet:Ordering.PolicyTests" }, 1, "domain")).warnings === 0,
    "coverage: a .domain requirement still accepts `integration`"
);
check(
    behaviourIssues("invariant", { tests: "e2e:playwright:tests/x.spec.ts" }, 0, "design").length === 0,
    "coverage: `.design` has no invariant kind to warn about"
);

// ── A real component chapter ───────────────────────────────────────────────

const component = (tests) => `# Component Libraries

\`\`\`meta
\`\`\`

## Sortable List

\`\`\`meta
\`\`\`

The list the product reorders items in.

### Requirement: Reorder by keyboard

\`\`\`meta
type: requirement
tests: ${tests}
\`\`\`

The sortable list SHALL let every item be moved without a pointer.

#### Scenario: Move an item down

- **Given** an item that is not the last one has focus
- **When** the person presses Alt+ArrowDown
- **Then** the item moves one place down and keeps focus
`;

const clean = validateDocument(".devbook/design/component-libraries.md", component("e2e:playwright:tests/sortable.spec.ts"));
check(counts(clean).errors === 0 && counts(clean).warnings === 0, "document: a typed requirement under its component is clean", clean.map((i) => `${i.severity}: ${i.message}`).join(" | "));

const unitOnly = validateDocument(".devbook/design/component-libraries.md", component("unit:vitest:src/sortable.test.ts"));
check(
    counts(unitOnly).errors === 0 && counts(unitOnly).warnings === 1 && /design requirement is proved `e2e`/.test(unitOnly[0]?.message),
    "document: a `unit`-only design requirement surfaces through validateDocument as one warning",
    unitOnly.map((i) => `${i.severity}: ${i.message}`).join(" | ")
);

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
