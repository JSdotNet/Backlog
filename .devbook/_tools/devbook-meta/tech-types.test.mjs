// Asserts the `.tech` chapter vocabulary: the three AI-development words
// `.ai` already allows — `plugin`, `skill`, `agent` — type a tech chapter too,
// and a word outside the list is still an error.
import { validateDocument } from "./metadata.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};

const fence = (body) => "```meta\n" + body + "```\n";
const techFile = (type) =>
    `# AI Development\n\n${fence("status: adopted\n")}\nLead.\n\n` +
    `## Some Technology\n\n${fence(`type: ${type}\nstatus: adopted\n`)}\nProse.\n`;
const typeErrors = (issues) => issues.filter((i) => i.severity === "error" && i.message.includes("type"));

for (const type of ["plugin", "skill", "agent", "language", "format"]) {
    const issues = validateDocument(".devbook/tech/ai-development.md", techFile(type));
    check(typeErrors(issues).length === 0, `a tech chapter typed \`${type}\` validates`, JSON.stringify(issues));
}

const unknown = validateDocument(".devbook/tech/ai-development.md", techFile("gadget"));
check(typeErrors(unknown).length > 0, "a tech chapter typed with an unknown word is an error", JSON.stringify(unknown));

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
