// Asserts that the review triad — `review`, `reviewer`, `review-at` — is gone
// from the schema since contract 21. A review in progress is the chapter's
// `status` rung plus its open annotation fences; who owes the next move is the
// pull request's or the tracker's. A leftover field is reported by name, with
// the migration that deletes it, in every folder.
//
// Run: `node review-state.test.mjs`
import { validateDocument } from "./metadata.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};

const fence = (body) => "```meta\n" + body + "```\n";
const find = (issues, severity, needle) =>
    issues.find((i) => i.severity === severity && i.message.includes(needle));
const dump = (issues) => JSON.stringify(issues, null, 2);
const triad = 'review: requested\nreviewer: "@reviewer"\nreview-at: 2026-09-17\n';
const domain = (meta) =>
    `# Ordering\n\n${fence("type: domain\n")}\n## Order\n\n${fence("type: aggregate\n" + meta)}\nProse.\n`;

{
    const issues = validateDocument(".devbook/domain/ordering/domain.md", domain(`status: draft\n${triad}`));
    for (const field of ["review", "reviewer", "review-at"]) {
        check(
            Boolean(find(issues, "error", `has \`${field}\`, which is no longer part of the metadata schema`)),
            `a leftover \`${field}\` is reported as removed`,
            dump(issues)
        );
    }
    check(
        issues.some((i) => i.message.includes("021-no-review-triad")),
        "the report names the migration that deletes it",
        dump(issues)
    );
    check(
        !issues.some((i) => i.message.includes("unrecognized field `review")),
        "a removed field is reported once, not again as unrecognized",
        dump(issues)
    );
}

{
    const doc = `# Context\n\n${fence("type: file\n" + triad)}\nProse.\n`;
    const issues = validateDocument(".devbook/arc42/03-context.md", doc);
    check(
        Boolean(find(issues, "error", "has `review`, which is no longer part of the metadata schema")),
        "the triad is gone from every folder, not only domain/",
        dump(issues)
    );
}

{
    const issues = validateDocument(".devbook/domain/ordering/domain.md", domain("status: draft\n"));
    check(!issues.some((i) => i.message.includes("review")), "a chapter without the fields reports nothing about them", dump(issues));
}

if (failed) {
    console.log(`\n${failed} failed`);
    process.exit(1);
}
console.log("\nall passed");
