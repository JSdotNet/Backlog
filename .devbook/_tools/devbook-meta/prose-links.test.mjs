// A Markdown link in chapter prose is checked the way `related` is: a relative
// target with no file behind it, or an anchor no heading in an indexed file
// renders, is a warning. Nothing else reads prose, which is how a link to a
// deleted chapter outlived it.
//
// Warnings only, so the check never fails on one. Skipped: absolute URLs,
// links in code spans and in any fence — the `meta` block and `annotation`
// fences included — and a file outside the corpus that exists, whose anchors
// are not this tool's to know.
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { buildGraph } from "./graph.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};

const fence = (body) => "```meta\n" + body + "```\n";
const REL = ".devbook/arc42/09-architecture-decisions.md";

const TARGET =
    `# Introduction\n\n${fence("type: introduction\n")}\n` +
    `## Quality Goals\n\n${fence("type: quality-goals\n")}\n` +
    `### Payload\n\nFirst.\n\n### Payload\n\nSecond, which GitHub anchors as payload-1.\n`;

async function graphOf(body) {
    const root = await mkdtemp(path.join(tmpdir(), "devbook-links-"));
    try {
        await mkdir(path.join(root, ".devbook/arc42"), { recursive: true });
        await mkdir(path.join(root, "plugins/devbook"), { recursive: true });
        await writeFile(path.join(root, ".devbook/arc42/01-introduction-and-goals.md"), TARGET, "utf8");
        await writeFile(path.join(root, "plugins/devbook/README.md"), "# Devbook\n", "utf8");
        await writeFile(
            path.join(root, REL),
            `# Architecture Decisions\n\n${fence("type: architecture-decisions\n")}\n${body}\n`,
            "utf8"
        );
        return await buildGraph(root);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

const linkWarnings = (graph) => graph.problems.filter((p) => p.message.includes(" links to "));

// ── What resolves ───────────────────────────────────────────────────────────

const clean = await graphOf(
    [
        "See [the goals](01-introduction-and-goals.md#quality-goals) and [intro](01-introduction-and-goals.md#introduction).",
        "The [second payload](01-introduction-and-goals.md#payload-1), [this file](#architecture-decisions).",
        "An [angle-bracket link](<01-introduction-and-goals.md>) and [a titled one](01-introduction-and-goals.md \"Goals\").",
        "The [plugin](../../plugins/devbook/README.md#any-anchor-at-all), [root-relative](/plugins/devbook/README.md).",
        "The [site](https://example.com/missing.md), [mail](mailto:a@b.c), [protocol-relative](//example.com/x).",
        "[ref]: 01-introduction-and-goals.md#quality-goals",
    ].join("\n\n")
);
check(linkWarnings(clean).length === 0, "links to existing files and rendered anchors raise nothing", JSON.stringify(linkWarnings(clean)));

// ── What does not ───────────────────────────────────────────────────────────

const broken = await graphOf(
    [
        "Moved to [authority](authority.md).",
        "See [the goals](01-introduction-and-goals.md#quality-goal).",
        "Back [up](#no-such-heading).",
        "Gone [plugin](../../plugins/retired/README.md).",
        "![diagram](images/missing.svg)",
        "[ref]: missing-reference.md",
    ].join("\n\n")
);
const messages = linkWarnings(broken).map((p) => p.message);
check(linkWarnings(broken).every((p) => p.severity === "warning"), "every broken link is a warning");
check(
    messages.some((m) => m.includes('"authority.md"') && m.includes(".devbook/arc42/authority.md does not exist")),
    "a link to a missing file inside .devbook warns"
);
check(
    messages.some((m) => m.includes('"#quality-goal"')),
    "a <file>#anchor whose file renders no such anchor warns",
    messages.join("\n")
);
check(messages.some((m) => m.includes('"#no-such-heading"')), "a same-file #anchor with no heading warns");
check(messages.some((m) => m.includes("plugins/retired/README.md does not exist")), "a missing file outside .devbook warns");
check(messages.some((m) => m.includes("images/missing.svg")), "a missing image warns");
check(messages.some((m) => m.includes("missing-reference.md")), "a reference definition to a missing file warns");
check(messages.every((m) => m.startsWith(`${REL}:`)), "every warning names the file and line", messages.join("\n"));
check(messages.length === 6, "one warning per broken link", messages.join("\n"));

// ── Code and fences are not prose ───────────────────────────────────────────

const quoted = await graphOf(
    [
        "Write it as `[label](missing.md)` or ``[x](also-missing.md)``.",
        "```markdown\n[label](missing-in-fence.md)\n```",
        "~~~\n[label](missing-in-tilde.md)\n~~~",
        "```annotation\nid: n1\nkind: question\nstatus: open\nquote: x\n---\nSee [old](missing-in-note.md).\n```",
    ].join("\n\n")
);
check(linkWarnings(quoted).length === 0, "links in code spans, fences, and annotation fences are skipped", JSON.stringify(linkWarnings(quoted)));

// ── Never fails the check ───────────────────────────────────────────────────

check(
    !broken.problems.some((p) => p.severity === "error" && p.message.includes(" links to ")),
    "no broken link is reported at error severity"
);

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
