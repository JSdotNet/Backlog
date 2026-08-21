// Exercises escapeSequenceIssues against the corruption seen in JSdotNet/Backlog
// plus the legitimate uses that must NOT be flagged.
import { escapeSequenceIssues } from "./metadata.mjs";

const cases = [
    {
        name: "the real defect: `r`n glueing a heading onto the previous line",
        expect: true,
        text: "Some prose about enums.`r`n`r`n## Shared Enums\n",
    },
    {
        name: "bare `r`n with no heading after it",
        expect: true,
        text: "First line.`r`nSecond line.\n",
    },
    {
        name: "C-style \\n in prose",
        expect: true,
        text: "The record ends.\\n\\n## Terms\n",
    },
    {
        name: "escape inside a fenced code block (legitimate)",
        expect: false,
        text: "Example:\n\n```powershell\n\"a`r`nb\"\n```\n\nDone.\n",
    },
    {
        name: "escape inside a tilde fence (legitimate)",
        expect: false,
        text: "Example:\n\n~~~\nprintf \"a\\nb\"\n~~~\n\nDone.\n",
    },
    {
        name: "C-style escape inside an inline code span (legitimate)",
        expect: false,
        text: "Separate the fields with `\\n` when writing the file.\n",
    },
    {
        name: "ordinary prose with backticked identifiers",
        expect: false,
        text: "The `n` parameter and the `r` flag are unrelated.\n",
    },
    {
        name: "a Windows path, which must not look like an escape",
        expect: false,
        text: "Stored under `C:\\repos\\knowledge` on disk.\n",
    },
    {
        name: "a tab escape in an unformatted path is deliberately not matched",
        expect: false,
        text: "Stored under C:\\temp\\build on disk.\n",
    },
    {
        name: "a doubly-backticked escape, i.e. a doc describing the lint",
        expect: false,
        text: "Flag a literal ``\x60r\x60n`` sequence in body text.\n",
    },
    {
        name: "clean document",
        expect: false,
        text: "# Order Management\n\n## Order\n\nThe consistency boundary.\n",
    },
];

let failed = 0;
for (const c of cases) {
    const issues = escapeSequenceIssues(c.text);
    const flagged = issues.length > 0;
    const ok = flagged === c.expect;
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${c.name}`);
    for (const i of issues) console.log(`        [${i.severity}] ${i.message}`);
}
console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
