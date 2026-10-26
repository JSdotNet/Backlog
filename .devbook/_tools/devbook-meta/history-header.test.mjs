// history-header.test.mjs — a decision record's `## History` header, per devbook-arc42.md.
//
// An `arc42/adr/` record other than the folder's index holds a `## History`
// table headed `Date | Change | Why`. Anything else is a warning, never an
// error: a record written before the header was fixed needs a person to split
// each row's reason out, and keeps validating until one does.
//
// Run: node plugins/devbook/tools/devbook-meta/history-header.test.mjs
import { validateDocument, historyHeaderIssues, HISTORY_HEADER } from "./metadata.mjs";

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

const ADR = ".devbook/arc42/adr/storage.md";
const record = (history) => `# Storage\n\n${fence("date: 2026-10-20\n")}\nOne store.\n\n## Why\n\n${fence()}\nBecause.\n${history}`;
const section = (table) => `\n## History\n\n${fence()}\n${table}`;
const THREE = "| Date | Change | Why |\n| --- | --- | --- |\n| 2026-10-20 | One store. | Two drifted. |\n";
const TWO = "| Date | Change |\n| --- | --- |\n| 2026-10-20 | One store, because two drifted. |\n";

check(HISTORY_HEADER.join(" | ") === "Date | Change | Why", "the fixed header is Date | Change | Why");

{
    const issues = validateDocument(ADR, record(section(THREE)));
    check(errors(issues).length === 0 && warnings(issues).length === 0, "a record headed Date | Change | Why is clean", dump(issues));
}
{
    const issues = validateDocument(ADR, record(section(TWO)));
    check(has(warnings(issues), "table headed `Date | Change` on line") && errors(issues).length === 0, "a two-column History table is a warning, never an error", dump(issues));
}
{
    const issues = validateDocument(ADR, record(section("No table yet.\n")));
    check(has(warnings(issues), "`## History` section with no table") && errors(issues).length === 0, "a History section with no table is a warning", dump(issues));
}
{
    const issues = validateDocument(ADR, record(""));
    check(has(warnings(issues), "has no `## History` section") && errors(issues).length === 0, "a record with no History section is a warning", dump(issues));
}
{
    const issues = historyHeaderIssues(ADR, record(section("```text\n| Date | Change |\n```\n\n" + THREE)), null);
    check(issues.length === 0, "a table inside a fence is not the History table", dump(issues));
}
{
    const issues = historyHeaderIssues(ADR, record(section("````markdown\n```\n| Date | Change |\n| --- | --- |\n```\n````\n\n" + THREE)), null);
    check(issues.length === 0, "a shorter fence line inside a longer fence does not close it", dump(issues));
}
{
    const issues = historyHeaderIssues(ADR, record(section("Date | Change\n--- | ---\n2026-10-20 | One store.\n")), null);
    check(has(issues, "table headed `Date | Change` on line"), "a table without a leading pipe is still read", dump(issues));
}
{
    const issues = historyHeaderIssues(ADR, record(section("Prose.\n") + "\n## Notes\n\n" + THREE), null);
    check(has(issues, "section with no table"), "a table under the next section is not the History table", dump(issues));
}
{
    const issues = historyHeaderIssues(ADR, record(section("|Date|Change|Why|\n|---|---|---|\n")), null);
    check(issues.length === 0, "the header is matched cell by cell, whatever the padding", dump(issues));
}
for (const [where, relPath, fileMeta] of [
    ["the adr/ index", ".devbook/arc42/adr/README.md", { index: "root" }],
    ["a debt record", ".devbook/arc42/tdr/3-no-retry-budget.md", {}],
    ["chapter 9", ".devbook/arc42/09-architecture-decisions.md", {}],
]) {
    check(historyHeaderIssues(relPath, record(section(TWO)), fileMeta).length === 0, `${where} owes no History header`);
}

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
