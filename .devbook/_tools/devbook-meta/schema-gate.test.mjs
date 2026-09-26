// Asserts the schema lint reaches the hard gate.
//
// `validateDocument` is the only implementation of the per-block rules, and for
// several releases nothing on the `build.mjs --check` path called it: the graph
// build collected reference and containment problems only, so a chapter with an
// out-of-ladder `status` exited 0 and merged. The three cases below are the ones
// that slipped through, so each is asserted against `buildGraph` — the function
// the gate actually runs — rather than against the validator in isolation.
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
const find = (problems, severity, needle) =>
    problems.find((p) => p.severity === severity && p.message.includes(needle));

const repoRoot = await mkdtemp(path.join(tmpdir(), "devbook-schema-gate-"));
try {
    await mkdir(path.join(repoRoot, ".devbook", "tech"), { recursive: true });
    await mkdir(path.join(repoRoot, ".devbook", "domain", "ordering"), { recursive: true });

    // An out-of-ladder rating. `.tech` has no resting value, so the ladder is
    // the whole of what `status` may say.
    await writeFile(
        path.join(repoRoot, ".devbook", "tech", "technology-graph.md"),
        `# Technology Graph\n\n${fence("status: nonsense-rung\n")}\nProse.\n`,
        "utf8"
    );

    // A structural heading with no block, which is legal and stays a warning.
    await writeFile(
        path.join(repoRoot, ".devbook", "domain", "ordering", "domain.md"),
        `# Ordering\n\n${fence("type: domain\n")}\n## Order\n\n` +
            `${fence("type: aggregate\n")}\n` +
            `Prose.\n\n### Notes\n\nProse.\n`,
        "utf8"
    );

    // `feature-flag` resolving to an aggregate instead of a `feature-flag`
    // chapter: the reference is well-formed and resolves, so only the graph
    // build — which knows the target's kind — can see what is wrong with it.
    // Beside it a `setting` reference that resolves correctly and must produce
    // a `gated-by` edge. Written on a `feature` chapter, where both fields are
    // in scope.
    await writeFile(
        path.join(repoRoot, ".devbook", "domain", "ordering", "features.md"),
        `# Ordering Features\n\n${fence("type: features\n")}\n## Refunds\n\n` +
            `${fence("type: feature\nfeature-flag: [.devbook/domain/ordering/domain.md#order]\nsetting: .devbook/domain/ordering/context.md#refund-notices\n")}\n` +
            `Prose.\n`,
        "utf8"
    );
    await writeFile(
        path.join(repoRoot, ".devbook", "domain", "ordering", "context.md"),
        `# Ordering\n\n${fence("index: root\ntype: context\n")}\nBoundary.\n\n## Refund notices\n\n` +
            `${fence("type: setting\nkey: notifications.refund\nscope: user\n")}\n` +
            `Prose.\n`,
        "utf8"
    );

    const { problems, edges } = await buildGraph(repoRoot);

    check(
        Boolean(find(problems, "error", 'has status "nonsense-rung"')),
        "an out-of-ladder `status` is an error on the gate path",
        JSON.stringify(problems, null, 2)
    );

    check(
        Boolean(find(problems, "error", "resolves to a `aggregate` chapter, not a `feature-flag` chapter")),
        "a `feature-flag` reference resolving to the wrong kind is an error on the gate path",
        JSON.stringify(problems, null, 2)
    );

    check(
        edges.some((e) => e.type === "configured-by" && e.target === ".devbook/domain/ordering/context.md#refund-notices"),
        "a `setting` reference produces a `configured-by` edge",
        JSON.stringify(edges.filter((e) => e.type === "configured-by"), null, 2)
    );

    check(
        Boolean(find(problems, "warning", "has no `meta` block")),
        "a heading with no `meta` block is reported, at warning severity",
        JSON.stringify(problems, null, 2)
    );

    // The severity split is what keeps the gate usable: a structural heading
    // must never fail a pull request, and a bad rung must always fail one.
    check(
        problems.filter((p) => p.severity === "error").length === 2,
        "only the two real violations carry error severity",
        JSON.stringify(problems.filter((p) => p.severity === "error"), null, 2)
    );
} finally {
    await rm(repoRoot, { recursive: true, force: true });
}

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
