// Exercises the `tests` field: reference parsing, the run-command mapping every
// UI affordance sits on, and the lint that separates a real test identifier
// from a chapter reference pasted into the wrong field.
import { parseTestReference, testCommand, testIssues } from "./metadata.mjs";

const parses = [
    {
        name: "dotnet fully-qualified method",
        ref: "unit:dotnet:Ordering.Domain.Tests.OrderTests.CannotConfirmCancelledOrder",
        command: [
            "dotnet",
            "test",
            "--filter",
            "FullyQualifiedName~Ordering.Domain.Tests.OrderTests.CannotConfirmCancelledOrder",
        ],
    },
    {
        name: "dotnet test class, integration level",
        ref: "integration:dotnet:Ordering.Api.Tests.CheckoutEndpointTests",
        command: ["dotnet", "test", "--filter", "FullyQualifiedName~Ordering.Api.Tests.CheckoutEndpointTests"],
    },
    {
        name: "playwright spec with a title",
        ref: "e2e:playwright:tests/e2e/checkout.spec.ts#Guest checkout completes",
        command: ["npx", "playwright", "test", "tests/e2e/checkout.spec.ts", "-g", "Guest checkout completes"],
    },
    {
        name: "playwright whole spec, no title",
        ref: "e2e:playwright:tests/e2e/checkout.spec.ts",
        command: ["npx", "playwright", "test", "tests/e2e/checkout.spec.ts"],
    },
    {
        name: "vitest spec with a name",
        ref: "unit:vitest:src/cart.test.ts#applies the discount once",
        command: ["npx", "vitest", "run", "src/cart.test.ts", "-t", "applies the discount once"],
    },
    {
        name: "jest spec",
        ref: "unit:jest:src/cart.test.ts",
        command: ["npx", "jest", "src/cart.test.ts"],
    },
    {
        name: "pytest node id, whose own colons stay in the selector",
        ref: "unit:pytest:tests/test_orders.py::TestOrder::test_cannot_confirm_cancelled",
        command: ["pytest", "tests/test_orders.py::TestOrder::test_cannot_confirm_cancelled"],
    },
];

const malformed = [
    { name: "no runner or selector", ref: "OrderTests" },
    { name: "level and runner only", ref: "unit:dotnet" },
    { name: "empty selector", ref: "unit:dotnet:" },
    { name: "empty runner", ref: "unit::OrderTests" },
    { name: "missing level", ref: ":dotnet:OrderTests" },
];

const lints = [
    { name: "a valid pair reports nothing", tests: ["unit:dotnet:A.B", "e2e:playwright:a.spec.ts"], errors: 0, warnings: 0 },
    { name: "a scalar entry is accepted", tests: "unit:dotnet:A.B", errors: 0, warnings: 0 },
    { name: "an unknown level is an error", tests: ["smoke:dotnet:A.B"], errors: 1, warnings: 0 },
    { name: "an unknown runner is only a warning", tests: ["e2e:cypress:a.cy.ts"], errors: 0, warnings: 1 },
    { name: "a chapter reference is an error", tests: [".devbook/domain/ordering/domain.md#order"], errors: 1, warnings: 0 },
    { name: "a malformed entry is an error", tests: ["OrderTests"], errors: 1, warnings: 0 },
    { name: "no field at all reports nothing", tests: undefined, errors: 0, warnings: 0 },
];

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};

for (const c of parses) {
    const built = testCommand(c.ref);
    const actual = built ? built.command.join(" \u2423 ") : "null";
    check(actual === c.command.join(" \u2423 "), c.name, `got ${actual}`);
}

for (const c of malformed) {
    check(parseTestReference(c.ref) === null && testCommand(c.ref) === null, c.name, "parsed when it should not");
}

for (const c of lints) {
    const issues = testIssues(c.tests === undefined ? {} : { tests: c.tests });
    const errors = issues.filter((i) => i.severity === "error").length;
    const warnings = issues.filter((i) => i.severity === "warning").length;
    check(
        errors === c.errors && warnings === c.warnings,
        c.name,
        `got ${errors} error(s), ${warnings} warning(s): ${issues.map((i) => i.message).join(" | ")}`
    );
}

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
